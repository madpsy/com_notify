using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ComNotify
{
    /// <summary>Tray icon, device-change plumbing and the port menu.</summary>
    internal class TrayContext : ApplicationContext
    {
        private readonly Settings settings;
        private readonly MessageWindow window;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();

        private List<PortInfo> ports = new List<PortInfo>();
        private readonly Dictionary<string, PortInfo> present = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);
        private Icon currentIcon;
        private bool firstScan = true;

        private EventWaitHandle showEvent;
        private Thread showThread;
        private volatile bool stopping;

        private static readonly Color ConnectAccent = Color.FromArgb(0x3F, 0xC1, 0x5A);
        private static readonly Color DisconnectAccent = Color.FromArgb(0xE0, 0x6C, 0x4F);

        public TrayContext()
        {
            settings = Settings.Load();

            menu.Font = new Font("Segoe UI", 9f);
            menu.ShowImageMargin = false;

            tray = new NotifyIcon();
            tray.Text = "ComNotify";
            tray.Visible = true;
            tray.MouseClick += OnTrayClick;
            tray.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenDetails();
            };
            tray.BalloonTipClicked += delegate { OpenDetails(); };
            SetIcon(0);

            window = new MessageWindow();
            window.DeviceChanged += delegate { ScheduleRescan(); };
            StartShowListener();

            debounce.Interval = 600;
            debounce.Tick += delegate
            {
                debounce.Stop();
                Rescan();
            };

            Rescan();  // baseline, no notifications
        }

        /// <summary>
        /// A second launch of ComNotify signals this event instead of starting another tray icon,
        /// which brings the port list up front.
        /// </summary>
        private void StartShowListener()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            }
            catch { return; }

            showThread = new Thread(new ThreadStart(ShowListenerLoop));
            showThread.IsBackground = true;
            showThread.Start();
        }

        private void ShowListenerLoop()
        {
            while (!stopping)
            {
                try
                {
                    showEvent.WaitOne();
                    if (stopping) return;
                    window.BeginInvoke(new MethodInvoker(OpenDetails));
                }
                catch { return; }
            }
        }

        // ---- device change ---------------------------------------------------

        private void ScheduleRescan()
        {
            debounce.Stop();
            debounce.Start();
        }

        private void Rescan()
        {
            List<PortInfo> fresh;
            try
            {
                fresh = PortEnumerator.Enumerate();
            }
            catch (Exception ex)
            {
                tray.Text = Truncate("ComNotify — enumeration failed: " + ex.Message, 63);
                return;
            }

            List<PortInfo> arrived = new List<PortInfo>();
            List<PortInfo> departed = new List<PortInfo>();
            Dictionary<string, PortInfo> nowPresent = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (PortInfo p in fresh)
            {
                if (!p.Present) continue;
                nowPresent[p.Key] = p;
                PortInfo old;
                if (!present.TryGetValue(p.Key, out old))
                    arrived.Add(p);
                else if (!string.Equals(old.PortName, p.PortName, StringComparison.OrdinalIgnoreCase))
                    arrived.Add(p);  // same device, Windows reassigned the COM number
            }
            foreach (KeyValuePair<string, PortInfo> kv in present)
                if (!nowPresent.ContainsKey(kv.Key)) departed.Add(kv.Value);

            ports = fresh;
            present.Clear();
            foreach (KeyValuePair<string, PortInfo> kv in nowPresent) present[kv.Key] = kv.Value;

            SetIcon(nowPresent.Count);
            UpdateTooltip();
            DetailsForm.ReloadIfOpen();

            if (firstScan)
            {
                firstScan = false;
                return;  // don't announce whatever was already plugged in at launch
            }

            if (settings.NotifyOnConnect) Announce(Filter(arrived), true);
            if (settings.NotifyOnDisconnect) Announce(Filter(departed), false);
        }

        private List<PortInfo> Filter(List<PortInfo> input)
        {
            if (settings.ShowNonUsb) return input;
            List<PortInfo> outp = new List<PortInfo>();
            foreach (PortInfo p in input) if (p.IsUsb) outp.Add(p);
            return outp;
        }

        private void Announce(List<PortInfo> changed, bool connected)
        {
            if (changed.Count == 0) return;

            string title;
            if (changed.Count == 1)
                title = changed[0].PortName + (connected ? " connected" : " disconnected");
            else
                title = changed.Count + (connected ? " serial ports connected" : " serial ports disconnected");

            List<string> lines = new List<string>();
            foreach (PortInfo p in changed)
            {
                if (lines.Count == 4) { lines.Add("…and " + (changed.Count - 4) + " more"); break; }
                lines.Add(changed.Count == 1 ? p.MenuText.Replace("   ", " — ") : p.MenuText);
            }

            string copyValue = connected && changed.Count == 1 ? changed[0].PortName : null;

            if (settings.PlaySound)
            {
                try
                {
                    if (connected) SystemSounds.Asterisk.Play();
                    else SystemSounds.Exclamation.Play();
                }
                catch { }
            }

            if (settings.UsePopup)
            {
                ToastForm.Show(title, lines, connected ? ConnectAccent : DisconnectAccent,
                    copyValue, settings.PopupSeconds);
            }
            else
            {
                tray.BalloonTipTitle = title;
                tray.BalloonTipText = string.Join("\r\n", lines.ToArray());
                tray.BalloonTipIcon = ToolTipIcon.Info;
                tray.ShowBalloonTip(settings.PopupSeconds * 1000);
            }
        }

        // ---- tray icon -------------------------------------------------------

        private void SetIcon(int count)
        {
            Icon old = currentIcon;
            currentIcon = IconFactory.Create(Ui.TrayIconSize, count > 0, count);
            tray.Icon = currentIcon;
            if (old != null) old.Dispose();
        }

        private void UpdateTooltip()
        {
            int usb = 0;
            List<string> names = new List<string>();
            foreach (PortInfo p in ports)
            {
                if (!p.Present) continue;
                if (p.IsUsb) usb++;
                if (names.Count < 6) names.Add(p.PortName);
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("ComNotify — ").Append(present.Count).Append(present.Count == 1 ? " port" : " ports");
            if (usb > 0) sb.Append(" (").Append(usb).Append(" USB)");
            if (names.Count > 0) sb.Append(": ").Append(string.Join(", ", names.ToArray()));
            tray.Text = Truncate(sb.ToString(), 63);
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        // ---- menu ------------------------------------------------------------

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            Rescan();
            BuildMenu();
            ShowMenu();
        }

        private void ShowMenu()
        {
            tray.ContextMenuStrip = menu;
            try
            {
                MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi != null) { mi.Invoke(tray, null); return; }
            }
            catch { }
            menu.Show(Control.MousePosition);
        }

        private void BuildMenu()
        {
            menu.Items.Clear();

            List<PortInfo> connected = new List<PortInfo>();
            List<PortInfo> offline = new List<PortInfo>();
            foreach (PortInfo p in ports)
            {
                if (!settings.ShowNonUsb && !p.IsUsb) continue;
                if (p.Present) connected.Add(p); else offline.Add(p);
            }

            AddHeader(connected.Count == 0
                ? "No serial ports connected"
                : "Connected (" + connected.Count + ")");

            foreach (PortInfo p in connected) menu.Items.Add(MakePortItem(p, true));

            if (settings.ShowDisconnected && offline.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                AddHeader("Previously seen (" + offline.Count + ")");
                int shown = 0;
                foreach (PortInfo p in offline)
                {
                    if (shown++ == 15)
                    {
                        AddHeader("…and " + (offline.Count - 15) + " more — see Details");
                        break;
                    }
                    menu.Items.Add(MakePortItem(p, false));
                }
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("&Details…", delegate { OpenDetails(); }));
            menu.Items.Add(Item("&Copy connected port list", delegate { CopyConnected(); }));
            menu.Items.Add(Item("&Refresh", delegate { Rescan(); }));
            menu.Items.Add(BuildOptionsMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("E&xit", delegate { ExitApp(); }));
        }

        private ToolStripMenuItem MakePortItem(PortInfo p, bool online)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(p.MenuText);
            item.ToolTipText = p.ToolTipText;
            if (!online) item.ForeColor = SystemColors.GrayText;
            if (online && p.IsUsb) item.Font = new Font(menu.Font, FontStyle.Bold);
            PortInfo captured = p;
            item.Click += delegate { CopyPort(captured); };
            return item;
        }

        private ToolStripMenuItem BuildOptionsMenu()
        {
            ToolStripMenuItem options = new ToolStripMenuItem("&Options");
            ToolStripDropDownMenu dropDown = options.DropDown as ToolStripDropDownMenu;
            if (dropDown != null) dropDown.ShowImageMargin = false;

            options.DropDownItems.Add(Check("Notify when a port &connects", settings.NotifyOnConnect,
                delegate(bool v) { settings.NotifyOnConnect = v; }));
            options.DropDownItems.Add(Check("Notify when a port &disconnects", settings.NotifyOnDisconnect,
                delegate(bool v) { settings.NotifyOnDisconnect = v; }));
            options.DropDownItems.Add(Check("&Play a sound", settings.PlaySound,
                delegate(bool v) { settings.PlaySound = v; }));
            options.DropDownItems.Add(Check("Use built-in popup (not Windows &balloon)", settings.UsePopup,
                delegate(bool v) { settings.UsePopup = v; }));
            options.DropDownItems.Add(new ToolStripSeparator());
            options.DropDownItems.Add(Check("Show &non-USB ports", settings.ShowNonUsb,
                delegate(bool v) { settings.ShowNonUsb = v; }));
            options.DropDownItems.Add(Check("Show previously &seen ports", settings.ShowDisconnected,
                delegate(bool v) { settings.ShowDisconnected = v; }));
            options.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with &Windows");
            startup.Checked = Settings.RunAtStartup;
            startup.CheckOnClick = true;
            startup.Click += delegate { Settings.SetRunAtStartup(startup.Checked); };
            options.DropDownItems.Add(startup);

            return options;
        }

        private ToolStripMenuItem Check(string text, bool value, Action<bool> setter)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Checked = value;
            item.CheckOnClick = true;
            item.Click += delegate
            {
                setter(item.Checked);
                settings.Save();
            };
            return item;
        }

        private static ToolStripMenuItem Item(string text, Action action)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { action(); };
            return item;
        }

        private void AddHeader(string text)
        {
            ToolStripMenuItem header = new ToolStripMenuItem(text);
            header.Enabled = false;
            header.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(header);
        }

        // ---- actions ---------------------------------------------------------

        private void CopyPort(PortInfo p)
        {
            try { Clipboard.SetText(p.PortName); } catch { return; }
            List<string> lines = new List<string>();
            lines.Add(p.DisplayName);
            ToastForm.Show(p.PortName + " copied to clipboard", lines, ConnectAccent, null, 3);
        }

        private void CopyConnected()
        {
            StringBuilder sb = new StringBuilder();
            foreach (PortInfo p in ports)
                if (p.Present) sb.AppendLine(p.PortName + "\t" + p.DisplayName +
                    (string.IsNullOrEmpty(p.VidPid) ? "" : "\t" + p.VidPid));
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString().TrimEnd()); } catch { }
        }

        private void OpenDetails()
        {
            DetailsForm.ShowSingleton(delegate { return PortEnumerator.Enumerate(); });
        }

        private void ExitApp()
        {
            stopping = true;
            if (showEvent != null)
            {
                try { showEvent.Set(); } catch { }
            }
            ToastForm.CloseAny();
            tray.Visible = false;
            tray.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            window.Cleanup();
            ExitThread();
        }

        /// <summary>
        /// Hidden top-level window: receives WM_DEVICECHANGE for the COM-port interface class
        /// (registered explicitly) plus the broadcast devnode-changed message as a backstop.
        /// </summary>
        private class MessageWindow : Form
        {
            public event EventHandler DeviceChanged;

            private IntPtr notifyHandle = IntPtr.Zero;

            public MessageWindow()
            {
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(1, 1);
                Opacity = 0;
                CreateHandle();  // top-level HWND without ever showing the form
                Register();
            }

            private void Register()
            {
                Native.DEV_BROADCAST_DEVICEINTERFACE filter = new Native.DEV_BROADCAST_DEVICEINTERFACE();
                filter.dbcc_size = Marshal.SizeOf(typeof(Native.DEV_BROADCAST_DEVICEINTERFACE));
                filter.dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE;
                filter.dbcc_reserved = 0;
                filter.dbcc_classguid = Native.GuidDevInterfaceComPort;
                filter.dbcc_name = new byte[255];

                IntPtr buffer = Marshal.AllocHGlobal(filter.dbcc_size);
                try
                {
                    Marshal.StructureToPtr(filter, buffer, false);
                    notifyHandle = Native.RegisterDeviceNotification(Handle, buffer,
                        Native.DEVICE_NOTIFY_WINDOW_HANDLE);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            protected override void SetVisibleCore(bool value)
            {
                base.SetVisibleCore(false);  // never show, ever
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_DEVICECHANGE)
                {
                    int evt = m.WParam.ToInt32();
                    if (evt == Native.DBT_DEVICEARRIVAL || evt == Native.DBT_DEVICEREMOVECOMPLETE ||
                        evt == Native.DBT_DEVNODES_CHANGED)
                    {
                        EventHandler h = DeviceChanged;
                        if (h != null) h(this, EventArgs.Empty);
                    }
                }
                base.WndProc(ref m);
            }

            public void Cleanup()
            {
                if (notifyHandle != IntPtr.Zero)
                {
                    Native.UnregisterDeviceNotification(notifyHandle);
                    notifyHandle = IntPtr.Zero;
                }
                Dispose();
            }
        }
    }
}
