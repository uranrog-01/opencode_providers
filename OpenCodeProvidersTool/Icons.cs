using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OpenCodeProvidersTool
{
    internal enum Glyph
    {
        Logo, Provider, Models, File, Terminal, Info, Search, Plus, Trash, Refresh,
        Folder, Eye, EyeOff, ChevronDown, ChevronRight, Close, Minimize, Alert,
        Check, Pencil, Save, ArrowLeft, ExternalLink, Server, Chat, Swatch, Help, Sparkle
    }

    /// <summary>
    /// Vector icons drawn with GDI+ on a 16x16 design grid, so they stay crisp at any DPI
    /// and need no image assets shipped alongside the exe.
    /// </summary>
    internal static class Icons
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Window/taskbar icon: the OpenCode terminal mark on a dark rounded square,
        /// with a small blue "patch" badge so this build is recognizable.
        /// </summary>
        public static Icon CreateAppIcon()
        {
            const int s = 64;
            using (var bmp = new Bitmap(s, s))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    // Dark rounded square.
                    using (GraphicsPath path = Theme.Rounded(new Rectangle(1, 1, s - 2, s - 2), 14))
                    using (var fill = new SolidBrush(Color.FromArgb(0x1F, 0x1F, 0x1F)))
                    {
                        g.FillPath(fill, path);
                    }
                    using (GraphicsPath path = Theme.Rounded(new Rectangle(1, 1, s - 2, s - 2), 14))
                    using (var pen = new Pen(Color.FromArgb(0x3A, 0x3A, 0x3A), 2f))
                    {
                        g.DrawPath(pen, path);
                    }

                    // Terminal prompt mark (same language as Glyph.Logo).
                    using (var mark = new Pen(Color.FromArgb(0xE6, 0xE6, 0xE6), 5f))
                    {
                        mark.StartCap = LineCap.Round;
                        mark.EndCap = LineCap.Round;
                        mark.LineJoin = LineJoin.Round;
                        float u = s / 16f;
                        g.DrawLines(mark, new[]
                        {
                            new PointF(4.6f * u, 4.4f * u),
                            new PointF(8.2f * u, 8f * u),
                            new PointF(4.6f * u, 11.6f * u)
                        });
                        g.DrawLine(mark, new PointF(9.4f * u, 11.6f * u), new PointF(12.2f * u, 11.6f * u));
                    }

                    // Blue "patched" badge, bottom-right.
                    float cx = s - 15f, cy = s - 15f, r = 11f;
                    using (var badge = new SolidBrush(Color.FromArgb(0x5B, 0x8D, 0xEF)))
                    {
                        g.FillEllipse(badge, cx - r, cy - r, r * 2, r * 2);
                    }
                    using (var ring = new Pen(Color.FromArgb(0x1F, 0x1F, 0x1F), 3f))
                    {
                        g.DrawEllipse(ring, cx - r, cy - r, r * 2, r * 2);
                    }
                    using (var tick = new Pen(Color.White, 3f))
                    {
                        tick.StartCap = LineCap.Round;
                        tick.EndCap = LineCap.Round;
                        tick.LineJoin = LineJoin.Round;
                        g.DrawLines(tick, new[]
                        {
                            new PointF(cx - 5f, cy),
                            new PointF(cx - 1f, cy + 4f),
                            new PointF(cx + 5.5f, cy - 4.5f)
                        });
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon fromHandle = Icon.FromHandle(hIcon))
                    {
                        return (Icon)fromHandle.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        public static void Draw(Graphics g, Glyph glyph, RectangleF box, Color color)
        {
            float s = box.Width / 16f;
            float stroke = Math.Max(1f, 1.45f * s);

            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                PointF P(float x, float y)
                {
                    return new PointF(box.X + x * s, box.Y + y * s);
                }

                switch (glyph)
                {
                    case Glyph.Logo:
                        using (var brush = new SolidBrush(color))
                        {
                            g.FillPath(brush, Theme.Rounded(
                                new Rectangle((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height),
                                (int)(3.5f * s)));
                        }
                        using (var mark = new Pen(Theme.PageTop, Math.Max(1.4f, 1.8f * s)))
                        {
                            mark.StartCap = LineCap.Round;
                            mark.EndCap = LineCap.Round;
                            mark.LineJoin = LineJoin.Round;
                            g.DrawLines(mark, new[] { P(4.6f, 4.4f), P(8.2f, 8f), P(4.6f, 11.6f) });
                            g.DrawLine(mark, P(9.4f, 11.6f), P(12.2f, 11.6f));
                        }
                        break;

                    case Glyph.Provider:
                        // isometric cube
                        g.DrawLines(pen, new[] { P(8, 1.8f), P(14.2f, 5.4f), P(14.2f, 10.6f), P(8, 14.2f), P(1.8f, 10.6f), P(1.8f, 5.4f), P(8, 1.8f) });
                        g.DrawLines(pen, new[] { P(1.8f, 5.4f), P(8, 9f), P(14.2f, 5.4f) });
                        g.DrawLine(pen, P(8, 9f), P(8, 14.2f));
                        break;

                    case Glyph.Models:
                        g.DrawLines(pen, new[] { P(8, 1.6f), P(14.4f, 5.2f), P(8, 8.8f), P(1.6f, 5.2f), P(8, 1.6f) });
                        g.DrawLines(pen, new[] { P(1.6f, 8.4f), P(8, 12f), P(14.4f, 8.4f) });
                        g.DrawLines(pen, new[] { P(1.6f, 11.2f), P(8, 14.8f), P(14.4f, 11.2f) });
                        break;

                    case Glyph.File:
                        g.DrawLines(pen, new[] { P(3.8f, 1.8f), P(9.6f, 1.8f), P(12.4f, 4.6f), P(12.4f, 14.2f), P(3.8f, 14.2f), P(3.8f, 1.8f) });
                        g.DrawLines(pen, new[] { P(9.4f, 1.9f), P(9.4f, 4.7f), P(12.3f, 4.7f) });
                        break;

                    case Glyph.Terminal:
                        g.DrawLines(pen, new[] { P(2.2f, 3.2f), P(13.8f, 3.2f), P(13.8f, 12.8f), P(2.2f, 12.8f), P(2.2f, 3.2f) });
                        g.DrawLines(pen, new[] { P(5f, 6.4f), P(7.4f, 8.4f), P(5f, 10.4f) });
                        g.DrawLine(pen, P(9f, 10.4f), P(11.4f, 10.4f));
                        break;

                    case Glyph.Info:
                        using (var circle = new Pen(color, stroke))
                        {
                            g.DrawEllipse(circle, box.X + 1.8f * s, box.Y + 1.8f * s, 12.4f * s, 12.4f * s);
                        }
                        g.DrawLine(pen, P(8, 7.2f), P(8, 11.2f));
                        using (var dot = new SolidBrush(color))
                        {
                            float r = 1.05f * s;
                            g.FillEllipse(dot, P(8, 4.9f).X - r, P(8, 4.9f).Y - r, r * 2, r * 2);
                        }
                        break;

                    case Glyph.Search:
                        g.DrawEllipse(pen, box.X + 2.2f * s, box.Y + 2.2f * s, 8.4f * s, 8.4f * s);
                        g.DrawLine(pen, P(10.6f, 10.6f), P(14f, 14f));
                        break;

                    case Glyph.Plus:
                        g.DrawLine(pen, P(8, 3.4f), P(8, 12.6f));
                        g.DrawLine(pen, P(3.4f, 8f), P(12.6f, 8f));
                        break;

                    case Glyph.Trash:
                        g.DrawLine(pen, P(2.6f, 4.4f), P(13.4f, 4.4f));
                        g.DrawLines(pen, new[] { P(6.2f, 4.2f), P(6.2f, 2.4f), P(9.8f, 2.4f), P(9.8f, 4.2f) });
                        g.DrawLines(pen, new[] { P(4.4f, 4.6f), P(5.2f, 13.6f), P(10.8f, 13.6f), P(11.6f, 4.6f) });
                        g.DrawLine(pen, P(6.8f, 7f), P(6.8f, 11.2f));
                        g.DrawLine(pen, P(9.2f, 7f), P(9.2f, 11.2f));
                        break;

                    case Glyph.Refresh:
                        g.DrawArc(pen, box.X + 2f * s, box.Y + 2f * s, 12f * s, 12f * s, 40, 280);
                        g.DrawLines(pen, new[] { P(11.4f, 1.6f), P(12.6f, 4.2f), P(9.9f, 5.2f) });
                        break;

                    case Glyph.Folder:
                        g.DrawLines(pen, new[] { P(1.8f, 4f), P(6.2f, 4f), P(7.8f, 6.2f), P(14.2f, 6.2f), P(14.2f, 12.6f), P(1.8f, 12.6f), P(1.8f, 4f) });
                        break;

                    case Glyph.Eye:
                        g.DrawEllipse(pen, box.X + 1.4f * s, box.Y + 4.4f * s, 13.2f * s, 7.2f * s);
                        using (var pupil = new Pen(color, stroke))
                        {
                            g.DrawEllipse(pupil, box.X + 6.2f * s, box.Y + 6.2f * s, 3.6f * s, 3.6f * s);
                        }
                        break;

                    case Glyph.EyeOff:
                        g.DrawEllipse(pen, box.X + 1.4f * s, box.Y + 4.4f * s, 13.2f * s, 7.2f * s);
                        g.DrawLine(pen, P(3f, 13.4f), P(13f, 2.6f));
                        break;

                    case Glyph.ChevronDown:
                        g.DrawLines(pen, new[] { P(3.8f, 6f), P(8f, 10.2f), P(12.2f, 6f) });
                        break;

                    case Glyph.ChevronRight:
                        g.DrawLines(pen, new[] { P(6f, 3.8f), P(10.2f, 8f), P(6f, 12.2f) });
                        break;

                    case Glyph.ArrowLeft:
                        g.DrawLine(pen, P(13f, 8f), P(3.4f, 8f));
                        g.DrawLines(pen, new[] { P(7.4f, 4f), P(3.4f, 8f), P(7.4f, 12f) });
                        break;

                    case Glyph.Close:
                        g.DrawLine(pen, P(4.2f, 4.2f), P(11.8f, 11.8f));
                        g.DrawLine(pen, P(11.8f, 4.2f), P(4.2f, 11.8f));
                        break;

                    case Glyph.Minimize:
                        g.DrawLine(pen, P(3.6f, 8f), P(12.4f, 8f));
                        break;

                    case Glyph.Alert:
                        g.DrawLines(pen, new[] { P(8, 2.2f), P(14.6f, 13.6f), P(1.4f, 13.6f), P(8, 2.2f) });
                        g.DrawLine(pen, P(8, 6.4f), P(8, 10f));
                        using (var dot = new SolidBrush(color))
                        {
                            float r = 0.95f * s;
                            g.FillEllipse(dot, P(8, 11.8f).X - r, P(8, 11.8f).Y - r, r * 2, r * 2);
                        }
                        break;

                    case Glyph.Check:
                        g.DrawLines(pen, new[] { P(3.2f, 8.4f), P(6.6f, 11.8f), P(12.8f, 4.4f) });
                        break;

                    case Glyph.Pencil:
                        g.DrawLines(pen, new[] { P(2.6f, 13.4f), P(3.2f, 10.4f), P(10.6f, 3f), P(13f, 5.4f), P(5.6f, 12.8f), P(2.6f, 13.4f) });
                        g.DrawLine(pen, P(9.4f, 4.2f), P(11.8f, 6.6f));
                        break;

                    case Glyph.Save:
                        g.DrawLines(pen, new[] { P(2.8f, 2.8f), P(11.2f, 2.8f), P(13.2f, 4.8f), P(13.2f, 13.2f), P(2.8f, 13.2f), P(2.8f, 2.8f) });
                        g.DrawLines(pen, new[] { P(5.6f, 2.9f), P(5.6f, 6.4f), P(10.2f, 6.4f), P(10.2f, 2.9f) });
                        g.DrawLines(pen, new[] { P(5.6f, 13.1f), P(5.6f, 9.4f), P(10.4f, 9.4f), P(10.4f, 13.1f) });
                        break;

                    case Glyph.ExternalLink:
                        g.DrawLines(pen, new[] { P(8.6f, 2.8f), P(13.2f, 2.8f), P(13.2f, 7.4f) });
                        g.DrawLine(pen, P(13.2f, 2.8f), P(7.6f, 8.4f));
                        g.DrawLines(pen, new[] { P(11.4f, 9.4f), P(11.4f, 13.2f), P(2.8f, 13.2f), P(2.8f, 4.6f), P(6.6f, 4.6f) });
                        break;

                    case Glyph.Server:
                        g.DrawLines(pen, new[] { P(3f, 3.4f), P(13f, 3.4f), P(13f, 7f), P(3f, 7f), P(3f, 3.4f) });
                        g.DrawLines(pen, new[] { P(3f, 9f), P(13f, 9f), P(13f, 12.6f), P(3f, 12.6f), P(3f, 9f) });
                        using (var dot = new SolidBrush(color))
                        {
                            float r = 0.9f * s;
                            g.FillEllipse(dot, P(5.2f, 5.2f).X - r, P(5.2f, 5.2f).Y - r, r * 2, r * 2);
                            g.FillEllipse(dot, P(5.2f, 10.8f).X - r, P(5.2f, 10.8f).Y - r, r * 2, r * 2);
                        }
                        break;

                    case Glyph.Chat:
                        g.DrawLines(pen, new[] { P(2.4f, 2.8f), P(13.6f, 2.8f), P(13.6f, 10.4f), P(6.4f, 10.4f), P(4.2f, 13.2f), P(4.2f, 10.4f), P(2.4f, 10.4f), P(2.4f, 2.8f) });
                        g.DrawLine(pen, P(5.4f, 6.6f), P(10.6f, 6.6f));
                        break;

                    case Glyph.Swatch:
                        g.DrawEllipse(pen, box.X + 2f * s, box.Y + 2f * s, 12f * s, 12f * s);
                        using (var fill = new SolidBrush(color))
                        {
                            g.FillEllipse(fill, box.X + 3.6f * s, box.Y + 3.6f * s, 4.6f * s, 4.6f * s);
                        }
                        g.DrawLine(pen, P(11f, 11f), P(13.6f, 13.6f));
                        break;

                    case Glyph.Help:
                        g.DrawEllipse(pen, box.X + 1.8f * s, box.Y + 1.8f * s, 12.4f * s, 12.4f * s);
                        g.DrawArc(pen, box.X + 5.4f * s, box.Y + 4f * s, 5.2f * s, 5.2f * s, 170, 200);
                        g.DrawLine(pen, P(8f, 8.9f), P(8f, 9.7f));
                        using (var dot = new SolidBrush(color))
                        {
                            float r = 0.95f * s;
                            g.FillEllipse(dot, P(8, 11.5f).X - r, P(8, 11.5f).Y - r, r * 2, r * 2);
                        }
                        break;

                    case Glyph.Sparkle:
                        using (var star = new SolidBrush(color))
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(P(8f, 1.4f), P(8.8f, 5.2f), P(10.8f, 7.2f), P(14.6f, 8f));
                            path.AddBezier(P(14.6f, 8f), P(10.8f, 8.8f), P(8.8f, 10.8f), P(8f, 14.6f));
                            path.AddBezier(P(8f, 14.6f), P(7.2f, 10.8f), P(5.2f, 8.8f), P(1.4f, 8f));
                            path.AddBezier(P(1.4f, 8f), P(5.2f, 7.2f), P(7.2f, 5.2f), P(8f, 1.4f));
                            path.CloseFigure();
                            g.FillPath(star, path);
                        }
                        break;
                }
            }

            g.SmoothingMode = previous;
        }

        /// <summary>Rounded badge with a provider's initial, used in the provider list.</summary>
        public static void DrawProviderBadge(Graphics g, RectangleF box, string text, Color tint)
        {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = Theme.Rounded(
                new Rectangle((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height),
                (int)(box.Width * 0.28f)))
            {
                using (var fill = new SolidBrush(Color.FromArgb(38, tint)))
                {
                    g.FillPath(fill, path);
                }
                using (var pen = new Pen(Color.FromArgb(90, tint)))
                {
                    g.DrawPath(pen, path);
                }
            }

            string initial = string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpperInvariant();
            using (var font = new Font("Segoe UI Semibold", box.Height * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(tint))
            {
                g.DrawString(initial, font, brush,
                    new RectangleF(box.X, box.Y + box.Height * 0.06f, box.Width, box.Height), Theme.Center);
            }

            g.SmoothingMode = previous;
        }

        /// <summary>Filled status dot.</summary>
        public static void DrawDot(Graphics g, float cx, float cy, float radius, Color color)
        {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
            }
            g.SmoothingMode = previous;
        }
    }
}
