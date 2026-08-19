using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ComNotify
{
    /// <summary>User options, stored under HKCU\Software\ComNotify.</summary>
    internal class Settings
    {
        private const string KeyPath = @"Software\ComNotify";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ComNotify";

        public bool NotifyOnConnect = true;
        public bool NotifyOnDisconnect = true;
        public bool UsePopup = true;      // custom toast instead of a shell balloon
        public bool PlaySound = true;
        public bool ShowNonUsb = true;    // list Bluetooth / on-board / virtual ports too
        public bool ShowDisconnected = true;
        public int PopupSeconds = 7;

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null) return s;
                    s.NotifyOnConnect = ReadBool(key, "NotifyOnConnect", s.NotifyOnConnect);
                    s.NotifyOnDisconnect = ReadBool(key, "NotifyOnDisconnect", s.NotifyOnDisconnect);
                    s.UsePopup = ReadBool(key, "UsePopup", s.UsePopup);
                    s.PlaySound = ReadBool(key, "PlaySound", s.PlaySound);
                    s.ShowNonUsb = ReadBool(key, "ShowNonUsb", s.ShowNonUsb);
                    s.ShowDisconnected = ReadBool(key, "ShowDisconnected", s.ShowDisconnected);
                    object secs = key.GetValue("PopupSeconds");
                    if (secs != null)
                    {
                        int v;
                        if (int.TryParse(secs.ToString(), out v) && v >= 2 && v <= 60) s.PopupSeconds = v;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key == null) return;
                    key.SetValue("NotifyOnConnect", NotifyOnConnect ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("NotifyOnDisconnect", NotifyOnDisconnect ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("UsePopup", UsePopup ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PlaySound", PlaySound ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ShowNonUsb", ShowNonUsb ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("ShowDisconnected", ShowDisconnected ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PopupSeconds", PopupSeconds, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static bool ReadBool(RegistryKey key, string name, bool fallback)
        {
            object v = key.GetValue(name);
            if (v == null) return fallback;
            int i;
            if (int.TryParse(v.ToString(), out i)) return i != 0;
            return fallback;
        }

        // ---- run at logon ----------------------------------------------------

        public static bool RunAtStartup
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    {
                        if (key == null) return false;
                        object v = key.GetValue(RunValueName);
                        return v != null && v.ToString().Length > 0;
                    }
                }
                catch { return false; }
            }
        }

        public static void SetRunAtStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    else if (key.GetValue(RunValueName) != null)
                        key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not update the startup entry:\r\n" + ex.Message,
                    "ComNotify", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
