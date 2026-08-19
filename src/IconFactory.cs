using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ComNotify
{
    /// <summary>Draws the tray icon at runtime so the build needs no binary assets.</summary>
    internal static class IconFactory
    {
        public static readonly Color Active = Color.FromArgb(0x3F, 0xC1, 0x5A);
        public static readonly Color Idle = Color.FromArgb(0x9A, 0xA0, 0xA6);

        /// <summary>Serial-plug glyph: cable on the left, connector body on the right, plus an
        /// optional port-count badge. Caller owns the Icon and must dispose it.</summary>
        public static Icon Create(int size, bool active, int count)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                float s = size / 32f;
                Color body = active ? Active : Idle;

                // cable
                using (Pen cable = new Pen(Color.FromArgb(active ? 235 : 200, body), 3.2f * s))
                {
                    cable.StartCap = LineCap.Round;
                    cable.EndCap = LineCap.Round;
                    g.DrawLine(cable, 2.5f * s, 16 * s, 11 * s, 16 * s);
                }

                // connector shell (a DB-style trapezoid)
                using (GraphicsPath shell = new GraphicsPath())
                {
                    shell.AddPolygon(new PointF[]
                    {
                        new PointF(10.5f * s,  7.5f * s),
                        new PointF(21.5f * s,  4.5f * s),
                        new PointF(29.5f * s, 10.0f * s),
                        new PointF(29.5f * s, 22.0f * s),
                        new PointF(21.5f * s, 27.5f * s),
                        new PointF(10.5f * s, 24.5f * s),
                    });
                    using (SolidBrush fill = new SolidBrush(body))
                        g.FillPath(fill, shell);
                    using (Pen edge = new Pen(Color.FromArgb(70, 0, 0, 0), 1.1f * s))
                        g.DrawPath(edge, shell);
                }

                // Pins turn to mush below ~20px, so small sizes get a single slot instead.
                using (SolidBrush pin = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                {
                    if (size >= 20)
                    {
                        float d = 3.6f * s;
                        g.FillEllipse(pin, 16.4f * s, 10.6f * s, d, d);
                        g.FillEllipse(pin, 22.6f * s, 12.0f * s, d, d);
                        g.FillEllipse(pin, 16.4f * s, 17.6f * s, d, d);
                        g.FillEllipse(pin, 22.6f * s, 18.4f * s, d, d);
                    }
                    else
                    {
                        g.FillRectangle(pin, 16.0f * s, 13.5f * s, 10.5f * s, 5.0f * s);
                    }
                }

                // count badge, only where it stays legible
                if (count > 0 && size >= 28)
                {
                    string text = count > 9 ? "9+" : count.ToString();
                    float badge = 15f * s;
                    RectangleF r = new RectangleF(size - badge - 0.5f * s, size - badge - 0.5f * s, badge, badge);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(0x18, 0x1C, 0x22)))
                        g.FillEllipse(b, r);
                    using (Pen p = new Pen(body, 1.4f * s))
                        g.DrawEllipse(p, r);
                    using (Font f = new Font("Segoe UI", badge * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(Color.White))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(text, f, b, r, sf);
                    }
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(hIcon))
                    return (Icon)tmp.Clone();
            }
            finally
            {
                Native.DestroyIcon(hIcon);
                bmp.Dispose();
            }
        }
    }
}
