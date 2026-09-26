using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>
    /// A settings page: large title, optional description, right-aligned header actions,
    /// a body area below, and a secondary action row along the bottom.
    /// </summary>
    internal class PagePanel : Panel
    {
        private string _title = "";
        private string _description = "";

        public readonly Panel Body;
        public readonly FlowLayoutPanel Actions;
        public readonly FlowLayoutPanel Footer;
        public readonly FlowLayoutPanel FooterRight;

        public PagePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Page;

            Actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Page,
                AutoSize = false
            };

            // Secondary actions live under the body so the header stays reserved for the
            // two things that act on the document itself. Two groups, so About can sit on
            // the far right while the file actions stay on the left.
            Footer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Page,
                AutoSize = false
            };

            FooterRight = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Page,
                AutoSize = false
            };

            Body = new Panel { BackColor = Theme.Page };

            Controls.Add(Body);
            Controls.Add(Footer);
            Controls.Add(FooterRight);
            Controls.Add(Actions);
        }

        public string Title
        {
            get { return _title; }
            set { _title = value ?? ""; Invalidate(); }
        }

        public string Description
        {
            get { return _description; }
            set { _description = value ?? ""; Invalidate(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutPage();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutPage();
        }

        private void LayoutPage()
        {
            float scale = Theme.DpiFactor(this);
            int actionsWidth = Theme.Scaled(600, scale);
            // Tight header: title + action row only — no dead space under the buttons.
            int headerHeight = Theme.Scaled(64, scale);
            int footerHeight = Theme.Scaled(52, scale);

            Actions.Location = new Point(Math.Max(0, Width - actionsWidth - Theme.Scaled(2, scale)), Theme.Scaled(6, scale));
            Actions.Size = new Size(actionsWidth, Theme.Scaled(34, scale));

            int footerTop = Math.Max(0, Height - footerHeight);
            int half = Math.Max(0, Width / 2);
            Footer.Location = new Point(0, footerTop);
            Footer.Size = new Size(half, footerHeight);
            FooterRight.Location = new Point(Width - half, footerTop);
            FooterRight.Size = new Size(half, footerHeight);

            Body.Location = new Point(0, headerHeight);
            Body.Size = new Size(Width, Math.Max(0, footerTop - headerHeight));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);

            using (var brush = new SolidBrush(Theme.Text))
            {
                g.DrawString(_title, Theme.PageTitle, brush,
                    new RectangleF(0, Theme.S(2, g), Width - Theme.S(480, g), Theme.S(34, g)), Theme.Left);
            }

            if (_description.Length > 0)
            {
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(_description, Theme.Body, brush,
                        new RectangleF(0, Theme.S(44, g), Width - Theme.S(470, g), Theme.S(64, g)), Theme.Wrap);
                }
            }
        }
    }
}
