using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>
    /// Soft drop shadow parked directly beneath a dialog. A borderless form is clipped to
    /// its own region, so the dialog cannot paint outside itself; this is a separate
    /// click-through layered window that follows the dialog around instead.
    /// </summary>
    internal class ShadowWindow : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_ALPHA = 1;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOACTIVATE = 0x0010;

        /// <summary>Room reserved around the dialog for the shadow to fade into.</summary>
        private const int Pad = 18;

        /// <summary>Pushes the shadow down a little, so the light reads as coming from above.</summary>
        private const int DropY = 5;

        private const int MaxAlpha = 170;
        private const int BlurRadius = 5;
        private const int BlurPasses = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
            int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        private readonly Form _target;
        private readonly int _radius;
        private Bitmap _shadow;
        private Size _painted = Size.Empty;

        public ShadowWindow(Form target, int radius)
        {
            _target = target;
            _radius = radius;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            Text = "";
        }

        /// <summary>Never take focus away from the dialog this shadow belongs to.</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        /// <summary>Repaints if the dialog changed size, then parks under it again.</summary>
        public void Follow()
        {
            if (_target == null || !_target.IsHandleCreated || !IsHandleCreated) return;

            int width = _target.Width + Pad * 2;
            int height = _target.Height + Pad * 2 + DropY;
            var size = new Size(width, height);

            // The blur is the expensive part, so it is only redone when the size changes;
            // moving a dialog just re-blits the cached bitmap.
            if (_shadow == null || _painted != size)
            {
                if (_shadow != null) _shadow.Dispose();
                _shadow = Build(width, height, _target.Width, _target.Height);
                _painted = size;
            }

            var destination = new POINT { X = _target.Left - Pad, Y = _target.Top - Pad };
            Blit(destination, new SIZE { cx = width, cy = height });

            // Inserting the shadow after the dialog puts it immediately below, which is
            // all that keeps it from covering the window it is drawn for.
            SetWindowPos(Handle, _target.Handle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void Blit(POINT destination, SIZE size)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            // A black shadow is RGB 0 throughout, so premultiplication is a no-op and the
            // alpha channel is the only thing that carries the shape.
            IntPtr handle = _shadow.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memoryDc, handle);
            try
            {
                var source = new POINT { X = 0, Y = 0 };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc,
                    ref source, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memoryDc, previous);
                DeleteObject(handle);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private Bitmap Build(int width, int height, int dialogWidth, int dialogHeight)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var card = new Rectangle(Pad, Pad + DropY, dialogWidth, dialogHeight);
                using (GraphicsPath path = Theme.Rounded(card, _radius))
                using (var brush = new SolidBrush(Color.FromArgb(MaxAlpha, 0, 0, 0)))
                {
                    g.FillPath(brush, path);
                }
            }
            Blur(bitmap);
            return bitmap;
        }

        /// <summary>Successive box passes approximate a Gaussian, which reads as a soft shadow.</summary>
        private static void Blur(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = stride * height;
                var pixels = new byte[bytes];
                Marshal.Copy(data.Scan0, pixels, 0, bytes);

                var alpha = new byte[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++) alpha[y * width + x] = pixels[row + x * 4 + 3];
                }

                var scratch = new byte[width * height];
                for (int pass = 0; pass < BlurPasses; pass++)
                {
                    BlurAcross(alpha, scratch, width, height);
                    BlurDown(scratch, alpha, width, height);
                }

                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++) pixels[row + x * 4 + 3] = alpha[y * width + x];
                }
                Marshal.Copy(pixels, 0, data.Scan0, bytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void BlurAcross(byte[] source, byte[] target, int width, int height)
        {
            int span = BlurRadius * 2 + 1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int sum = 0;
                for (int k = -BlurRadius; k <= BlurRadius; k++) sum += source[row + Clamp(k, width)];
                for (int x = 0; x < width; x++)
                {
                    target[row + x] = (byte)(sum / span);
                    sum += source[row + Clamp(x + BlurRadius + 1, width)]
                         - source[row + Clamp(x - BlurRadius, width)];
                }
            }
        }

        private static void BlurDown(byte[] source, byte[] target, int width, int height)
        {
            int span = BlurRadius * 2 + 1;
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int k = -BlurRadius; k <= BlurRadius; k++) sum += source[Clamp(k, height) * width + x];
                for (int y = 0; y < height; y++)
                {
                    target[y * width + x] = (byte)(sum / span);
                    sum += source[Clamp(y + BlurRadius + 1, height) * width + x]
                         - source[Clamp(y - BlurRadius, height) * width + x];
                }
            }
        }

        /// <summary>Edge samples clamp to the border, so the shadow does not darken at the rim.</summary>
        private static int Clamp(int value, int limit)
        {
            if (value < 0) return 0;
            if (value >= limit) return limit - 1;
            return value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _shadow != null)
            {
                _shadow.Dispose();
                _shadow = null;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Borderless dark dialog that matches the app instead of the native chrome. Layout is
    /// done in device pixels scaled by <see cref="Px"/> so it stays correct on high-DPI screens.
    /// </summary>
    internal class DarkDialog : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        protected readonly Panel Body;
        private readonly WindowButton _close;
        private readonly string _title;
        private readonly float _scale = 1f;
        private ShadowWindow _shadow;

        public DarkDialog(string title, int width, int height)
        {
            _title = title ?? "";

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icons.CreateAppIcon(); }
            catch { }
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Touch the handle so DeviceDpi is meaningful before laying anything out.
            IntPtr unused = Handle;
            _scale = Theme.DpiFactor(this);

            ClientSize = new Size(Px(width), Px(height));

            Body = new Panel
            {
                Location = new Point(0, Px(50)),
                Size = new Size(ClientSize.Width, ClientSize.Height - Px(50)),
                BackColor = Theme.Card
            };
            Controls.Add(Body);

            _close = new WindowButton
            {
                Icon = Glyph.Close,
                Location = new Point(ClientSize.Width - Px(44), Px(6)),
                Size = new Size(Px(38), Px(32))
            };
            _close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_close);

            UpdateRegion();
        }

        /// <summary>Design pixels to device pixels.</summary>
        protected int Px(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        protected void ResizeBody(int width, int height)
        {
            ClientSize = new Size(width, height);
            Body.Size = new Size(width, height - Px(50));
            _close.Location = new Point(width - Px(44), Px(6));
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            using (GraphicsPath path = Theme.Rounded(new Rectangle(0, 0, Width, Height), Px(12)))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (var brush = new SolidBrush(Theme.Card))
            {
                g.FillRectangle(brush, ClientRectangle);
            }
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            float iconSize = Px(15);
            Icons.Draw(g, Glyph.Logo, new RectangleF(Px(16), (Px(50) - iconSize) / 2f, iconSize, iconSize), Theme.TextDim);

            using (var brush = new SolidBrush(Theme.Text))
            {
                g.DrawString(_title, Theme.BodyBold, brush,
                    new RectangleF(Px(16) + iconSize + Px(9), 0, Width - Px(120), Px(50)), Theme.Left);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int x = unchecked((short)(long)m.LParam);
                    int y = unchecked((short)((long)m.LParam >> 16));
                    Point p = PointToClient(new Point(x, y));
                    if (p.Y <= Px(50)) m.Result = (IntPtr)HTCAPTION;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // -------------------------------------------------------------------- shadow

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_shadow == null) _shadow = new ShadowWindow(this, Px(12));
            _shadow.Show();
            _shadow.Follow();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (_shadow != null) _shadow.Follow();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_shadow != null) _shadow.Follow();
        }

        /// <summary>Re-asserts the parked order, in case activating re-shuffled the windows.</summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_shadow != null) _shadow.Follow();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_shadow != null)
            {
                _shadow.Dispose();
                _shadow = null;
            }
            base.OnFormClosed(e);
        }
    }

    internal enum MessageKind { Info, Warning, Error, Question }

    internal enum MessageButtons { Ok, SaveDiscardCancel, YesNo }

    /// <summary>Themed replacement for MessageBox.</summary>
    internal class MessageDialog : DarkDialog
    {
        private DialogResult _result = DialogResult.Cancel;

        private MessageDialog(string title, string message, MessageKind kind, MessageButtons buttons, string primaryText)
            : base(title, 500, 150)
        {
            Color accent = kind == MessageKind.Error ? Theme.Danger
                : kind == MessageKind.Warning ? Theme.Warn
                : kind == MessageKind.Question ? Theme.Text
                : Theme.Focus;

            Glyph glyph = kind == MessageKind.Error || kind == MessageKind.Warning ? Glyph.Alert : Glyph.Info;

            int pad = Px(22);
            int width = ClientSize.Width;
            int iconSize = Px(22);
            int textLeft = pad + iconSize + Px(14);
            int textWidth = width - textLeft - pad;

            Size measured = TextRenderer.MeasureText(message, Theme.Body,
                new Size(textWidth, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int textHeight = Math.Max(measured.Height + Px(4), iconSize);

            int height = Px(50) + Px(18) + textHeight + Px(24) + Px(38) + Px(22);
            ResizeBody(width, height);

            var icon = new IconPanel
            {
                Glyph = glyph,
                GlyphColor = accent,
                Location = new Point(pad, Px(18)),
                Size = new Size(iconSize, iconSize)
            };
            Body.Controls.Add(icon);

            var text = new Label
            {
                Text = message,
                Location = new Point(textLeft, Px(18)),
                Size = new Size(textWidth, textHeight),
                ForeColor = Theme.Text,
                Font = Theme.Body,
                BackColor = Theme.Card
            };
            Body.Controls.Add(text);

            int buttonY = height - Px(50) - Px(38) - Px(22);
            int right = width - pad;

            var primary = new FlatButton
            {
                Text = primaryText,
                Style = ButtonStyle.Primary,
                Size = new Size(Px(150), Px(38)),
                Location = new Point(right - Px(150), buttonY)
            };
            primary.Click += (s, e) => { _result = DialogResult.OK; Close(); };
            Body.Controls.Add(primary);

            var middle = new FlatButton
            {
                Text = buttons == MessageButtons.SaveDiscardCancel ? "Discard" : "No",
                Style = ButtonStyle.Danger,
                Size = new Size(Px(120), Px(38)),
                Location = new Point(right - Px(150) - Px(12) - Px(120), buttonY)
            };
            middle.Click += (s, e) => { _result = DialogResult.No; Close(); };
            Body.Controls.Add(middle);

            var cancel = new FlatButton
            {
                Text = "Cancel",
                Style = ButtonStyle.Ghost,
                Size = new Size(Px(110), Px(38)),
                Location = new Point(right - Px(150) - Px(12) - Px(120) - Px(12) - Px(110), buttonY)
            };
            cancel.Click += (s, e) => { _result = DialogResult.Cancel; Close(); };
            Body.Controls.Add(cancel);

            if (buttons == MessageButtons.Ok)
            {
                middle.Visible = false;
                cancel.Visible = false;
                primary.Location = new Point(right - Px(130), buttonY);
                primary.Size = new Size(Px(130), Px(38));
            }
            else if (buttons == MessageButtons.YesNo)
            {
                middle.Text = "No";
                cancel.Visible = false;
            }

            AcceptButton = primary;
            CancelButton = cancel;
        }

        /// <summary>Save / Discard / Cancel. OK means "save", No means "discard".</summary>
        public static DialogResult AskSave(IWin32Window owner, string message)
        {
            using (var dialog = new MessageDialog("Unsaved changes", message, MessageKind.Question,
                MessageButtons.SaveDiscardCancel, "Save changes"))
            {
                dialog.ShowDialog(owner);
                return dialog._result;
            }
        }

        public static DialogResult Show(IWin32Window owner, string title, string message, MessageKind kind)
        {
            using (var dialog = new MessageDialog(title, message, kind, MessageButtons.Ok, "OK"))
            {
                dialog.ShowDialog(owner);
                return dialog._result;
            }
        }

        /// <summary>Yes / No question. Returns true only when the primary button is pressed.</summary>
        public static bool Confirm(IWin32Window owner, string title, string message)
        {
            return Confirm(owner, title, message, "Yes, delete");
        }

        /// <summary>Yes / No question with a custom primary button label.</summary>
        public static bool Confirm(IWin32Window owner, string title, string message, string primaryText)
        {
            using (var dialog = new MessageDialog(title, message, MessageKind.Warning,
                MessageButtons.YesNo, primaryText))
            {
                dialog.ShowDialog(owner);
                return dialog._result == DialogResult.OK;
            }
        }
    }

    /// <summary>Draws a single icon, used for dialog decorations.</summary>
    internal class IconPanel : Control
    {
        public Glyph Glyph = Glyph.Info;
        public Color GlyphColor = Theme.TextDim;

        public IconPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            e.Graphics.Clear(Theme.SurfaceOf(this));
            Icons.Draw(e.Graphics, Glyph, new RectangleF(0, 0, Width, Height), GlyphColor);
        }
    }

    /// <summary>Themed single-line input dialog.</summary>
    internal class TextPromptDialog : DarkDialog
    {
        private readonly FieldBox _field;
        private string _value = "";

        private TextPromptDialog(string title, string question, string initial)
            : base(title, 470, 250)
        {
            int pad = Px(22);
            int width = ClientSize.Width;

            var label = new Label
            {
                Text = question,
                Location = new Point(pad, Px(12)),
                Size = new Size(width - pad * 2, Px(20)),
                ForeColor = Theme.TextDim,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(label);

            _field = new FieldBox
            {
                Location = new Point(pad, Px(38)),
                Size = new Size(width - pad * 2, Px(36))
            };
            _field.Value = initial ?? "";
            Body.Controls.Add(_field);

            var hint = new Label
            {
                Text = "This becomes the key under \"provider\" in the config file.",
                Location = new Point(pad, Px(82)),
                Size = new Size(width - pad * 2, Px(20)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(hint);

            int buttonY = Px(118);
            var ok = new FlatButton
            {
                Text = "Create",
                Style = ButtonStyle.Primary,
                Size = new Size(Px(130), Px(38)),
                Location = new Point(width - pad - Px(130), buttonY)
            };
            ok.Click += (s, e) => { _value = _field.Value.Trim(); DialogResult = DialogResult.OK; Close(); };
            Body.Controls.Add(ok);

            var cancel = new FlatButton
            {
                Text = "Cancel",
                Style = ButtonStyle.Ghost,
                Size = new Size(Px(110), Px(38)),
                Location = new Point(width - pad - Px(130) - Px(12) - Px(110), buttonY)
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Body.Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => _field.FocusInput();
        }

        public static string Show(IWin32Window owner, string title, string question, string initial)
        {
            using (var dialog = new TextPromptDialog(title, question, initial))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._value : "";
            }
        }
    }

    /// <summary>What the config picker came back with.</summary>
    internal class ConfigChoice
    {
        /// <summary>The chosen config path, or "" when nothing was chosen.</summary>
        public string Path = "";

        /// <summary>The user asked for the system file dialog instead.</summary>
        public bool Browse;

        public bool Cancelled
        {
            get { return !Browse && Path.Length == 0; }
        }
    }

    /// <summary>
    /// Offers the config files that were actually found on this machine, instead of dropping
    /// the user into a file dialog to go hunting. Anything else is still reachable through
    /// Browse. With nothing found it defers to the file dialog straight away.
    /// </summary>
    internal class ConfigPickerDialog : DarkDialog
    {
        private readonly ConfigList _list;
        private readonly ConfigChoice _choice = new ConfigChoice();

        private ConfigPickerDialog(string installSummary, string current, List<ConfigInfo> configs)
            : base("Open config", 660, 540)
        {
            int pad = Px(22);
            int width = ClientSize.Width;

            Body.Controls.Add(Line("Config files found on this machine", pad, 10,
                width - pad * 2, 20, Theme.BodyBold, Theme.Text));
            Body.Controls.Add(Line(installSummary, pad, 32,
                width - pad * 2, 18, Theme.Small, Theme.TextFaint));

            var box = new ListPanel
            {
                Location = new Point(pad, Px(60)),
                Size = new Size(width - pad * 2, Px(358))
            };
            _list = new ConfigList { Dock = DockStyle.Fill };
            foreach (ConfigInfo info in configs) _list.Items.Add(info);
            _list.ItemActivated += (s, e) => Accept();
            box.Controls.Add(_list);
            Body.Controls.Add(box);

            // Preselect what is already open, so the common case is one click.
            _list.SelectedItem = FindCurrent(configs, current);

            var open = new FlatButton
            {
                Text = "Open",
                Style = ButtonStyle.Primary,
                Size = new Size(Px(130), Px(38)),
                Location = new Point(width - pad - Px(130), Px(430))
            };
            open.Click += (s, e) => Accept();
            Body.Controls.Add(open);

            var browse = new FlatButton
            {
                Text = "Browse\u2026",
                Style = ButtonStyle.Subtle,
                Size = new Size(Px(120), Px(38)),
                Location = new Point(width - pad - Px(130) - Px(12) - Px(120), Px(430))
            };
            browse.Click += (s, e) => { _choice.Browse = true; DialogResult = DialogResult.OK; Close(); };
            Body.Controls.Add(browse);

            var cancel = new FlatButton
            {
                Text = "Cancel",
                Style = ButtonStyle.Ghost,
                Size = new Size(Px(110), Px(38)),
                Location = new Point(width - pad - Px(130) - Px(12) - Px(120) - Px(12) - Px(110), Px(430))
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Body.Controls.Add(cancel);

            AcceptButton = open;
            CancelButton = cancel;
        }

        private static ConfigInfo FindCurrent(List<ConfigInfo> configs, string current)
        {
            if (!string.IsNullOrEmpty(current))
            {
                foreach (ConfigInfo info in configs)
                {
                    if (string.Equals(info.Path, current, StringComparison.OrdinalIgnoreCase)) return info;
                }
            }
            return configs.Count > 0 ? configs[0] : null;
        }

        private void Accept()
        {
            var info = _list.SelectedItem as ConfigInfo;
            if (info == null) return;
            _choice.Path = info.Path;
            DialogResult = DialogResult.OK;
            Close();
        }

        private Label Line(string text, int x, int y, int width, int height, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, Px(y)),
                Size = new Size(width, Px(height)),
                ForeColor = color,
                Font = font,
                BackColor = Theme.Card
            };
        }

        /// <summary>Shows the picker, or reports Browse when there is nothing to list.</summary>
        public static ConfigChoice Pick(IWin32Window owner, string installSummary, string current)
        {
            List<ConfigInfo> configs = OpenCodeLocator.FindConfigs();
            if (configs.Count == 0)
            {
                var empty = new ConfigChoice { Browse = true };
                return empty;
            }

            using (var dialog = new ConfigPickerDialog(installSummary, current, configs))
            {
                dialog.ShowDialog(owner);
                return dialog._choice;
            }
        }
    }

    /// <summary>Small about popup: what this tool is, and who made it.</summary>
    internal class AboutDialog : DarkDialog
    {
        private AboutDialog(string installSummary)
            : base("About", 470, 368)
        {
            int pad = Px(22);
            int width = ClientSize.Width;
            int textLeft = pad + Px(44) + Px(14);
            int textWidth = width - textLeft - pad;

            var logo = new IconPanel
            {
                Glyph = Glyph.Logo,
                GlyphColor = Theme.Text,
                Location = new Point(pad, Px(20)),
                Size = new Size(Px(44), Px(44))
            };
            Body.Controls.Add(logo);

            Body.Controls.Add(Line("OpenCode Providers Tool", textLeft, 22, textWidth, 22,
                Theme.BodyBold, Theme.Text));
            Body.Controls.Add(Line("A free tool for the OpenCode app", textLeft, 44, textWidth, 20,
                Theme.Small, Theme.TextDim));
            Body.Controls.Add(Line("Version " + VersionText, textLeft, 64, textWidth, 20,
                Theme.Small, Theme.TextFaint));

            Body.Controls.Add(new Panel
            {
                Location = new Point(pad, Px(104)),
                Size = new Size(width - pad * 2, 1),
                BackColor = Theme.Border
            });

            Body.Controls.Add(Line("Created by zer0ne", pad, 122, width - pad * 2, 24,
                Theme.BodyBold, Theme.Text));
            Body.Controls.Add(Line(
                "Edit providers, models and token limits in one window, "
                + "without hand-editing the JSON.", pad, 150, width - pad * 2, 44,
                Theme.Small, Theme.TextDim));

            Body.Controls.Add(Line("DETECTED", pad, 202, width - pad * 2, 16,
                Theme.Micro, Theme.TextFaint));
            Body.Controls.Add(Line(installSummary, pad, 220, width - pad * 2, 34,
                Theme.Small, Theme.TextDim));

            var close = new FlatButton
            {
                Text = "Close",
                Style = ButtonStyle.Primary,
                Size = new Size(Px(130), Px(38)),
                Location = new Point(width - pad - Px(130), Px(276))
            };
            close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Body.Controls.Add(close);

            AcceptButton = close;
            CancelButton = close;
        }

        private Label Line(string text, int x, int y, int width, int height, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, Px(y)),
                Size = new Size(width, Px(height)),
                ForeColor = color,
                Font = font,
                BackColor = Theme.Card
            };
        }

        /// <summary>The assembly version, minus any build-metadata suffix.</summary>
        private static string VersionText
        {
            get
            {
                string text = Application.ProductVersion ?? "";
                int plus = text.IndexOf('+');
                if (plus >= 0) text = text.Substring(0, plus);
                return text.Length > 0 ? text : "1.0";
            }
        }

        /// <summary>
        /// Named "ShowAbout" rather than "Show": a one-argument Show would shadow the
        /// inherited <see cref="Form.Show(IWin32Window)"/>.
        /// </summary>
        public static void ShowAbout(IWin32Window owner, string installSummary)
        {
            using (var dialog = new AboutDialog(installSummary)) dialog.ShowDialog(owner);
        }
    }

    /// <summary>
    /// Model editor with Smart configuration. Recommendations are looked up on models.dev
    /// by model ID, the provider's Base URL and its API format; a field the user edits
    /// becomes manually managed and stops following updates until the form is reset.
    /// </summary>
    internal class ModelDialog : DarkDialog
    {
        private const string SmartHelp =
            "Matches recommended configuration using the model ID, Base URL, and API format. " +
            "Recommendations come from models.dev and are refreshed on every save. When you " +
            "change a setting manually, that setting becomes manually managed and stops " +
            "following recommendation updates; other settings remain managed by smart " +
            "configuration.";

        private const string ContextHelp =
            "Maximum tokens the model can read at once, stored as limit.context. Smart " +
            "configuration keeps it in sync with models.dev unless you edit the value.";

        private const string OutputHelp =
            "Maximum tokens the model can generate in one response, stored as limit.output. " +
            "Smart configuration keeps it in sync with models.dev unless you edit the value.";

        private readonly FieldBox _idField;
        private readonly FieldBox _contextField;
        private readonly FieldBox _outputField;
        private readonly FieldBox _nameField;
        private readonly ToggleSwitch _smartToggle;
        private readonly Label _statusLabel;
        private readonly Label _hintLabel;
        private readonly Label _contextMode;
        private readonly Label _outputMode;
        private readonly Label _nameMode;
        private readonly DarkToolTip _tips = new DarkToolTip();
        private readonly System.Windows.Forms.Timer _debounce;

        private readonly string _providerId;
        private readonly string _baseUrl;
        private readonly string _npm;

        private bool _populating;
        private bool _hooked;
        private CatalogMatch _match = new CatalogMatch();

        private string _modelId = "";
        private string _modelName = "";
        private string _context = "";
        private string _output = "";
        private bool _manualName;
        private bool _manualContext;
        private bool _manualOutput;

        public ModelDialog(ProviderEntry provider, ModelEntry existing, SmartModelState state)
            : base(existing == null ? "Add model" : "Edit model settings", 560, 496)
        {
            _providerId = provider == null ? "" : provider.Id;
            _baseUrl = provider == null ? "" : provider.BaseUrl;
            _npm = provider == null ? "" : provider.Npm;

            SmartModelState smart = state == null ? new SmartModelState() : state;
            _manualName = smart.ManualName;
            _manualContext = smart.ManualContext;
            _manualOutput = smart.ManualOutput;

            int pad = Px(22);
            int width = ClientSize.Width;

            // ------------------------------------------------------- smart row
            var smartCaption = new Label
            {
                Text = "Smart configuration",
                Location = new Point(pad, Px(13)),
                Size = new Size(Px(125), Px(20)),
                ForeColor = Theme.Text,
                Font = Theme.Body,
                BackColor = Theme.Card
            };
            Body.Controls.Add(smartCaption);

            var smartHelp = new HelpGlyph
            {
                Location = new Point(pad + Px(132), Px(15)),
                Size = new Size(Px(15), Px(15))
            };
            string smartTip = DialogText.Wrap(SmartHelp, 64);
            _tips.SetToolTip(smartHelp, smartTip);
            smartHelp.Click += (s, e) => _tips.Show(smartTip, smartHelp, 14000);
            Body.Controls.Add(smartHelp);

            _smartToggle = new ToggleSwitch
            {
                Location = new Point(width - pad - Px(44), Px(11)),
                Size = new Size(Px(44), Px(24))
            };
            _smartToggle.Checked = smart.Smart;
            _smartToggle.CheckedChanged += SmartToggle_Changed;
            Body.Controls.Add(_smartToggle);

            // ------------------------------------------------------- model id row
            var idCaption = new Label
            {
                Text = "Model ID",
                Location = new Point(pad, Px(47)),
                Size = new Size(Px(160), Px(18)),
                ForeColor = Theme.TextDim,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(idCaption);

            _statusLabel = new Label
            {
                Location = new Point(pad + Px(160), Px(47)),
                Size = new Size(width - pad * 2 - Px(160), Px(18)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleRight
            };
            Body.Controls.Add(_statusLabel);

            _idField = new FieldBox
            {
                Location = new Point(pad, Px(69)),
                Size = new Size(width - pad * 2, Px(34)),
                Placeholder = "claude-opus-4-8"
            };
            _idField.Value = existing == null ? "" : existing.Id;
            _idField.ValueChanged += IdChanged;
            Body.Controls.Add(_idField);

            // ------------------------------------------------------- limits
            _contextField = AddLimitRow("Context window", "1000000", Px(115), ContextHelp, Px(100), out _contextMode, ContextChanged);
            _outputField = AddLimitRow("Max output tokens", "384000", Px(183), OutputHelp, Px(124), out _outputMode, OutputChanged);

            // ------------------------------------------------------- display name
            var nameCaption = new Label
            {
                Text = "Display name",
                Location = new Point(pad, Px(251)),
                Size = new Size(Px(160), Px(18)),
                ForeColor = Theme.TextDim,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(nameCaption);

            _nameMode = new Label
            {
                Location = new Point(width - pad - Px(74), Px(251)),
                Size = new Size(Px(74), Px(18)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleRight
            };
            Body.Controls.Add(_nameMode);

            _nameField = new FieldBox
            {
                Location = new Point(pad, Px(273)),
                Size = new Size(width - pad * 2, Px(34)),
                Placeholder = "Claude Opus 4.8"
            };
            _nameField.Value = existing == null ? "" : existing.DisplayName;
            _nameField.ValueChanged += NameChanged;
            Body.Controls.Add(_nameField);

            // ------------------------------------------------------- footer
            _hintLabel = new Label
            {
                Location = new Point(pad, Px(318)),
                Size = new Size(width - pad * 2, Px(48)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(_hintLabel);

            int buttonY = Px(382);
            var save = new FlatButton
            {
                Text = "Save",
                Style = ButtonStyle.Primary,
                Size = new Size(Px(120), Px(38)),
                Location = new Point(width - pad - Px(120), buttonY)
            };
            save.Click += Save_Click;
            Body.Controls.Add(save);

            var cancel = new FlatButton
            {
                Text = "Cancel",
                Style = ButtonStyle.Ghost,
                Size = new Size(Px(110), Px(38)),
                Location = new Point(width - pad - Px(120) - Px(12) - Px(110), buttonY)
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Body.Controls.Add(cancel);

            var reset = new FlatButton
            {
                Text = "Reset form",
                Style = ButtonStyle.Ghost,
                Font = Theme.Small,
                Size = new Size(Px(110), Px(34)),
                Location = new Point(pad, buttonY + Px(2))
            };
            reset.Click += (s, e) => ResetForm();
            Body.Controls.Add(reset);

            _debounce = new System.Windows.Forms.Timer { Interval = 450 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); ApplyLookup(); };

            AcceptButton = save;
            CancelButton = cancel;

            UpdateModeLabels();
            SetHint(DialogText.Wrap(IdleHint, 74), false);
        }

        private FieldBox AddLimitRow(string caption, string placeholder, int y, string help, int helpX,
            out Label mode, EventHandler changed)
        {
            int pad = Px(22);
            int width = ClientSize.Width;

            var label = new Label
            {
                Text = caption,
                Location = new Point(pad, y),
                Size = new Size(helpX - Px(6), Px(18)),
                ForeColor = Theme.TextDim,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(label);

            var glyph = new HelpGlyph
            {
                Location = new Point(pad + helpX, y + Px(1)),
                Size = new Size(Px(15), Px(15))
            };
            string tip = DialogText.Wrap(help, 64);
            _tips.SetToolTip(glyph, tip);
            glyph.Click += (s, e) => _tips.Show(tip, glyph, 14000);
            Body.Controls.Add(glyph);

            mode = new Label
            {
                Location = new Point(width - pad - Px(74), y),
                Size = new Size(Px(74), Px(18)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleRight
            };
            Body.Controls.Add(mode);

            var field = new FieldBox
            {
                Location = new Point(pad, y + Px(22)),
                Size = new Size(width - pad * 2, Px(34)),
                Placeholder = placeholder
            };
            field.ValueChanged += changed;
            Body.Controls.Add(field);
            return field;
        }

        // ------------------------------------------------------------------ results

        public string ModelId { get { return _modelId; } }

        public string ModelName { get { return _modelName; } }

        public string ContextWindow { get { return _context; } }

        public string MaxOutputTokens { get { return _output; } }

        public bool SmartConfiguration { get { return _smartToggle.Checked; } }

        public bool ManualName { get { return _manualName; } }

        public bool ManualContext { get { return _manualContext; } }

        public bool ManualOutput { get { return _manualOutput; } }

        /// <summary>models.dev match note, for the status line after saving.</summary>
        public string CatalogNote { get { return _match == null ? "" : _match.Note; } }

        // ------------------------------------------------------------------ loading

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_hooked)
            {
                _hooked = true;
                ModelCatalog.Updated += CatalogUpdated;
            }
            ApplyLookup();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hooked) ModelCatalog.Updated -= CatalogUpdated;
            _debounce.Stop();
            _debounce.Dispose();
            _tips.Dispose();
            base.OnFormClosed(e);
        }

        private void CatalogUpdated(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(ApplyLookup));
            }
            catch { }
        }

        // ------------------------------------------------------------------ fields

        private void IdChanged(object sender, EventArgs e)
        {
            if (_populating) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void ContextChanged(object sender, EventArgs e)
        {
            if (_populating) return;
            _manualContext = true;
            UpdateModeLabels();
        }

        private void OutputChanged(object sender, EventArgs e)
        {
            if (_populating) return;
            _manualOutput = true;
            UpdateModeLabels();
        }

        private void NameChanged(object sender, EventArgs e)
        {
            if (_populating) return;
            _manualName = true;
            UpdateModeLabels();
        }

        private void SmartToggle_Changed(object sender, EventArgs e)
        {
            if (_populating) return;
            ApplyLookup();
        }

        // ------------------------------------------------------------------ smart lookup

        private const string IdleHint =
            "Smart configuration looks the model ID up on models.dev, together with the " +
            "provider's Base URL and API format, and fills in the display name and token limits.";

        private void ApplyLookup()
        {
            if (!_smartToggle.Checked)
            {
                _match = new CatalogMatch();
                _statusLabel.Text = "Smart configuration is off";
                SetHint("Smart configuration is off. The display name and limits are stored exactly as entered.", false);
                UpdateModeLabels();
                return;
            }

            string id = _idField.Value.Trim();
            if (id.Length == 0)
            {
                _statusLabel.Text = "";
                SetHint(DialogText.Wrap(IdleHint, 74), false);
                UpdateModeLabels();
                return;
            }

            if (!ModelCatalog.HaveData)
            {
                bool failed = ModelCatalog.Error.Length > 0 && !ModelCatalog.IsLoading;
                _statusLabel.Text = failed ? "models.dev unavailable" : "Loading models.dev\u2026";
                SetHint(failed
                    ? "models.dev could not be reached. Enter the limits manually; recommendations resume when it is available."
                    : "Fetching recommendations from models.dev\u2026", failed);
                if (!failed) ModelCatalog.BeginLoad();
                UpdateModeLabels();
                return;
            }

            CatalogMatch match = ModelCatalog.FindModel(_providerId, _npm, _baseUrl, id);
            _match = match;

            if (!match.Found)
            {
                _statusLabel.Text = "No models.dev match";
                SetHint("No recommendation found for \"" + id + "\". Enter the limits manually, or check the model ID.", false);
                UpdateModeLabels();
                return;
            }

            _statusLabel.Text = "Matched " + (match.Provider != null ? match.Provider.Id : "models.dev");
            bool filled = false;
            if (!_manualName && match.Model.Name.Length > 0)
            {
                filled |= SetField(_nameField, match.Model.Name);
            }
            if (!_manualContext && match.Model.Context > 0)
            {
                filled |= SetField(_contextField, match.Model.Context.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (!_manualOutput && match.Model.Output > 0)
            {
                filled |= SetField(_outputField, match.Model.Output.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            SetHint("Matched " + match.Note + (filled
                ? ". Manual edits override individual limits."
                : ". Manual values are kept; Reset form follows the recommendation again."), false);
            UpdateModeLabels();
        }

        private bool SetField(FieldBox field, string value)
        {
            if (field.Value == value) return false;
            _populating = true;
            field.Value = value;
            _populating = false;
            return true;
        }

        private void UpdateModeLabels()
        {
            bool smart = _smartToggle.Checked;
            bool contextAuto = smart && !_manualContext;
            bool outputAuto = smart && !_manualOutput;
            bool nameAuto = smart && !_manualName;

            _contextMode.Text = contextAuto ? "auto" : "manual";
            _contextMode.ForeColor = contextAuto ? Theme.TextFaint : Theme.Warn;
            _outputMode.Text = outputAuto ? "auto" : "manual";
            _outputMode.ForeColor = outputAuto ? Theme.TextFaint : Theme.Warn;
            _nameMode.Text = nameAuto ? "auto" : "manual";
            _nameMode.ForeColor = nameAuto ? Theme.TextFaint : Theme.Warn;
        }

        private void SetHint(string text, bool warning)
        {
            _hintLabel.Text = text;
            _hintLabel.ForeColor = warning ? Theme.Warn : Theme.TextFaint;
        }

        private void ResetForm()
        {
            _populating = true;
            _manualName = false;
            _manualContext = false;
            _manualOutput = false;
            _smartToggle.Checked = true;
            _contextField.Value = "";
            _outputField.Value = "";
            _nameField.Value = "";
            _populating = false;

            ApplyLookup();
            if (_statusLabel.Text.Length == 0) SetHint("Form reset. Enter a model ID to look up recommendations.", false);
        }

        // ------------------------------------------------------------------ save

        private void Save_Click(object sender, EventArgs e)
        {
            string id = _idField.Value.Trim();
            if (id.Length == 0)
            {
                SetHint("Enter a model ID.", true);
                _idField.FocusInput();
                return;
            }
            if (!ValidCount(_contextField.Value))
            {
                SetHint("Context window must be a whole number of tokens.", true);
                _contextField.FocusInput();
                return;
            }
            if (!ValidCount(_outputField.Value))
            {
                SetHint("Max output tokens must be a whole number of tokens.", true);
                _outputField.FocusInput();
                return;
            }

            // OpenCode's schema requires limit.context and limit.output together, so a half
            // limit would be written as a config it rejects. Ask for both, or neither.
            bool hasContext = CleanCount(_contextField.Value).Length > 0;
            bool hasOutput = CleanCount(_outputField.Value).Length > 0;
            if (hasContext != hasOutput)
            {
                SetHint("Context and Max output go together: fill in both, or clear both.", true);
                if (hasContext) _outputField.FocusInput();
                else _contextField.FocusInput();
                return;
            }

            _modelId = id;
            _modelName = _nameField.Value.Trim();
            _context = CleanCount(_contextField.Value);
            _output = CleanCount(_outputField.Value);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool ValidCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string clean = text.Replace(",", "").Replace("_", "").Replace(" ", "").Trim();
            if (clean.Length == 0) return false;
            foreach (char c in clean) if (c < '0' || c > '9') return false;
            return true;
        }

        private static string CleanCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string clean = text.Replace(",", "").Replace("_", "").Replace(" ", "").Trim();
            foreach (char c in clean) if (c < '0' || c > '9') return "";
            return clean.TrimStart('0').Length == 0 ? "" : clean.TrimStart('0');
        }
    }

    /// <summary>
    /// Smart add: every model models.dev knows for this provider as a check list, so a
    /// whole catalog can be added and refreshed at once instead of one row at a time.
    /// </summary>
    internal class CatalogDialog : DarkDialog
    {
        private readonly ProviderEntry _provider;
        private readonly FieldBox _searchField;
        private readonly CatalogList _list;
        private readonly Label _header;
        private readonly Label _note;
        private readonly Label _countLabel;
        private readonly FlatButton _checkAll;
        private readonly FlatButton _checkNone;
        private readonly FlatButton _retry;
        private readonly FlatButton _apply;
        private readonly CheckControl _overwrite;
        private readonly List<CatalogRow> _rows = new List<CatalogRow>();

        private string _source = "";
        private bool _hooked;
        private bool _discovering;
        private DiscoveryResult _discovery;

        public CatalogDialog(ProviderEntry provider)
            : base("Smart add", 720, 648)
        {
            _provider = provider;

            int pad = Px(22);
            int width = ClientSize.Width;

            _header = new Label
            {
                Location = new Point(pad, Px(12)),
                Size = new Size(width - pad * 2, Px(20)),
                ForeColor = Theme.Text,
                Font = Theme.BodyBold,
                BackColor = Theme.Card
            };
            Body.Controls.Add(_header);

            _countLabel = new Label
            {
                Location = new Point(pad, Px(34)),
                Size = new Size(width - pad * 2, Px(16)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Body.Controls.Add(_countLabel);

            _searchField = new FieldBox
            {
                Location = new Point(pad, Px(58)),
                Size = new Size(width - pad * 2 - Px(184), Px(34)),
                LeadingIcon = Glyph.Search,
                Placeholder = "Search models"
            };
            _searchField.ValueChanged += (s, e) => ApplyFilter();
            Body.Controls.Add(_searchField);

            _checkAll = new FlatButton
            {
                Text = "All",
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Location = new Point(width - pad - Px(176), Px(60)),
                Size = new Size(Px(84), Px(30))
            };
            _checkAll.Click += (s, e) => SetAllChecked(true);
            Body.Controls.Add(_checkAll);

            _checkNone = new FlatButton
            {
                Text = "None",
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Location = new Point(width - pad - Px(84), Px(60)),
                Size = new Size(Px(84), Px(30))
            };
            _checkNone.Click += (s, e) => SetAllChecked(false);
            Body.Controls.Add(_checkNone);

            _retry = new FlatButton
            {
                Text = "Retry",
                Icon = Glyph.Refresh,
                Style = ButtonStyle.Subtle,
                Font = Theme.Small,
                Location = new Point(width - pad - Px(100), Px(60)),
                Size = new Size(Px(100), Px(30)),
                Visible = false
            };
            _retry.Click += (s, e) =>
            {
                _retry.Visible = false;
                _discovery = null;
                Start();
            };
            Body.Controls.Add(_retry);

            var listHost = new ListPanel
            {
                Location = new Point(pad, Px(104)),
                Size = new Size(width - pad * 2, Px(384))
            };
            _list = new CatalogList
            {
                Location = new Point(Px(7), Px(7)),
                Size = new Size(listHost.Width - Px(14), listHost.Height - Px(14))
            };
            _list.ItemActivated += (s, e) => ToggleSelected();
            listHost.Controls.Add(_list);
            Body.Controls.Add(listHost);

            _note = new Label
            {
                Location = new Point(pad, Px(494)),
                Size = new Size(width - pad * 2, Px(16)),
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                BackColor = Theme.Card
            };
            Body.Controls.Add(_note);

            _overwrite = new CheckControl
            {
                Location = new Point(pad, Px(516)),
                Size = new Size(width - pad * 2, Px(22)),
                Text = "Overwrite limits that were set manually",
                Visible = false
            };
            _overwrite.CheckedChanged += (s, e) => UpdateApply();
            Body.Controls.Add(_overwrite);

            int buttonY = Px(548);
            _apply = new FlatButton
            {
                Text = "Add checked",
                Style = ButtonStyle.Primary,
                Size = new Size(Px(180), Px(38)),
                Location = new Point(width - pad - Px(180), buttonY),
                Enabled = false
            };
            _apply.Click += Apply_Click;
            Body.Controls.Add(_apply);

            var cancel = new FlatButton
            {
                Text = "Cancel",
                Style = ButtonStyle.Ghost,
                Size = new Size(Px(110), Px(38)),
                Location = new Point(width - pad - Px(180) - Px(12) - Px(110), buttonY)
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Body.Controls.Add(cancel);

            AcceptButton = _apply;
            CancelButton = cancel;

            _header.Text = provider == null ? "Smart add" : (provider.DisplayName.Length > 0 ? provider.DisplayName : provider.Id);
            _countLabel.Text = "Reading the provider's models\u2026";
        }

        /// <summary>Rows the user checked that are not in the config yet.</summary>
        public List<CatalogRow> SelectedNew { get; private set; } = new List<CatalogRow>();

        /// <summary>Rows the user checked that already exist, to refresh from the catalog.</summary>
        public List<CatalogRow> SelectedExisting { get; private set; } = new List<CatalogRow>();

        /// <summary>True when the user asked to replace manually set limits too.</summary>
        public bool OverwriteLimits { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_hooked)
            {
                _hooked = true;
                ModelCatalog.Updated += CatalogUpdated;
            }
            Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hooked) ModelCatalog.Updated -= CatalogUpdated;
            base.OnFormClosed(e);
        }

        private void CatalogUpdated(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(Start));
            }
            catch { }
        }

        // ------------------------------------------------------------------ sources

        /// <summary>
        /// Smart add is anchored on the provider's Base URL: the live /models catalog is
        /// the list, and models.dev only fills metadata the provider does not return. When
        /// the endpoint cannot be read, models.dev's catalog for the matched provider is
        /// the fallback; a provider in neither shows an error, never the whole catalog.
        /// </summary>
        private void Start()
        {
            _header.Text = _provider.DisplayName.Length > 0 ? _provider.DisplayName : _provider.Id;

            if (_discovering) return;

            if (_discovery != null)
            {
                if (_discovery.Ok) BuildFromDiscovery();
                else BuildFromFallback();
                return;
            }

            string endpoint = ModelDiscovery.Endpoint(_provider.BaseUrl);
            if (endpoint.Length == 0)
            {
                BuildFromFallback();
                return;
            }

            _discovering = true;
            ShowBusy("Reading models from " + HostOf(endpoint) + "\u2026");
            ModelDiscovery.DiscoverAsync(_provider.BaseUrl, _provider.ApiKey, _provider.Npm)
                .ContinueWith(delegate (Task<DiscoveryResult> task)
                {
                    DiscoveryResult result;
                    try { result = task.Result; }
                    catch
                    {
                        result = new DiscoveryResult
                        {
                            Endpoint = endpoint,
                            Error = "The provider catalog request failed."
                        };
                    }

                    try
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        BeginInvoke(new Action(delegate
                        {
                            _discovering = false;
                            _discovery = result;
                            if (result.Ok) BuildFromDiscovery();
                            else BuildFromFallback();
                        }));
                    }
                    catch { }
                });
        }

        /// <summary>Rows come from the provider; missing name/limits come from models.dev.</summary>
        private void BuildFromDiscovery()
        {
            var models = new List<CatalogModel>();
            foreach (DiscoveredModel found in _discovery.Models)
            {
                CatalogMatch match = ModelCatalog.HaveData
                    ? ModelCatalog.FindModel(_provider.Id, _provider.Npm, _provider.BaseUrl, found.Id)
                    : new CatalogMatch();

                models.Add(new CatalogModel
                {
                    Id = found.Id,
                    Name = found.Name.Length > 0 ? found.Name : (match.Found ? match.Model.Name : ""),
                    Context = found.Context > 0 ? found.Context : (match.Found ? match.Model.Context : 0),
                    Output = found.Output > 0 ? found.Output : (match.Found ? match.Model.Output : 0)
                });
            }

            string note = "Live catalog \u00b7 " + _discovery.Endpoint;
            if (!ModelCatalog.HaveData) note += "  \u00b7  models.dev metadata follows when loaded";
            BuildRows(models, note);
        }

        /// <summary>models.dev's provider catalog, used only when the live call cannot be used.</summary>
        private void BuildFromFallback()
        {
            string discoveryError = _discovery != null ? _discovery.Error : "";

            if (!ModelCatalog.HaveData)
            {
                bool loading = ModelCatalog.IsLoading;
                _countLabel.Text = discoveryError.Length > 0 ? discoveryError : ModelCatalog.Status;
                ShowBusy(loading ? "Loading models.dev\u2026" : "Preparing models.dev\u2026");
                _retry.Visible = !loading && (ModelCatalog.Error.Length > 0 || discoveryError.Length > 0);
                if (!loading && ModelCatalog.Error.Length == 0) ModelCatalog.BeginLoad();
                return;
            }

            CatalogListing listing = ModelCatalog.ListModels(_provider.Id, _provider.Npm, _provider.BaseUrl);
            if (listing.Provider == null)
            {
                ShowBusy(discoveryError.Length > 0
                    ? discoveryError
                    : "No provider catalog and no models.dev match \u2014 check the Base URL and API key, then retry.");
                _retry.Visible = true;
                return;
            }

            var models = new List<CatalogModel>();
            foreach (CatalogModel model in listing.Models)
            {
                models.Add(new CatalogModel
                {
                    Id = model.Id,
                    Name = model.Name,
                    Context = model.Context,
                    Output = model.Output
                });
            }

            string note = "models.dev \u00b7 " + listing.Provider.Name + " (" + listing.Provider.Id + ")";
            if (discoveryError.Length > 0) note = discoveryError + "  \u00b7  " + note;
            BuildRows(models, note);
        }

        private void ShowBusy(string message)
        {
            _countLabel.Text = message;
            _checkAll.Visible = false;
            _checkNone.Visible = false;
            _searchField.Enabled = false;
            _apply.Enabled = false;
            _list.Items.Clear();
        }

        private void BuildRows(List<CatalogModel> models, string source)
        {
            _source = source;
            _retry.Visible = false;
            _checkAll.Visible = true;
            _checkNone.Visible = true;
            _searchField.Enabled = true;

            var existingById = new Dictionary<string, ModelEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelEntry model in _provider.Models) existingById[model.Id] = model;

            _rows.Clear();
            foreach (CatalogModel model in models)
            {
                string id = model.Id;
                ModelEntry entry = FindExisting(existingById, id);
                if (entry == null) entry = FindExisting(existingById, BareId(id));

                SmartModelState state = entry == null ? null : SmartStateStore.Get(_provider.Id, entry.Id);
                _rows.Add(new CatalogRow
                {
                    Model = model,
                    Id = entry != null ? entry.Id : id,
                    Existing = entry != null,
                    ExistingEntry = entry,
                    ManualLimits = state != null && (state.ManualContext || state.ManualOutput),
                    Checked = true
                });
            }

            // New models first so what can be added is obvious, then alphabetical.
            _rows.Sort(delegate (CatalogRow a, CatalogRow b)
            {
                if (a.Existing != b.Existing) return a.Existing ? 1 : -1;
                string an = a.Model.Name.Length > 0 ? a.Model.Name : a.Id;
                string bn = b.Model.Name.Length > 0 ? b.Model.Name : b.Id;
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });

            ApplyFilter();
        }

        private static string HostOf(string url)
        {
            string value = (url ?? "").Trim();
            int scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) value = value.Substring(scheme + 3);
            int slash = value.IndexOf('/');
            if (slash >= 0) value = value.Substring(0, slash);
            return value;
        }

        private static ModelEntry FindExisting(Dictionary<string, ModelEntry> map, string id)
        {
            ModelEntry entry;
            if (map.TryGetValue(id, out entry)) return entry;
            foreach (KeyValuePair<string, ModelEntry> pair in map)
            {
                if (string.Equals(BareId(pair.Key), id, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            return null;
        }

        private static string BareId(string id)
        {
            string value = (id ?? "").Trim();
            int slash = value.LastIndexOf('/');
            return slash >= 0 ? value.Substring(slash + 1) : value;
        }

        private void ApplyFilter()
        {
            string filter = _searchField.Value.Trim();
            _list.Items.Clear();

            int shown = 0;
            foreach (CatalogRow row in _rows)
            {
                if (filter.Length > 0 && !Matches(row, filter)) continue;
                _list.Items.Add(row);
                shown++;
            }

            string note = _source;
            if (filter.Length > 0) note += "  \u00b7  " + shown + " of " + _rows.Count + " shown";
            _note.Text = note;
            UpdateApply();
        }

        private static bool Matches(CatalogRow row, string filter)
        {
            return row.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Model.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Model.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetAllChecked(bool value)
        {
            foreach (object item in _list.Items.Raw)
            {
                var row = item as CatalogRow;
                if (row != null) row.Checked = value;
            }
            _list.Invalidate();
            UpdateApply();
        }

        private void ToggleSelected()
        {
            var row = _list.SelectedItem as CatalogRow;
            if (row == null) return;
            row.Checked = !row.Checked;
            _list.Invalidate();
            UpdateApply();
        }

        private void UpdateApply()
        {
            int added = 0, sync = 0;
            foreach (CatalogRow row in _rows)
            {
                if (!row.Checked) continue;
                if (row.Existing) sync++;
                else added++;
            }

            _apply.Enabled = added + sync > 0;
            _apply.Text = added == 0 && sync == 0 ? "Add checked"
                : "Add " + added + (sync > 0 ? "  \u00b7  sync " + sync : "");

            _countLabel.Text = _rows.Count + " models in the catalog   \u00b7   "
                + (added + sync) + " checked   \u00b7   " + added + " new";

            bool anyManual = false;
            foreach (CatalogRow row in _rows)
            {
                if (row.Checked && row.Existing && row.ManualLimits) { anyManual = true; break; }
            }
            _overwrite.Visible = anyManual;
        }

        private void Apply_Click(object sender, EventArgs e)
        {
            SelectedNew = new List<CatalogRow>();
            SelectedExisting = new List<CatalogRow>();

            foreach (CatalogRow row in _rows)
            {
                if (!row.Checked) continue;
                if (row.Existing) SelectedExisting.Add(row);
                else SelectedNew.Add(row);
            }
            if (SelectedNew.Count == 0 && SelectedExisting.Count == 0) return;

            OverwriteLimits = _overwrite.Visible && _overwrite.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Small clickable help glyph; clicking shows the long-form tooltip.</summary>
    internal class HelpGlyph : Control
    {
        private bool _hover;

        public HelpGlyph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));
            Icons.Draw(g, Glyph.Help, new RectangleF(0, 0, Width, Height), _hover ? Theme.Text : Theme.TextFaint);
        }
    }

    /// <summary>Text helpers for the dialogs.</summary>
    internal static class DialogText
    {
        /// <summary>Wraps at word boundaries; the stock tooltip does not wrap by itself.</summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] words = text.Split(' ');
            var builder = new System.Text.StringBuilder();
            int line = 0;
            foreach (string word in words)
            {
                if (line > 0 && line + 1 + word.Length > maxChars)
                {
                    builder.Append('\n');
                    line = 0;
                }
                else if (line > 0)
                {
                    builder.Append(' ');
                    line++;
                }
                builder.Append(word);
                line += word.Length;
            }
            return builder.ToString();
        }
    }
}
