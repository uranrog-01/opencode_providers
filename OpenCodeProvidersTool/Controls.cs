using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>Rounded card with an optional icon and title, the base surface of the design.</summary>
    internal class CardPanel : Panel
    {
        private string _title = "";
        private string _hint = "";

        public int Radius = 12;
        public Color Fill = Theme.Card;
        public Glyph? HeaderIcon = null;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Page;
        }

        public string Title
        {
            get { return _title; }
            set { _title = value ?? ""; Invalidate(); }
        }

        /// <summary>Right-aligned dim text on the title row.</summary>
        public string Hint
        {
            get { return _hint; }
            set { _hint = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.S(Radius, g);

            // Modern card: a barely-there vertical lift so large cards do not read flat.
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var brush = new LinearGradientBrush(body, Theme.Card, Theme.Mix(Theme.Card, Theme.CardAlt, 0.35f), 90f))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawPath(pen, path);
            }

            // 1px top sheen separating the card from the page ground.
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var sheen = new Pen(Color.FromArgb(14, Color.White)))
            {
                g.DrawPath(sheen, path);
            }

            float x = Theme.S(16, g);

            if (HeaderIcon.HasValue)
            {
                float size = Theme.S(15, g);
                Icons.Draw(g, HeaderIcon.Value, new RectangleF(x, Theme.S(15, g), size, size), Theme.TextDim);
                x += size + Theme.S(8, g);
            }

            if (_title.Length > 0)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(_title, Theme.BodyBold, brush,
                        new RectangleF(x, Theme.S(14, g), Width - x - Theme.S(16, g), Theme.S(18, g)), Theme.Left);
                }
            }

            if (_hint.Length > 0)
            {
                using (var brush = new SolidBrush(Theme.TextFaint))
                {
                    g.DrawString(_hint, Theme.Small, brush,
                        new RectangleF(Theme.S(16, g), Theme.S(14, g), Width - Theme.S(32, g), Theme.S(18, g)), Theme.Right);
                }
            }
        }
    }

    /// <summary>Compact metric tile: label, big value, right-aligned caption.</summary>
    internal class StatTile : Control
    {
        private string _label = "";
        private string _value = "0";
        private string _caption = "";
        private Color _valueColor = Theme.Text;
        private Font _valueFont = Theme.Stat;

        public StatTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardAlt;
        }

        /// <summary>Font for the value; drop to <see cref="Theme.StatSm"/> for word values.</summary>
        public Font ValueFont
        {
            get { return _valueFont; }
            set { _valueFont = value ?? Theme.Stat; Invalidate(); }
        }

        public string Label
        {
            get { return _label; }
            set { _label = value ?? ""; Invalidate(); }
        }

        public string Value
        {
            get { return _value; }
            set { _value = value ?? ""; Invalidate(); }
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value ?? ""; Invalidate(); }
        }

        public Color ValueColor
        {
            get { return _valueColor; }
            set { _valueColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(8, g)))
            using (var brush = new SolidBrush(BackColor))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(8, g)))
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawPath(pen, path);
            }

            float pad = Theme.S(13, g);
            float valueTop = Theme.S(24, g);
            float valueHeight = Height - valueTop - Theme.S(10, g);

            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(_label, Theme.Small, brush,
                    new RectangleF(pad, Theme.S(7, g), Width - pad * 2, Theme.S(16, g)), Theme.Left);
            }

            if (_caption.Length > 0)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(_caption, Theme.Small, brush,
                        new RectangleF(pad, valueTop, Width - pad * 2, valueHeight), Theme.Right);
                }
            }

            using (var brush = new SolidBrush(_valueColor))
            {
                g.DrawString(_value, _valueFont, brush,
                    new RectangleF(pad - Theme.S(1, g), valueTop, Width - pad * 2 + Theme.S(2, g), valueHeight), Theme.Left);
            }
        }
    }

    /// <summary>Small rounded status chip with a leading dot.</summary>
    internal class Pill : Control
    {
        private string _text = "";
        private Color _color = Theme.Ok;
        private bool _dot = true;

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        public string Caption
        {
            get { return _text; }
            set { _text = value ?? ""; Invalidate(); }
        }

        public Color Color
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        public bool ShowDot
        {
            get { return _dot; }
            set { _dot = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(Height / 2, g)))
            {
                using (var brush = new SolidBrush(Color.FromArgb(30, _color)))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(70, _color)))
                {
                    g.DrawPath(pen, path);
                }
            }

            float x = Theme.S(10, g);
            if (_dot)
            {
                Icons.DrawDot(g, x + Theme.S(3, g), Height / 2f, Theme.S(3, g), _color);
                x += Theme.S(11, g);
            }

            using (var brush = new SolidBrush(_color))
            {
                g.DrawString(_text, Theme.Small, brush,
                    new RectangleF(x, 0, Width - x - Theme.S(10, g), Height), Theme.Left);
            }
        }
    }

    internal enum ButtonStyle { Primary, Subtle, Ghost, Danger }

    /// <summary>Flat rounded button with optional icon, hover and pressed feedback.</summary>
    internal class FlatButton : Button
    {
        /// <summary>Same settings used for measuring and for drawing, so text never ellipsizes early.</summary>
        private static readonly StringFormat Label = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        private bool _hover;
        private bool _down;

        public ButtonStyle Style = ButtonStyle.Subtle;
        public Glyph? Icon;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.BodyBold;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            Color fill, border, text;
            switch (Style)
            {
                case ButtonStyle.Primary:
                    fill = _down ? Theme.Mix(Theme.Accent, Color.Black, 0.2f)
                         : _hover ? Theme.Mix(Theme.Accent, Color.White, 0.22f) : Theme.Accent;
                    border = fill;
                    text = Theme.AccentText;
                    break;
                case ButtonStyle.Danger:
                    fill = _down ? Theme.Mix(Theme.CardAlt, Theme.Danger, 0.3f)
                         : _hover ? Theme.Mix(Theme.CardAlt, Theme.Danger, 0.2f) : Theme.CardAlt;
                    border = Color.FromArgb(_hover ? 140 : 70, Theme.Danger);
                    text = Theme.Danger;
                    break;
                case ButtonStyle.Ghost:
                    fill = _down ? Theme.CardAlt : _hover ? Theme.Mix(Theme.Card, Theme.CardAlt, 0.7f) : Color.Transparent;
                    border = Color.Transparent;
                    text = _hover ? Theme.Text : Theme.TextDim;
                    break;
                default:
                    fill = _down ? Theme.Field : _hover ? Theme.CardHot : Theme.CardAlt;
                    border = _hover ? Theme.BorderHot : Theme.Border;
                    text = Theme.Text;
                    break;
            }

            if (!Enabled)
            {
                if (Style == ButtonStyle.Primary)
                {
                    fill = Theme.Mix(Theme.Accent, Theme.Card, 0.6f);
                    border = Theme.Border;
                    text = Theme.TextFaint;
                }
                else if (Style == ButtonStyle.Danger)
                {
                    // Disabled danger must still read as a delete button, not vanish
                    // into the card background.
                    fill = Theme.Mix(Theme.CardAlt, Theme.Card, 0.35f);
                    border = Color.FromArgb(80, Theme.Danger);
                    text = Color.FromArgb(150, Theme.Danger);
                }
                else
                {
                    fill = Theme.Card;
                    border = Theme.Border;
                    text = Theme.TextFaint;
                }
            }

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(body, Theme.S(8, g)))
            {
                if (fill != Color.Transparent)
                {
                    using (var brush = new SolidBrush(fill))
                    {
                        g.FillPath(brush, path);
                    }
                }
                if (border != Color.Transparent)
                {
                    using (var pen = new Pen(border))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            // Centre icon + label as one group, giving the label all the space that is left
            // so a slightly generous measurement cannot ellipsize it.
            float gap = Icon.HasValue && Text.Length > 0 ? Theme.S(7, g) : 0;
            float iconSize = Theme.S(15, g);
            float iconWidth = Icon.HasValue ? iconSize : 0;
            SizeF label = g.MeasureString(Text, Font, new SizeF(4000, Height), Label);
            float total = iconWidth + gap + label.Width;
            float startX = Math.Max(Theme.S(4, g), (Width - total) / 2f);
            float textX = startX;

            if (Icon.HasValue)
            {
                Icons.Draw(g, Icon.Value,
                    new RectangleF(startX, (Height - iconSize) / 2f, iconSize, iconSize), text);
                textX += iconWidth + gap;
            }

            using (var brush = new SolidBrush(text))
            {
                g.DrawString(Text, Font, brush,
                    new RectangleF(textX, 0, Math.Max(0, Width - textX), Height), Label);
            }
        }
    }

    /// <summary>Inset rounded panel that hosts a list, giving the models column its own surface.</summary>
    internal class ListPanel : Panel
    {
        public ListPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.S(10, g);

            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var brush = new SolidBrush(Theme.Field))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Rounded input surface hosting a borderless TextBox, with optional icons.</summary>
    internal class FieldBox : Panel
    {
        private const int EM_SETCUEBANNER = 0x1501;
        private const int WM_NCHITTEST = 0x0084;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private readonly TextBox _input;
        private string _placeholder = "";
        private bool _focused;
        private bool _hot;
        private bool _readOnly;
        private bool _trailingHot;
        private float _scale = 1f;

        public Glyph? LeadingIcon;
        public Glyph? TrailingIcon;

        public event EventHandler ValueChanged;
        public event EventHandler TrailingIconClicked;

        public FieldBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;

            _input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
                Font = Theme.Body
            };
            _input.TextChanged += (s, e) => { if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); };
            _input.GotFocus += (s, e) => { _focused = true; Invalidate(); };
            _input.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            _input.MouseEnter += (s, e) => { _hot = true; Invalidate(); };
            _input.MouseLeave += (s, e) => { _hot = false; Invalidate(); };
            Controls.Add(_input);
        }

        public TextBox Inner { get { return _input; } }

        protected int Px(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        public string Value
        {
            get { return _input.Text; }
            set { _input.Text = value ?? ""; }
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value ?? "";
                if (_input.IsHandleCreated)
                {
                    SendMessage(_input.Handle, EM_SETCUEBANNER, (IntPtr)1, _placeholder);
                }
            }
        }

        public bool Secret
        {
            get { return _input.UseSystemPasswordChar; }
            set { _input.UseSystemPasswordChar = value; }
        }

        public bool ReadOnlyField
        {
            get { return _readOnly; }
            set
            {
                _readOnly = value;
                _input.ReadOnly = value;
                _input.ForeColor = value ? Theme.TextDim : Theme.Text;
                Invalidate();
            }
        }

        public void FocusInput()
        {
            _input.Focus();
        }

        private int LeftInset
        {
            get { return Px(LeadingIcon.HasValue ? 34 : 11); }
        }

        private int RightInset
        {
            get { return Px(TrailingIcon.HasValue ? 34 : 11); }
        }

        private Rectangle TrailingBounds
        {
            get { return new Rectangle(Width - Px(30), (Height - Px(20)) / 2, Px(22), Px(20)); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = Theme.DpiFactor(this);
            if (_placeholder.Length > 0)
            {
                SendMessage(_input.Handle, EM_SETCUEBANNER, (IntPtr)1, _placeholder);
            }
            LayoutInput();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInput();
        }

        private void LayoutInput()
        {
            _input.Left = LeftInset;
            _input.Width = Math.Max(10, Width - LeftInset - RightInset);
            _input.Top = Math.Max(0, (Height - _input.Height) / 2);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = TrailingIcon.HasValue && TrailingBounds.Contains(e.Location);
            if (hot != _trailingHot)
            {
                _trailingHot = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _trailingHot = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (TrailingIcon.HasValue && TrailingBounds.Contains(e.Location))
            {
                if (TrailingIconClicked != null) TrailingIconClicked(this, EventArgs.Empty);
                return;
            }
            FocusInput();
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            var body = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Px(7);
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var brush = new SolidBrush(Theme.Field))
            {
                g.FillPath(brush, path);
            }

            Color border = _focused ? Theme.Focus : (_hot ? Theme.BorderHot : Theme.Border);
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var pen = new Pen(border))
            {
                g.DrawPath(pen, path);
            }

            if (_focused)
            {
                Theme.Glow(g, body, Theme.Focus, radius, 3);
            }

            float iconSize = Px(15);
            if (LeadingIcon.HasValue)
            {
                Icons.Draw(g, LeadingIcon.Value,
                    new RectangleF(Px(11), (Height - iconSize) / 2f, iconSize, iconSize), Theme.TextFaint);
            }

            if (TrailingIcon.HasValue)
            {
                Rectangle bounds = TrailingBounds;
                Icons.Draw(g, TrailingIcon.Value,
                    new RectangleF(bounds.X + (bounds.Width - iconSize) / 2f, (Height - iconSize) / 2f, iconSize, iconSize),
                    _trailingHot ? Theme.Text : Theme.TextFaint);
            }
        }
    }

    /// <summary>Sliding on/off switch, matching the reference design's enable toggles.</summary>
    internal class ToggleSwitch : Control
    {
        private bool _checked = true;
        private bool _hover;
        private float _scale = 1f;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            Size = new Size(40, 22);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = Theme.DpiFactor(this);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            int h = (int)Math.Round(22 * _scale);
            int w = (int)Math.Round(40 * _scale);
            int x = (Width - w) / 2;
            int y = (Height - h) / 2;
            var track = new Rectangle(x, y, w, h);

            Color trackColor = _checked
                ? (_hover ? Color.White : Theme.Accent)
                : (_hover ? Theme.BorderHot : Theme.CardHot);

            using (GraphicsPath path = Theme.Rounded(track, h / 2))
            using (var brush = new SolidBrush(trackColor))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(track, h / 2))
            using (var pen = new Pen(_checked ? trackColor : Theme.Border))
            {
                g.DrawPath(pen, path);
            }

            int knobSize = h - (int)Math.Round(6 * _scale);
            int knobX = _checked ? track.Right - knobSize - (int)Math.Round(3 * _scale)
                                 : track.Left + (int)Math.Round(3 * _scale);
            int knobY = track.Top + (h - knobSize) / 2;

            using (var brush = new SolidBrush(_checked ? Theme.AccentText : Theme.TextDim))
            {
                g.FillEllipse(brush, knobX, knobY, knobSize, knobSize);
            }
        }
    }

    /// <summary>Caption-bar button drawn as a glyph.</summary>
    internal class WindowButton : Control
    {
        private bool _hover;

        public Glyph Icon = Glyph.Minimize;
        public bool DangerOnHover = true;

        public WindowButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            bool danger = _hover && DangerOnHover && Icon == Glyph.Close;
            if (_hover)
            {
                using (var brush = new SolidBrush(danger ? Theme.Danger : Theme.CardAlt))
                {
                    g.FillRectangle(brush, ClientRectangle);
                }
            }

            float size = Theme.S(15, g);
            Color color = danger ? Color.White : (_hover ? Theme.Text : Theme.TextDim);
            Icons.Draw(g, Icon,
                new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size), color);
        }
    }

    /// <summary>Custom caption bar: brand mark and wordmark. Hosts the window buttons.</summary>
    internal class TitleBarPanel : Panel
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        public string Brand = "OpenCode";
        public string Subtitle = "";

        public TitleBarPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sidebar;
            Height = 46;
        }

        /// <summary>
        /// Reports HTTRANSPARENT so hit testing falls through to the form. Without this the
        /// bar swallows WM_NCHITTEST, the form never answers HTCAPTION, and the window
        /// cannot be dragged or resized by its top edge.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);

            using (var pen = new Pen(Theme.Border))
            {
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }

            float iconSize = Theme.S(19, g);
            Icons.Draw(g, Glyph.Logo, new RectangleF(Theme.S(15, g), (Height - iconSize) / 2f, iconSize, iconSize), Theme.Text);

            float textX = Theme.S(15, g) + iconSize + Theme.S(9, g);
            using (var brush = new SolidBrush(Theme.Text))
            {
                g.DrawString(Brand, Theme.BodyBold, brush,
                    new RectangleF(textX, 0, Width - textX - Theme.S(200, g), Height), Theme.Left);
            }

            if (Subtitle.Length > 0)
            {
                textX += Theme.MeasureTracked(g, Brand, Theme.BodyBold, 0) + Theme.S(4, g);
                using (var brush = new SolidBrush(Theme.TextFaint))
                {
                    g.DrawString(Subtitle, Theme.Body, brush,
                        new RectangleF(textX, 0, Width - textX - Theme.S(200, g), Height), Theme.Left);
                }
            }
        }
    }

    /// <summary>Dark owner-drawn tooltip; the stock one renders in system colours.</summary>
    internal class DarkToolTip : ToolTip
    {
        public DarkToolTip()
        {
            OwnerDraw = true;
            UseAnimation = false;
            UseFading = false;
            InitialDelay = 220;
            ReshowDelay = 80;
            AutoPopDelay = 30000;
            Draw += OnDraw;
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var brush = new SolidBrush(Theme.CardAlt))
            {
                g.FillRectangle(brush, 0, 0, e.Bounds.Width, e.Bounds.Height);
            }
            using (var pen = new Pen(Theme.BorderHot))
            {
                g.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            // Draw with the font the tooltip measured with, so wrapped text cannot clip.
            Font font = e.Font == null ? SystemFonts.DefaultFont : e.Font;
            TextRenderer.DrawText(g, e.ToolTipText, font,
                new Rectangle(0, 0, e.Bounds.Width, e.Bounds.Height), Theme.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }

    /// <summary>Themed checkbox with a label, used by the Smart add dialog.</summary>
    internal class CheckControl : Control
    {
        private bool _checked;
        private bool _hover;
        private float _scale = 1f;

        public event EventHandler CheckedChanged;

        public CheckControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            Size = new Size(220, 22);
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = Theme.DpiFactor(this);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.SurfaceOf(this));

            int box = (int)Math.Round(15 * _scale);
            var rect = new Rectangle(0, (Height - box) / 2, box, box);
            using (GraphicsPath path = Theme.Rounded(rect, (int)Math.Round(4 * _scale)))
            using (var brush = new SolidBrush(_checked ? Theme.Focus : (_hover ? Theme.CardHot : Theme.Field)))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(rect, (int)Math.Round(4 * _scale)))
            using (var pen = new Pen(_checked ? Theme.Focus : Theme.BorderHot))
            {
                g.DrawPath(pen, path);
            }

            if (_checked)
            {
                float tick = box * 0.72f;
                Icons.Draw(g, Glyph.Check,
                    new RectangleF(rect.X + (box - tick) / 2f, rect.Y + (box - tick) / 2f, tick, tick), Color.White);
            }

            float textLeft = box + (float)Math.Round(9 * _scale);
            using (var brush = new SolidBrush(_hover ? Theme.Text : Theme.TextDim))
            {
                g.DrawString(Text, Theme.Small, brush,
                    new RectangleF(textLeft, 0, Width - textLeft, Height), Theme.Left);
            }
        }
    }
}
