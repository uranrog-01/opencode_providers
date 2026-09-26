using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>A choice with a friendly label and the raw value written to the config.</summary>
    internal class Option
    {
        public string Label;
        public string Value;

        public Option(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>Borderless popup holding the list of choices.</summary>
    internal class DropdownPopup : Form
    {
        private readonly SimpleList _list;
        private bool _picked;

        public DropdownPopup(IList<Option> options, int selectedIndex, int width, int maxHeight)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            // Above the app window even when that window is topmost, so the list is never
            // hidden behind the thing it belongs to.
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            float scale = DpiScale();
            int rowHeight = Theme.Scaled(30, scale);
            int rows = Math.Min(options.Count, 9);
            int height = Math.Max(rowHeight, rows * rowHeight) + Theme.Scaled(2, scale);
            if (height > maxHeight) height = maxHeight;

            ClientSize = new Size(width, height);

            _list = new SimpleList
            {
                Location = new Point(Theme.Scaled(1, scale), Theme.Scaled(1, scale)),
                Size = new Size(width - Theme.Scaled(2, scale), height - Theme.Scaled(2, scale))
            };
            foreach (Option option in options) _list.Items.Add(option);
            _list.SelectedIndex = selectedIndex;
            _list.ItemActivated += (s, e) => { _picked = true; Close(); };
            Controls.Add(_list);

            Deactivate += (s, e) => Close();
        }

        private float DpiScale()
        {
            IntPtr unused = Handle;
            return Theme.DpiFactor(this);
        }

        /// <summary>True only when the user committed a choice, not when the popup was dismissed.</summary>
        public bool Picked
        {
            get { return _picked; }
        }

        /// <summary>Index the user committed, or -1 when the popup was dismissed.</summary>
        public int ChosenIndex
        {
            get { return _picked ? _list.SelectedIndex : -1; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (var brush = new SolidBrush(Theme.Card))
            {
                g.FillRectangle(brush, ClientRectangle);
            }
            using (var pen = new Pen(Theme.BorderHot))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _list.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// Themed replacement for ComboBox. The stock control draws its drop-down arrow with
    /// system colours that cannot be changed, which stood out against the dark surface.
    /// </summary>
    internal class DropdownField : Control
    {
        private readonly List<Option> _options = new List<Option>();
        private int _selected = -1;
        private bool _hover;
        private bool _open;
        private DropdownPopup _popup;
        private float _scale = 1f;

        public event EventHandler SelectedIndexChanged;

        public DropdownField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
            Cursor = Cursors.Hand;
        }

        protected int Px(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = Theme.DpiFactor(this);
        }

        public int Count { get { return _options.Count; } }

        public void SetOptions(IEnumerable<Option> options)
        {
            _options.Clear();
            _options.AddRange(options);
            if (_selected >= _options.Count) _selected = _options.Count - 1;
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int next = value;
                if (next < -1) next = -1;
                if (next >= _options.Count) next = _options.Count - 1;
                if (next == _selected) return;
                _selected = next;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Currently shown label.</summary>
        public string SelectedLabel
        {
            get { return _selected >= 0 && _selected < _options.Count ? _options[_selected].Label : ""; }
        }

        /// <summary>Raw value written to the config; setting an unknown value selects nothing.</summary>
        public string SelectedValue
        {
            get { return _selected >= 0 && _selected < _options.Count ? _options[_selected].Value : ""; }
            set
            {
                for (int i = 0; i < _options.Count; i++)
                {
                    if (string.Equals(_options[i].Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedIndex = i;
                        return;
                    }
                }
                SelectedIndex = -1;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        // Opened on mouse-up, not mouse-down: the popup takes activation, and the trailing
        // mouse-up of the opening click would otherwise land on the main form and close it.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            TogglePopup();
            base.OnMouseUp(e);
        }

        private void TogglePopup()
        {
            if (_open)
            {
                ClosePopup();
                return;
            }
            if (_options.Count == 0) return;

            int maxHeight = Px(300);
            var popup = new DropdownPopup(_options, _selected, Width, maxHeight);

            Point below = PointToScreen(new Point(0, Height + Px(4)));
            Screen screen = Screen.FromControl(this);
            int height = popup.Height;
            if (below.Y + height > screen.WorkingArea.Bottom)
            {
                below = PointToScreen(new Point(0, -height - Px(4)));
            }
            popup.Location = below;

            _open = true;
            _popup = popup;

            // The handler must close over the local, not the field: closing raises FormClosed
            // more than once (the item pick and the resulting deactivate), and a second pass
            // through a field that has already been cleared would dereference null.
            popup.FormClosed += (s, e) =>
            {
                if (!ReferenceEquals(_popup, popup)) return;
                int chosen = popup.ChosenIndex;
                _popup = null;
                _open = false;
                Invalidate();
                if (chosen >= 0) SelectedIndex = chosen;
            };
            popup.Show();
        }

        private void ClosePopup()
        {
            DropdownPopup popup = _popup;
            if (popup == null) return;
            _popup = null;
            _open = false;
            popup.Close();
            Invalidate();
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

            Color border = _open ? Theme.Focus : (_hover ? Theme.BorderHot : Theme.Border);
            using (GraphicsPath path = Theme.Rounded(body, radius))
            using (var pen = new Pen(border))
            {
                g.DrawPath(pen, path);
            }

            string text = SelectedLabel;
            using (var brush = new SolidBrush(text.Length > 0 ? Theme.Text : Theme.TextFaint))
            {
                g.DrawString(text, Theme.Body, brush,
                    new RectangleF(Px(12), 0, Width - Px(40), Height), Theme.Left);
            }

            float size = Px(14);
            Icons.Draw(g, Glyph.ChevronDown,
                new RectangleF(Width - Px(28), (Height - size) / 2f, size, size),
                _hover || _open ? Theme.Text : Theme.TextFaint);
        }
    }
}
