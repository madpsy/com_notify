using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ComNotify Setup")]
[assembly: AssemblyDescription("Installer for ComNotify")]
[assembly: AssemblyProduct("ComNotify")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ComNotifySetup
{
    /// <summary>
    /// Per-user installer. Everything it writes lives under %LOCALAPPDATA% and HKCU, so it never
    /// needs elevation. The compiled ComNotify.exe travels inside this exe as a resource.
    /// </summary>
    internal static class Program
    {
        public const string AppName = "ComNotify";
        public const string Version = "1.0.0";
        public const string Publisher = "ComNotify";
        public const string ExeName = "ComNotify.exe";
        public const string IconName = "ComNotify.ico";
        public const string UninstallerName = "Uninstall ComNotify.exe";

        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ComNotify";
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "ComNotify";
        public const string SettingsKeyPath = @"Software\ComNotify";

        public const string ResApp = "ComNotify.Payload.App";
        public const string ResIcon = "ComNotify.Payload.Icon";

        [STAThread]
        private static void Main(string[] rawArgs)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Args args = new Args(rawArgs);

            try
            {
                if (args.Has("uninstall") || args.Has("u"))
                {
                    RunUninstall(args);
                    return;
                }

                if (args.Silent)
                {
                    string dir = args.Value("dir", DefaultInstallDir);
                    Installer.Install(dir, !args.Has("noshortcut"), args.Has("desktop"),
                        args.Has("autostart"), delegate(string s) { });
                    if (args.Has("launch")) Installer.LaunchApp(dir);
                    return;
                }

                Application.Run(new SetupForm());
            }
            catch (Exception ex)
            {
                if (args.Silent)
                    Environment.ExitCode = 1;
                else
                    MessageBox.Show("Setup failed:\r\n\r\n" + ex.Message, AppName + " Setup",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RunUninstall(Args args)
        {
            string cleanupDir = args.Value("cleanup", null);

            if (cleanupDir == null)
            {
                // We are the copy sitting inside the install folder and cannot delete ourselves.
                // Re-run from %TEMP% and let that copy do the work.
                string here = Path.GetDirectoryName(Application.ExecutablePath);
                if (!args.Silent)
                {
                    DialogResult r = MessageBox.Show(
                        "Remove " + AppName + " from this computer?", AppName + " Uninstall",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                }

                string temp = Path.Combine(Path.GetTempPath(),
                    "ComNotify-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(Application.ExecutablePath, temp, true);

                ProcessStartInfo psi = new ProcessStartInfo(temp,
                    "/uninstall /silentprompt /cleanup:\"" + here + "\"" + (args.Silent ? " /silent" : ""));
                psi.UseShellExecute = false;
                Process.Start(psi);
                return;
            }

            Installer.Uninstall(cleanupDir);
            if (!args.Silent && !args.Has("silentprompt"))
                MessageBox.Show(AppName + " has been removed.", AppName + " Uninstall",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            Installer.SelfDeleteLater(Application.ExecutablePath);
        }

        public static string DefaultInstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", AppName));
            }
        }

        /// <summary>Install folder recorded by a previous run, if any.</summary>
        public static string ExistingInstallDir
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath))
                    {
                        if (key == null) return null;
                        object v = key.GetValue("InstallLocation");
                        if (v == null) return null;
                        string s = v.ToString();
                        return string.IsNullOrEmpty(s) ? null : s;
                    }
                }
                catch { return null; }
            }
        }
    }

    /// <summary>Tiny command-line parser: /flag, /key=value, -flag and --flag all work.</summary>
    internal class Args
    {
        private readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Args(string[] argv)
        {
            foreach (string raw in argv)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                string a = raw.TrimStart('/', '-');
                if (a.Length == 0) continue;
                int eq = a.IndexOfAny(new char[] { '=', ':' });
                if (eq > 0) map[a.Substring(0, eq)] = a.Substring(eq + 1).Trim('"');
                else map[a] = "";
            }
        }

        public bool Has(string name) { return map.ContainsKey(name); }

        public string Value(string name, string fallback)
        {
            string v;
            if (map.TryGetValue(name, out v) && v.Length > 0) return v;
            return fallback;
        }

        public bool Silent
        {
            get { return Has("s") || Has("silent") || Has("quiet") || Has("verysilent"); }
        }
    }

    internal static class Installer
    {
        public static void Install(string dir, bool startMenuShortcut, bool desktopShortcut,
            bool autoStart, Action<string> log)
        {
            if (string.IsNullOrEmpty(dir)) throw new ArgumentException("No install folder given.");
            dir = Path.GetFullPath(dir);

            log("Closing any running instance...");
            StopRunningApp();

            log("Creating " + dir);
            Directory.CreateDirectory(dir);

            string exePath = Path.Combine(dir, Program.ExeName);
            string icoPath = Path.Combine(dir, Program.IconName);
            string uninstPath = Path.Combine(dir, Program.UninstallerName);

            log("Extracting " + Program.ExeName);
            WriteResource(Program.ResApp, exePath);
            WriteResource(Program.ResIcon, icoPath);

            log("Writing uninstaller");
            File.Copy(Application.ExecutablePath, uninstPath, true);

            if (startMenuShortcut)
            {
                log("Creating Start menu shortcut");
                CreateShortcut(StartMenuShortcutPath, exePath, dir, icoPath,
                    "Tray notifier for USB serial adapters");
            }
            else
            {
                DeleteQuietly(StartMenuShortcutPath);
            }

            if (desktopShortcut)
            {
                log("Creating desktop shortcut");
                CreateShortcut(DesktopShortcutPath, exePath, dir, icoPath,
                    "Tray notifier for USB serial adapters");
            }
            else
            {
                DeleteQuietly(DesktopShortcutPath);
            }

            log("Registering with Apps & features");
            long size = 0;
            try { size = new FileInfo(exePath).Length + new FileInfo(uninstPath).Length; }
            catch { }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.UninstallKeyPath))
            {
                key.SetValue("DisplayName", Program.AppName);
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", Program.Publisher);
                key.SetValue("InstallLocation", dir);
                key.SetValue("DisplayIcon", icoPath);
                key.SetValue("UninstallString", "\"" + uninstPath + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstPath + "\" /uninstall /silent");
                key.SetValue("EstimatedSize", (int)Math.Max(1, size / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            SetAutoStart(autoStart, exePath);
            log("Done.");
        }

        public static void Uninstall(string dir)
        {
            StopRunningApp();
            SetAutoStart(false, null);

            DeleteQuietly(StartMenuShortcutPath);
            DeleteQuietly(DesktopShortcutPath);

            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKeyPath, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.SettingsKeyPath, false); } catch { }

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (string name in new string[] { Program.ExeName, Program.IconName, Program.UninstallerName })
                    DeleteQuietly(Path.Combine(dir, name));
                try
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
                catch { }
            }
        }

        public static void LaunchApp(string dir)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(dir, Program.ExeName));
                psi.WorkingDirectory = dir;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        public static void SetAutoStart(bool enable, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.RunKeyPath))
                {
                    if (key == null) return;
                    if (enable && !string.IsNullOrEmpty(exePath))
                        key.SetValue(Program.RunValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    else if (key.GetValue(Program.RunValueName) != null)
                        key.DeleteValue(Program.RunValueName, false);
                }
            }
            catch { }
        }

        public static string StartMenuShortcutPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Program.AppName + ".lnk");
            }
        }

        public static string DesktopShortcutPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Program.AppName + ".lnk");
            }
        }

        private static void WriteResource(string resourceName, string targetPath)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new InvalidOperationException("Embedded payload '" + resourceName +
                        "' is missing — rebuild the installer with build-installer.ps1.");
                using (FileStream f = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                    s.CopyTo(f);
            }
        }

        /// <summary>A running ComNotify.exe holds a lock on itself; close it before overwriting.</summary>
        public static void StopRunningApp()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("ComNotify");
                foreach (Process p in procs)
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        p.CloseMainWindow();
                        if (!p.WaitForExit(2000)) p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }
                if (procs.Length > 0) Thread.Sleep(400);
            }
            catch { }
        }

        private static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        /// <summary>Creates a .lnk through WScript.Shell (late-bound, so no COM reference needed).</summary>
        private static void CreateShortcut(string linkPath, string target, string workingDir,
            string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { linkPath });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingDir });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { iconPath + ",0" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); } catch { }
            }
        }

        /// <summary>Schedules deletion of the temp uninstaller copy once this process exits.</summary>
        public static void SelfDeleteLater(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + path + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }
    }

    internal class SetupForm : Form
    {
        private readonly TextBox pathBox = new TextBox();
        private readonly CheckBox startMenu = new CheckBox();
        private readonly CheckBox desktop = new CheckBox();
        private readonly CheckBox autoStart = new CheckBox();
        private readonly CheckBox launch = new CheckBox();
        private readonly Button install = new Button();
        private readonly Button cancel = new Button();
        private readonly Label statusLabel = new Label();
        private readonly bool upgrade;

        private static float scale = 0f;
        private static int Px(double v)
        {
            if (scale <= 0f)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                }
                catch { scale = 1f; }
                if (scale < 0.5f || scale > 8f) scale = 1f;
            }
            return (int)Math.Round(v * scale);
        }

        public SetupForm()
        {
            string existing = Program.ExistingInstallDir;
            upgrade = !string.IsNullOrEmpty(existing);

            Text = Program.AppName + " " + Program.Version + " Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(480), Px(300));
            Font = new Font("Segoe UI", 9f);
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Program.ResIcon))
                    if (s != null) Icon = new Icon(s);
            }
            catch { }

            Label heading = new Label();
            heading.Text = upgrade ? "Update ComNotify" : "Install ComNotify";
            heading.Font = new Font("Segoe UI Semibold", 12f);
            heading.Location = new Point(Px(16), Px(14));
            heading.Size = new Size(Px(440), Px(28));

            Label blurb = new Label();
            blurb.Text = "Shows a popup with the COM port whenever a USB serial adapter is plugged in " +
                         "or unplugged, and lists every known serial port from the tray.";
            blurb.Location = new Point(Px(16), Px(44));
            blurb.Size = new Size(Px(448), Px(38));
            blurb.ForeColor = SystemColors.GrayText;

            Label pathLabel = new Label();
            pathLabel.Text = "&Install to:";
            pathLabel.Location = new Point(Px(16), Px(92));
            pathLabel.Size = new Size(Px(70), Px(20));

            pathBox.Text = upgrade ? existing : Program.DefaultInstallDir;
            pathBox.Location = new Point(Px(16), Px(112));
            pathBox.Size = new Size(Px(360), Px(24));

            Button browse = new Button();
            browse.Text = "&Browse…";
            browse.Location = new Point(Px(384), Px(111));
            browse.Size = new Size(Px(80), Px(26));
            browse.Click += delegate { Browse(); };

            startMenu.Text = "Create a &Start menu shortcut";
            startMenu.Checked = true;
            startMenu.Location = new Point(Px(16), Px(150));
            startMenu.Size = new Size(Px(440), Px(22));

            desktop.Text = "Create a &desktop shortcut";
            desktop.Checked = false;
            desktop.Location = new Point(Px(16), Px(172));
            desktop.Size = new Size(Px(440), Px(22));

            autoStart.Text = "Start &automatically when I sign in";
            autoStart.Checked = true;
            autoStart.Location = new Point(Px(16), Px(194));
            autoStart.Size = new Size(Px(440), Px(22));

            launch.Text = "&Run ComNotify when setup finishes";
            launch.Checked = true;
            launch.Location = new Point(Px(16), Px(216));
            launch.Size = new Size(Px(440), Px(22));

            statusLabel.Location = new Point(Px(16), Px(248));
            statusLabel.Size = new Size(Px(280), Px(40));
            statusLabel.ForeColor = SystemColors.GrayText;

            install.Text = upgrade ? "&Update" : "&Install";
            install.Location = new Point(Px(288), Px(255));
            install.Size = new Size(Px(84), Px(28));
            install.Click += delegate { DoInstall(); };

            cancel.Text = "Cancel";
            cancel.Location = new Point(Px(380), Px(255));
            cancel.Size = new Size(Px(84), Px(28));
            cancel.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                heading, blurb, pathLabel, pathBox, browse,
                startMenu, desktop, autoStart, launch, statusLabel, install, cancel
            });
            AcceptButton = install;
            CancelButton = cancel;

            if (upgrade) statusLabel.Text = "Existing installation found — it will be replaced.";
        }

        private void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose where to install " + Program.AppName;
                dlg.SelectedPath = pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = Path.Combine(dlg.SelectedPath,
                        dlg.SelectedPath.EndsWith(Program.AppName, StringComparison.OrdinalIgnoreCase)
                            ? "" : Program.AppName);
            }
        }

        private void DoInstall()
        {
            install.Enabled = false;
            cancel.Enabled = false;
            try
            {
                Installer.Install(pathBox.Text.Trim(), startMenu.Checked, desktop.Checked,
                    autoStart.Checked, delegate(string s)
                    {
                        statusLabel.Text = s;
                        statusLabel.Refresh();
                    });

                if (launch.Checked) Installer.LaunchApp(pathBox.Text.Trim());

                MessageBox.Show(this,
                    Program.AppName + " " + Program.Version + " is installed.\r\n\r\n" +
                    "It lives in the notification area. On Windows 11 new tray icons start hidden — " +
                    "click the ^ arrow next to the clock and drag ComNotify onto the taskbar to keep it visible.",
                    "Setup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Install failed:\r\n\r\n" + ex.Message, "Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Failed.";
            }
            finally
            {
                install.Enabled = true;
                cancel.Enabled = true;
            }
        }
    }
}
