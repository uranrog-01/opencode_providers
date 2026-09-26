using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    /// <summary>Palette, fonts and GDI+ helpers shared by every custom control.</summary>
    internal static class Theme
    {
        // Modern dark: slightly lifted surfaces, cooler neutrals, one soft-blue accent
        // for focus only — chrome stays monochrome so status colour always means data.
        public static readonly Color Page = Color.FromArgb(0x16, 0x16, 0x17);
        public static readonly Color PageTop = Color.FromArgb(0x10, 0x10, 0x11);
        public static readonly Color Sidebar = Color.FromArgb(0x10, 0x10, 0x11);
        public static readonly Color Card = Color.FromArgb(0x1D, 0x1D, 0x1F);
        public static readonly Color CardAlt = Color.FromArgb(0x26, 0x26, 0x2A);
        public static readonly Color CardHot = Color.FromArgb(0x2C, 0x2C, 0x31);
        public static readonly Color Field = Color.FromArgb(0x14, 0x14, 0x16);
        public static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2E);
        public static readonly Color BorderHot = Color.FromArgb(0x3D, 0x3D, 0x44);

        /// <summary>Primary action fill: a light neutral — the default style, not LH.</summary>
        public static readonly Color Accent = Color.FromArgb(0xE8, 0xE8, 0xEA);
        public static readonly Color AccentText = Color.FromArgb(0x15, 0x15, 0x17);

        public static readonly Color Text = Color.FromArgb(0xEC, 0xEC, 0xEE);
        public static readonly Color TextDim = Color.FromArgb(0x9A, 0x9A, 0xA2);
        public static readonly Color TextFaint = Color.FromArgb(0x6E, 0x6E, 0x76);

        /// <summary>Focus ring; the only chromatic colour in the chrome.</summary>
        public static readonly Color Focus = Color.FromArgb(0x4C, 0x8D, 0xFF);

        public static readonly Color Ok = Color.FromArgb(0x34, 0xC7, 0x59);
        public static readonly Color Warn = Color.FromArgb(0xFF, 0xB0, 0x20);
        public static readonly Color Muted = Color.FromArgb(0x5A, 0x5A, 0x62);
        public static readonly Color Danger = Color.FromArgb(0xFF, 0x5D, 0x55);

        public static readonly Font PageTitle = new Font("Segoe UI Semibold", 20f);
        public static readonly Font SectionTitle = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Micro = new Font("Segoe UI Semibold", 7.5f);
        public static readonly Font Stat = new Font("Segoe UI Semibold", 20f);
        public static readonly Font StatSm = new Font("Segoe UI Semibold", 13f);
        public static readonly Font Mono = new Font("Consolas", 8.75f);

        public static readonly StringFormat Left = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public static readonly StringFormat Center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public static readonly StringFormat Right = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public static readonly StringFormat Wrap = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord
        };

        /// <summary>Scales a design pixel value to the current monitor DPI.</summary>
        public static int S(int px, Graphics g)
        {
            return (int)Math.Round(px * (g.DpiX / 96f));
        }

        public static float DpiFactor(Control control)
        {
            try { return control.DeviceDpi / 96f; }
            catch { return 1f; }
        }

        public static int Scaled(int value, float factor)
        {
            return (int)Math.Round(value * factor);
        }

        /// <summary>
        /// The colour a child should paint behind its own rounded shape: the fill of the
        /// card it sits on, so the corners blend instead of showing a darker rectangle.
        /// </summary>
        public static Color SurfaceOf(Control control)
        {
            Control parent = control == null ? null : control.Parent;
            while (parent != null)
            {
                var card = parent as CardPanel;
                if (card != null) return card.Fill;
                if (parent.BackColor != Color.Transparent) return parent.BackColor;
                parent = parent.Parent;
            }
            return Theme.Card;
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d >= bounds.Width || d >= bounds.Height)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Draws small uppercase text with extra letter spacing, used for nav groups.</summary>
        public static void DrawTracked(Graphics g, string text, Font font, Brush brush, float x, float y, float tracking)
        {
            StringFormat format = StringFormat.GenericTypographic;
            float cx = x;
            foreach (char c in text)
            {
                string glyph = c.ToString();
                g.DrawString(glyph, font, brush, cx, y, format);
                cx += g.MeasureString(glyph, font, PointF.Empty, format).Width + tracking;
            }
        }

        public static float MeasureTracked(Graphics g, string text, Font font, float tracking)
        {
            StringFormat format = StringFormat.GenericTypographic;
            float width = 0;
            foreach (char c in text)
            {
                width += g.MeasureString(c.ToString(), font, PointF.Empty, format).Width + tracking;
            }
            return width;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        /// <summary>Soft outer glow, used for the focus ring.</summary>
        public static void Glow(Graphics g, Rectangle bounds, Color color, int radius, int steps)
        {
            for (int i = steps; i >= 1; i--)
            {
                int spread = i * 2;
                var r = Rectangle.Inflate(bounds, spread, spread);
                int alpha = Math.Max(2, 12 - i * 3);
                using (var path = Rounded(r, radius + spread))
                using (var pen = new Pen(Color.FromArgb(alpha, color), 2f))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Scales a hand-coded layout to the monitor DPI. WinForms' own AutoScaleMode.Dpi
        /// resizes the form without scaling children for layouts built in code, so the
        /// bounds are multiplied here. Anchors are detached first, otherwise the form
        /// resize would move anchored children a second time.
        /// </summary>
        public static void ApplyDpiLayout(Form form, Size designClient, Size designMinimum, float factor)
        {
            if (factor <= 1.001f) return;

            var anchors = new List<KeyValuePair<Control, AnchorStyles>>();
            CollectAnchors(form, anchors);
            foreach (KeyValuePair<Control, AnchorStyles> entry in anchors) entry.Key.Anchor = AnchorStyles.None;

            form.ClientSize = new Size(Scaled(designClient.Width, factor), Scaled(designClient.Height, factor));
            if (!designMinimum.IsEmpty)
            {
                form.MinimumSize = new Size(Scaled(designMinimum.Width, factor), Scaled(designMinimum.Height, factor));
            }

            foreach (KeyValuePair<Control, AnchorStyles> entry in anchors)
            {
                Control control = entry.Key;
                control.Location = new Point(Scaled(control.Left, factor), Scaled(control.Top, factor));
                control.Size = new Size(Scaled(control.Width, factor), Scaled(control.Height, factor));
                if (!control.MinimumSize.IsEmpty)
                {
                    control.MinimumSize = new Size(
                        Scaled(control.MinimumSize.Width, factor),
                        Scaled(control.MinimumSize.Height, factor));
                }
            }

            foreach (KeyValuePair<Control, AnchorStyles> entry in anchors) entry.Key.Anchor = entry.Value;
        }

        private static void CollectAnchors(Control parent, List<KeyValuePair<Control, AnchorStyles>> found)
        {
            foreach (Control child in parent.Controls)
            {
                found.Add(new KeyValuePair<Control, AnchorStyles>(child, child.Anchor));
                CollectAnchors(child, found);
            }
        }
    }
}
