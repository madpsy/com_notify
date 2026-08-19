using System;
using System.Drawing;

namespace ComNotify
{
    /// <summary>
    /// The app declares system-DPI awareness, so hand-drawn pixel metrics have to be scaled
    /// explicitly (point-sized fonts already scale themselves).
    /// </summary>
    internal static class Ui
    {
        private static float scale = 0f;

        public static float Scale
        {
            get
            {
                if (scale <= 0f)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                            scale = g.DpiX / 96f;
                    }
                    catch { scale = 1f; }
                    if (scale < 0.5f || scale > 8f) scale = 1f;
                }
                return scale;
            }
        }

        public static int Px(double value)
        {
            return (int)Math.Round(value * Scale);
        }

        /// <summary>Pixel size the shell wants for a notification-area icon at the current DPI.</summary>
        public static int TrayIconSize
        {
            get
            {
                int n = Native.GetSystemMetrics(Native.SM_CXSMICON);
                if (n < 16) n = 16;
                if (n > 64) n = 64;
                return n;
            }
        }
    }
}
