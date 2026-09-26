using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>Non-selectable group heading inside a list, e.g. "Providers" / "Disabled".</summary>
    internal class ListGroupRow
    {
        public string Title = "";
        public ListGroupRow(string title) { Title = title; }
        public override string ToString() { return Title; }
    }

    /// <summary>
    /// Scrollable list drawn entirely by us. A ListBox was the obvious choice, but its
    /// scrollbar is drawn by the OS in light grey and cannot be themed, which looked out
    /// of place and overlapped the right-hand column.
    /// </summary>
    internal abstract class DarkList : Control
    {
        public sealed class ItemCollection
        {
            private readonly DarkList _owner;
            private readonly List<object> _items = new List<object>();

            internal ItemCollection(DarkList owner) { _owner = owner; }

            internal List<object> Raw { get { return _items; } }

            public int Count { get { return _items.Count; } }

            public object this[int index] { get { return _items[index]; } }

            public void Clear()
            {
                _items.Clear();
                _owner.ResetView();
            }

            public void Add(object item)
            {
                _items.Add(item);
                _owner.ClampScroll();
                _owner.Invalidate();
            }

            public void AddRange(IEnumerable<object> items)
            {
                _items.AddRange(items);
                _owner.ClampScroll();
                _owner.Invalidate();
            }
        }

        private readonly ItemCollection _items;
        private int _selected = -1;
        private int _hover = -1;
        private int _offset;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartOffset;
        private bool _thumbHot;
        private float _scale = 1f;

        public int RowHeight = 34;
        public Color SurfaceColor = Theme.Card;
        public Color HighlightColor = Theme.CardAlt;

        public event EventHandler SelectedIndexChanged;

        /// <summary>Raised on click-release or Enter, i.e. when the user commits a choice.</summary>
        public event EventHandler ItemActivated;

        private int _pressed = -1;

        protected DarkList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card;
            TabStop = true;
            _items = new ItemCollection(this);
        }

        public ItemCollection Items { get { return _items; } }

        public abstract string EmptyMessage { get; }

        protected abstract void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index);

        /// <summary>Group headings are shown but cannot be selected.</summary>
        protected virtual bool IsSelectable(object item)
        {
            return !(item is ListGroupRow);
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

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int next = value;
                if (next < -1) next = -1;
                if (next >= _items.Count) next = _items.Count - 1;
                if (next == _selected) return;
                _selected = next;
                EnsureVisible(_selected);
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : null; }
            set
            {
                int index = -1;
                if (value != null)
                {
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (ReferenceEquals(_items[i], value)) { index = i; break; }
                    }
                }
                SelectedIndex = index;
            }
        }

        /// <summary>Selects the first selectable row, used after repopulating the list.</summary>
        public void SelectFirst()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (IsSelectable(_items[i]))
                {
                    SelectedIndex = i;
                    return;
                }
            }
            SelectedIndex = -1;
        }

        internal void ResetView()
        {
            _selected = -1;
            _hover = -1;
            _offset = 0;
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        private int ContentHeight { get { return _items.Count * RowHeight; } }

        private int MaxOffset { get { return Math.Max(0, ContentHeight - Height); } }

        private bool NeedsScrollBar { get { return ContentHeight > Height; } }

        private int ScrollBarWidth { get { return Px(6); } }

        /// <summary>Row area width, leaving room for the scrollbar so columns never sit under it.</summary>
        private int ContentWidth
        {
            get { return Width - (NeedsScrollBar ? ScrollBarWidth + Px(4) : 0); }
        }

        internal void ClampScroll()
        {
            if (_offset > MaxOffset) _offset = MaxOffset;
            if (_offset < 0) _offset = 0;
        }

        private void EnsureVisible(int index)
        {
            if (index < 0) return;
            int top = index * RowHeight;
            int bottom = top + RowHeight;
            if (top < _offset) _offset = top;
            else if (bottom > _offset + Height) _offset = bottom - Height;
            ClampScroll();
        }

        private void ScrollBy(int delta)
        {
            _offset += delta;
            ClampScroll();
            Invalidate();
        }

        // ------------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(SurfaceColor);

            if (_items.Count == 0)
            {
                if (EmptyMessage.Length > 0)
                {
                    using (var brush = new SolidBrush(Theme.TextFaint))
                    {
                        g.DrawString(EmptyMessage, Theme.Small, brush,
                            new RectangleF(Px(14), Px(12), Width - Px(28), Px(40)), Theme.Left);
                    }
                }
                return;
            }

            int firstRow = _offset / RowHeight;
            int y = -(int)(_offset % RowHeight);

            for (int i = firstRow; i < _items.Count && y < Height; i++)
            {
                var row = new Rectangle(0, y, ContentWidth, RowHeight);
                var clip = new Rectangle(row.X, Math.Max(row.Y, 0), row.Width, Math.Min(RowHeight, Height - row.Y));
                if (clip.Height <= 0) { y += RowHeight; continue; }

                GraphicsState state = g.Save();
                g.SetClip(clip, CombineMode.Intersect);
                DrawRow(g, row, _items[i], i == _selected, i == _hover && !_dragging, i);
                g.Restore(state);

                y += RowHeight;
            }

            DrawEdgeFade(g);
            DrawScrollBar(g);
        }

        /// <summary>Softens the partially visible first and last rows so clipping looks deliberate.</summary>
        private void DrawEdgeFade(Graphics g)
        {
            int band = Px(16);
            if (band <= 0 || Height < band * 3) return;

            if (_offset > 0)
            {
                var top = new Rectangle(0, 0, ContentWidth, band);
                using (var brush = new LinearGradientBrush(top, SurfaceColor, Color.FromArgb(0, SurfaceColor), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, top);
                }
            }

            if (_offset + Height < ContentHeight)
            {
                var bottom = new Rectangle(0, Height - band, ContentWidth, band);
                using (var brush = new LinearGradientBrush(bottom, Color.FromArgb(0, SurfaceColor), SurfaceColor, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, bottom);
                }
            }
        }

        private void DrawScrollBar(Graphics g)
        {
            if (!NeedsScrollBar) return;

            Rectangle thumb = ThumbBounds();
            Color color = _dragging || _thumbHot ? Theme.TextFaint : Theme.BorderHot;
            using (GraphicsPath path = Theme.Rounded(thumb, thumb.Width / 2))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        // ------------------------------------------------------------------ interaction

        private Rectangle ThumbBounds()
        {
            int barWidth = ScrollBarWidth;
            int top = Px(2);
            int trackHeight = Height - Px(4);
            int thumbHeight = Math.Max(Px(28), (int)((long)Height * Height / Math.Max(1, ContentHeight)));
            int travel = trackHeight - thumbHeight;
            int thumbY = top + (MaxOffset == 0 ? 0 : (int)((long)travel * _offset / MaxOffset));
            return new Rectangle(Width - barWidth - Px(2), thumbY, barWidth, thumbHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();

            if (NeedsScrollBar && e.X >= Width - ScrollBarWidth - Px(4))
            {
                Rectangle thumb = ThumbBounds();
                if (thumb.Contains(e.Location))
                {
                    _dragging = true;
                    _dragStartY = e.Y;
                    _dragStartOffset = _offset;
                }
                else
                {
                    ScrollBy(e.Y < thumb.Y ? -Height + RowHeight : Height - RowHeight);
                }
                Invalidate();
                return;
            }

            int index = (_offset + e.Y) / RowHeight;
            if (e.Y >= 0 && index >= 0 && index < _items.Count && IsSelectable(_items[index]))
            {
                _pressed = index;
                SelectedIndex = index;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                int trackHeight = Height - Px(4);
                int thumbHeight = Math.Max(Px(28), (int)((long)Height * Height / Math.Max(1, ContentHeight)));
                int travel = trackHeight - thumbHeight;
                if (travel > 0)
                {
                    int moved = e.Y - _dragStartY;
                    _offset = _dragStartOffset + (int)((long)moved * MaxOffset / travel);
                    ClampScroll();
                }
                Invalidate();
                return;
            }

            bool thumbHot = NeedsScrollBar && ThumbBounds().Contains(e.Location);
            int hover = e.X >= Width - ScrollBarWidth - Px(4) ? -1 : (_offset + e.Y) / RowHeight;
            if (hover >= _items.Count || (hover >= 0 && !IsSelectable(_items[hover]))) hover = -1;

            if (hover != _hover || thumbHot != _thumbHot)
            {
                _hover = hover;
                _thumbHot = thumbHot;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Invalidate();
                base.OnMouseUp(e);
                return;
            }

            int index = (_offset + e.Y) / RowHeight;
            if (_pressed >= 0 && index == _pressed && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            _pressed = -1;

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1 || _thumbHot)
            {
                _hover = -1;
                _thumbHot = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta * RowHeight * 3 / 120);
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        private void Step(int direction)
        {
            int index = _selected;
            for (int guard = 0; guard < _items.Count; guard++)
            {
                index += direction;
                if (index < 0 || index >= _items.Count) return;
                if (IsSelectable(_items[index]))
                {
                    SelectedIndex = index;
                    return;
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Step(-1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Step(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _selected >= 0 && ItemActivated != null) { ItemActivated(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
        }

        /// <summary>Rounded selection highlight used by the row painters.</summary>
        protected void DrawHighlight(Graphics g, Rectangle row, bool selected, bool hover)
        {
            if (!selected && !hover) return;
            var body = new Rectangle(row.X + Px(5), row.Y + Px(2), row.Width - Px(10), row.Height - Px(4));
            using (GraphicsPath path = Theme.Rounded(body, Px(7)))
            using (var brush = new SolidBrush(selected ? HighlightColor : Theme.Mix(SurfaceColor, HighlightColor, 0.55f)))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Sends wheel messages to the list under the cursor. Without this the wheel only
        /// reaches the focused control, so hovering a list would not scroll it.
        /// </summary>
        internal sealed class WheelRouter : IMessageFilter
        {
            [DllImport("user32.dll")]
            private static extern IntPtr WindowFromPoint(POINT point);

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT { public int X; public int Y; }

            private const int WM_MOUSEWHEEL = 0x020A;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;

                long packed = m.LParam.ToInt64();
                var point = new POINT { X = unchecked((short)(packed & 0xFFFF)), Y = unchecked((short)((packed >> 16) & 0xFFFF)) };

                IntPtr handle = WindowFromPoint(point);
                if (handle == IntPtr.Zero) return false;

                Control control = Control.FromHandle(handle);
                while (control != null && !(control is DarkList)) control = control.Parent;

                var list = control as DarkList;
                if (list == null) return false;

                int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                list.ScrollBy(-delta * list.RowHeight * 3 / 120);
                return true;
            }
        }
    }

    /// <summary>Provider rows: badge, name, id, hover delete icon and an enable status dot.</summary>
    internal class ProviderList : DarkList
    {
        private sealed class RowIcons
        {
            public Rectangle Delete;
            public ProviderEntry Provider;
        }

        private readonly List<RowIcons> _rowIcons = new List<RowIcons>();

        /// <summary>Raised when a row's trash icon is clicked.</summary>
        public event EventHandler<ProviderEntry> ProviderDelete;

        public ProviderList()
        {
            RowHeight = 48;
            SurfaceColor = Theme.Card;
            HighlightColor = Theme.CardAlt;
            BackColor = Theme.Card;
        }

        public override string EmptyMessage
        {
            get { return "No provider matches."; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _rowIcons.Clear();
            base.OnPaint(e);
        }

        protected override void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index)
        {
            var group = item as ListGroupRow;
            if (group != null)
            {
                using (var brush = new SolidBrush(Theme.TextFaint))
                {
                    g.DrawString(group.Title.ToUpperInvariant(), Theme.Micro, brush,
                        new RectangleF(Px(14), row.Y + row.Height - Px(22), row.Width - Px(28), Px(18)), Theme.Left);
                }
                return;
            }

            var provider = item as ProviderEntry;
            if (provider == null) return;

            DrawHighlight(g, row, selected, hover);

            float badge = Px(28);
            var badgeBox = new RectangleF(Px(14), row.Y + (row.Height - badge) / 2f, badge, badge);
            Icons.DrawProviderBadge(g, badgeBox, provider.DisplayName.Length > 0 ? provider.DisplayName : provider.Id,
                provider.IsDisabled ? Theme.Muted : (provider.IsConfigured ? Theme.TextDim : Theme.Warn));

            bool hot = selected || hover;
            int size = Px(15);
            int margin = Px(10);
            int dotArea = Px(20);
            var deleteRect = new Rectangle(row.Right - margin - dotArea - size,
                row.Y + (row.Height - size) / 2, size, size);

            if (hot)
            {
                Icons.Draw(g, Glyph.Trash, new RectangleF(deleteRect.X, deleteRect.Y, size, size), Theme.Danger);
            }
            _rowIcons.Add(new RowIcons { Delete = deleteRect, Provider = provider });

            float textX = badgeBox.Right + Px(11);
            using (var brush = new SolidBrush(provider.IsDisabled ? Theme.TextFaint : Theme.Text))
            {
                g.DrawString(provider.DisplayName.Length > 0 ? provider.DisplayName : provider.Id, Theme.Body, brush,
                    new RectangleF(textX, row.Y + Px(5), deleteRect.X - textX - Px(8), Px(18)), Theme.Left);
            }

            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(provider.Id, Theme.Small, brush,
                    new RectangleF(textX, row.Y + Px(24), deleteRect.X - textX - Px(8), Px(16)), Theme.Left);
            }

            Color status = provider.IsDisabled ? Theme.Muted : (provider.IsConfigured ? Theme.Ok : Theme.Warn);
            Icons.DrawDot(g, row.Right - margin - Px(4), row.Y + row.Height / 2f, Px(6) / 2f, status);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Hit(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            RowIcons hit = Hit(e.Location);
            if (hit != null && hit.Delete.Contains(e.Location))
            {
                if (ProviderDelete != null) ProviderDelete(this, hit.Provider);
                return; // do not also activate the row
            }
            base.OnMouseUp(e);
        }

        private RowIcons Hit(Point location)
        {
            foreach (RowIcons icons in _rowIcons)
            {
                if (icons.Delete.Contains(location)) return icons;
            }
            return null;
        }
    }

    /// <summary>
    /// Model rows: id on the left, display name on the right, per-row edit (pencil) and
    /// delete (trash) icons. Clicking a row or pressing Enter still opens the editor.
    /// </summary>
    internal class ModelList : DarkList
    {
        private sealed class RowIcons
        {
            public Rectangle Row;
            public Rectangle Edit;
            public Rectangle Delete;
            public ModelEntry Model;
        }

        private readonly List<RowIcons> _rowIcons = new List<RowIcons>();

        /// <summary>Raised when a row's pencil icon is clicked.</summary>
        public event EventHandler<ModelEntry> ModelEdit;

        /// <summary>Raised when a row's trash icon is clicked.</summary>
        public event EventHandler<ModelEntry> ModelDelete;

        public ModelList()
        {
            RowHeight = 34;
            SurfaceColor = Theme.Field;
            HighlightColor = Theme.CardAlt;
            BackColor = Theme.Field;
        }

        public override string EmptyMessage
        {
            get { return ""; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _rowIcons.Clear();
            base.OnPaint(e);
        }

        protected override void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index)
        {
            var model = item as ModelEntry;
            if (model == null) return;

            DrawHighlight(g, row, selected, hover);

            float left = Px(12);
            if (model.SmartIndicator)
            {
                float sparkle = Px(13);
                Icons.Draw(g, Glyph.Sparkle,
                    new RectangleF(left, row.Y + (row.Height - sparkle) / 2f, sparkle, sparkle), Theme.Focus);
                left += sparkle + Px(6);
            }
            Color iconColor = hover || selected ? Theme.TextDim : Theme.TextFaint;

            // Per-row actions: pencil then trash, right-aligned inside the row.
            int size = Px(13);
            int gap = Px(6);
            int margin = Px(10);
            var deleteRect = new Rectangle(row.Right - margin - size, row.Y + (row.Height - size) / 2, size, size);
            var editRect = new Rectangle(deleteRect.X - gap - size, deleteRect.Y, size, size);

            Icons.Draw(g, Glyph.Pencil,
                new RectangleF(editRect.X, editRect.Y, editRect.Width, editRect.Height), iconColor);
            Icons.Draw(g, Glyph.Trash,
                new RectangleF(deleteRect.X, deleteRect.Y, deleteRect.Width, deleteRect.Height),
                hover ? Theme.Danger : Theme.TextFaint);

            _rowIcons.Add(new RowIcons { Row = row, Edit = editRect, Delete = deleteRect, Model = model });

            int nameWidth = Px(200);
            float nameLeft = editRect.X - Px(8) - nameWidth;

            using (var brush = new SolidBrush(Theme.Text))
            {
                g.DrawString(model.Id, Theme.Body, brush,
                    new RectangleF(left, row.Y, nameLeft - left, row.Height), Theme.Left);
            }

            bool hasName = model.DisplayName.Length > 0 && model.DisplayName != model.Id;
            if (hasName)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(model.DisplayName, Theme.Small, brush,
                        new RectangleF(nameLeft, row.Y, nameWidth, row.Height), Theme.Right);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overIcon = IconAt(e.Location) != null;
            Cursor = overIcon ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            RowIcons hit = IconAt(e.Location);
            if (hit != null)
            {
                if (hit.Delete.Contains(e.Location))
                {
                    if (ModelDelete != null) ModelDelete(this, hit.Model);
                }
                else
                {
                    if (ModelEdit != null) ModelEdit(this, hit.Model);
                }
                return; // do not raise ItemActivated for icon clicks
            }
            base.OnMouseUp(e);
        }

        private RowIcons IconAt(Point location)
        {
            foreach (RowIcons icons in _rowIcons)
            {
                if (icons.Row.Contains(location)) return icons;
            }
            return null;
        }
    }

    /// <summary>A model offered by models.dev inside the Smart add dialog.</summary>
    internal class CatalogRow
    {
        public CatalogModel Model;
        public string Id = "";
        public bool Existing;
        public ModelEntry ExistingEntry;
        public bool ManualLimits;
        public bool Checked = true;
    }

    /// <summary>Check-list rows for the Smart add dialog: checkbox, id, name and limits.</summary>
    internal class CatalogList : DarkList
    {
        public CatalogList()
        {
            RowHeight = 46;
            SurfaceColor = Theme.Field;
            HighlightColor = Theme.CardAlt;
            BackColor = Theme.Field;
        }

        public override string EmptyMessage
        {
            get { return "Nothing matches."; }
        }

        protected override void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index)
        {
            var entry = item as CatalogRow;
            if (entry == null) return;

            DrawHighlight(g, row, selected, hover);

            int box = Px(15);
            var check = new Rectangle(Px(12), row.Y + (row.Height - box) / 2, box, box);
            using (GraphicsPath path = Theme.Rounded(check, Px(4)))
            using (var brush = new SolidBrush(entry.Checked ? Theme.Focus : Theme.Field))
            {
                g.FillPath(brush, path);
            }
            using (GraphicsPath path = Theme.Rounded(check, Px(4)))
            using (var pen = new Pen(entry.Checked ? Theme.Focus : Theme.BorderHot))
            {
                g.DrawPath(pen, path);
            }
            if (entry.Checked)
            {
                float tick = box * 0.72f;
                Icons.Draw(g, Glyph.Check,
                    new RectangleF(check.X + (box - tick) / 2f, check.Y + (box - tick) / 2f, tick, tick), Color.White);
            }

            float left = check.Right + Px(10);
            float right = row.Width - Px(14);

            using (var brush = new SolidBrush(entry.Existing ? Theme.TextDim : Theme.Text))
            {
                g.DrawString(entry.Id, Theme.Body, brush,
                    new RectangleF(left, row.Y + Px(4), right - left - Px(90), Px(18)), Theme.Left);
            }

            string name = entry.Model == null ? "" : entry.Model.Name;
            using (var brush = new SolidBrush(Theme.TextDim))
            {
                g.DrawString(name, Theme.Small, brush,
                    new RectangleF(left, row.Y + Px(24), right - left - Px(200), Px(16)), Theme.Left);
            }

            string limits = Limits(entry.Model);
            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(limits, Theme.Small, brush,
                    new RectangleF(right - Px(196), row.Y + Px(24), Px(196), Px(16)), Theme.Right);
            }

            using (var brush = new SolidBrush(entry.Existing ? Theme.TextFaint : Theme.Focus))
            {
                string tag = entry.Existing ? (entry.ManualLimits ? "added \u00b7 manual" : "added") : "new";
                g.DrawString(tag, Theme.Small, brush,
                    new RectangleF(right - Px(140), row.Y + Px(4), Px(140), Px(18)), Theme.Right);
            }
        }

        private static string Limits(CatalogModel model)
        {
            if (model == null) return "";
            string context = ModelCatalog.FormatTokens(model.Context);
            string output = ModelCatalog.FormatTokens(model.Output);
            if (context.Length == 0 && output.Length == 0) return "no limits";
            return (context.Length > 0 ? context + " ctx" : "")
                + (context.Length > 0 && output.Length > 0 ? "  \u00b7  " : "")
                + (output.Length > 0 ? output + " out" : "");
        }
    }

    /// <summary>Config file rows: name, kind, provider count, path, modified.</summary>
    internal class ConfigList : DarkList
    {
        public ConfigList()
        {
            RowHeight = 56;
            SurfaceColor = Theme.Card;
            HighlightColor = Theme.CardAlt;
            BackColor = Theme.Card;
        }

        public override string EmptyMessage
        {
            get { return "No config file found yet."; }
        }

        protected override void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index)
        {
            var info = item as ConfigInfo;
            if (info == null) return;

            DrawHighlight(g, row, selected, hover);

            float left = Px(16);
            float right = row.Width - Px(16);

            using (var brush = new SolidBrush(Theme.Text))
            {
                g.DrawString(info.FileName, Theme.BodyBold, brush,
                    new RectangleF(left, row.Y + Px(8), right - left - Px(110), Px(18)), Theme.Left);
            }

            string count = info.ProviderCount < 0 ? "unreadable"
                : info.ProviderCount + (info.ProviderCount == 1 ? " provider" : " providers");
            using (var brush = new SolidBrush(info.ProviderCount < 0 ? Theme.Warn : Theme.TextDim))
            {
                g.DrawString(count, Theme.Small, brush,
                    new RectangleF(right - Px(110), row.Y + Px(8), Px(110), Px(18)), Theme.Right);
            }

            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(info.Kind + "  \u00b7  " + ShortenPath(info.Path, 44), Theme.Small, brush,
                    new RectangleF(left, row.Y + Px(27), right - left, Px(16)), Theme.Left);
            }

            string modified = info.Modified == default(DateTime) ? "" : info.Modified.ToString("yyyy-MM-dd HH:mm");
            using (var brush = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString(modified, Theme.Small, brush,
                    new RectangleF(left, row.Y + Px(41), right - left, Px(14)), Theme.Left);
            }

            if (info.IsGlobal)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString("global", Theme.Small, brush,
                        new RectangleF(right - Px(110), row.Y + Px(30), Px(110), Px(16)), Theme.Right);
                }
            }
        }

        /// <summary>Shortens a long path from the middle so both ends stay readable.</summary>
        internal static string ShortenPath(string path, int max)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= max) return path;
            string name = System.IO.Path.GetFileName(path);
            string head = path.Substring(0, Math.Max(3, max - name.Length - 4));
            return head + "..." + System.IO.Path.DirectorySeparatorChar + name;
        }
    }

    /// <summary>Plain text rows, used by the dropdown popup.</summary>
    internal class SimpleList : DarkList
    {
        public SimpleList()
        {
            RowHeight = 30;
            SurfaceColor = Theme.Card;
            HighlightColor = Theme.CardAlt;
            BackColor = Theme.Card;
        }

        public override string EmptyMessage { get { return ""; } }

        protected override void DrawRow(Graphics g, Rectangle row, object item, bool selected, bool hover, int index)
        {
            DrawHighlight(g, row, selected, hover);

            using (var brush = new SolidBrush(selected ? Theme.Text : Theme.TextDim))
            {
                g.DrawString(item == null ? "" : item.ToString(), Theme.Body, brush,
                    new RectangleF(Px(14), row.Y, row.Width - Px(24), row.Height), Theme.Left);
            }

            if (selected)
            {
                float size = Px(14);
                Icons.Draw(g, Glyph.Check,
                    new RectangleF(row.Width - Px(30), row.Y + (row.Height - size) / 2f, size, size), Theme.Text);
            }
        }
    }
}
