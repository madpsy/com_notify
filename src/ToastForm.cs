using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ComNotify
{
    /// <summary>
    /// Borderless popup near the tray. Used instead of a shell balloon because balloons are
    /// silently swallowed when notifications are muted or Focus Assist is on.
    /// </summary>
    internal class ToastForm : Form
    {
        private static ToastForm current;

        private readonly Timer life = new Timer();
        private readonly Timer fade = new Timer();
        private readonly string title;
        private readonly List<string> lines;
        private readonly Color accent;
        private readonly string copyValue;
        private int fadeDirection = 1;   // 1 in, -1 out
        private bool hovering;

        private static readonly Color Back = Color.FromArgb(0x1E, 0x22, 0x28);
        private static readonly Color Fore = Color.FromArgb(0xF2, 0xF4, 0xF6);
        private static readonly Color Dim = Color.FromArgb(0xA8, 0xB0, 0xBA);

        public static void Show(string title, List<string> lines, Color accent, string copyValue, int seconds)
        {
            if (current != null && !current.IsDisposed)
            {
                current.CloseNow();
            }
            ToastForm t = new ToastForm(title, lines, accent, copyValue, seconds);
            current = t;
            t.Show();
        }

        public static void CloseAny()
        {
            if (current != null && !current.IsDisposed) current.CloseNow();
            current = null;
        }

        private ToastForm(string title, List<string> lines, Color accent, string copyValue, int seconds)
        {
            this.title = title;
            this.lines = lines;
            this.accent = accent;
            this.copyValue = copyValue;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            Opacity = 0;
            DoubleBuffered = true;
            Cursor = string.IsNullOrEmpty(copyValue) ? Cursors.Default : Cursors.Hand;

            Size = Measure();
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Ui.Px(16), wa.Bottom - Height - Ui.Px(16));

            life.Interval = Math.Max(2, seconds) * 1000;
            life.Tick += delegate { if (!hovering) BeginFadeOut(); };
            life.Start();

            fade.Interval = 15;
            fade.Tick += FadeTick;
            fade.Start();
        }

        // Never steal focus from whatever the user is typing into.
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private Size Measure()
        {
            int pad = Ui.Px(76);
            int w = Ui.Px(300);
            using (Graphics g = CreateGraphics())
            using (Font tf = new Font("Segoe UI Semibold", 10.5f))
            using (Font lf = new Font("Segoe UI", 9.75f))
            {
                w = Math.Max(w, (int)g.MeasureString(title, tf).Width + pad);
                foreach (string s in lines)
                    w = Math.Max(w, (int)g.MeasureString(s, lf).Width + pad);
            }
            w = Math.Min(w, Ui.Px(460));
            int h = Ui.Px(46) + lines.Count * Ui.Px(21) + Ui.Px(14);
            if (!string.IsNullOrEmpty(copyValue)) h += Ui.Px(18);
            return new Size(w, h);
        }

        private void FadeTick(object sender, EventArgs e)
        {
            double step = fadeDirection > 0 ? 0.14 : -0.11;
            double next = Opacity + step;
            if (next >= 0.97 && fadeDirection > 0) { Opacity = 0.97; fade.Stop(); return; }
            if (next <= 0 && fadeDirection < 0) { CloseNow(); return; }
            Opacity = Math.Max(0, Math.Min(0.97, next));
        }

        private void BeginFadeOut()
        {
            life.Stop();
            fadeDirection = -1;
            fade.Start();
        }

        private void CloseNow()
        {
            life.Stop();
            fade.Stop();
            if (current == this) current = null;
            if (!IsDisposed) Close();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovering = true;
            if (fadeDirection < 0) { fadeDirection = 1; fade.Start(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovering = false;
            life.Stop();
            life.Interval = 2500;
            life.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && !string.IsNullOrEmpty(copyValue))
            {
                try { Clipboard.SetText(copyValue); } catch { }
            }
            BeginFadeOut();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Ui.Px(10);
            int textX = Ui.Px(50);

            using (GraphicsPath path = RoundedRect(r, radius))
            using (SolidBrush b = new SolidBrush(Back))
            using (Pen p = new Pen(Color.FromArgb(0x3A, 0x41, 0x4A)))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            // accent bar down the left edge
            using (GraphicsPath bar = RoundedRect(new Rectangle(0, 0, Ui.Px(8), Height - 1), radius))
            using (SolidBrush b = new SolidBrush(accent))
            {
                Region clip = g.Clip;
                g.SetClip(new Rectangle(0, 0, Ui.Px(5), Height));
                g.FillPath(b, bar);
                g.Clip = clip;
            }

            int glyph = Ui.Px(24);
            using (Icon ic = IconFactory.Create(glyph, true, 0))
                g.DrawIcon(ic, new Rectangle(Ui.Px(18), Ui.Px(16), glyph, glyph));

            using (Font tf = new Font("Segoe UI Semibold", 10.5f))
            using (SolidBrush b = new SolidBrush(Fore))
                g.DrawString(title, tf, b, textX, Ui.Px(14));

            using (Font lf = new Font("Segoe UI", 9.75f))
            using (SolidBrush b = new SolidBrush(Fore))
            {
                int y = Ui.Px(42);
                foreach (string s in lines)
                {
                    g.DrawString(s, lf, b, textX, y);
                    y += Ui.Px(21);
                }
                if (!string.IsNullOrEmpty(copyValue))
                {
                    using (SolidBrush d = new SolidBrush(Dim))
                    using (Font sf = new Font("Segoe UI", 8.25f))
                        g.DrawString("Click to copy " + copyValue + " to the clipboard", sf, d, textX, y + Ui.Px(2));
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                life.Dispose();
                fade.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
