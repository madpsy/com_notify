using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ComNotify
{
    /// <summary>
    /// Enumerates COM ports through SetupAPI. Two passes: everything installed (which includes
    /// adapters that are currently unplugged) and then everything present, so each port carries
    /// a Present flag rather than silently vanishing from the list.
    /// </summary>
    internal static class PortEnumerator
    {
        private static readonly Regex VidRx = new Regex(@"VID[_&]([0-9A-Fa-f]{4})", RegexOptions.Compiled);
        private static readonly Regex PidRx = new Regex(@"PID[_&]([0-9A-Fa-f]{4})", RegexOptions.Compiled);
        private static readonly Regex ComRx = new Regex(@"\((COM\d+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<PortInfo> Enumerate()
        {
            Dictionary<string, PortInfo> map = new Dictionary<string, PortInfo>(StringComparer.OrdinalIgnoreCase);

            Guid[] classes = new Guid[] { Native.GuidDevClassPorts, Native.GuidDevClassModem };
            foreach (Guid cls in classes)
            {
                Scan(map, cls, false);  // installed, present or not
                Scan(map, cls, true);   // present right now -> flips Present
            }

            List<PortInfo> list = new List<PortInfo>(map.Values);
            list.Sort(delegate(PortInfo a, PortInfo b)
            {
                if (a.PortNumber != b.PortNumber) return a.PortNumber.CompareTo(b.PortNumber);
                return string.Compare(a.PortName, b.PortName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static void Scan(Dictionary<string, PortInfo> map, Guid classGuid, bool presentOnly)
        {
            uint flags = presentOnly ? Native.DIGCF_PRESENT : 0u;
            IntPtr set = Native.SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, flags);
            if (set == Native.INVALID_HANDLE_VALUE || set == IntPtr.Zero) return;

            try
            {
                Native.SP_DEVINFO_DATA info = new Native.SP_DEVINFO_DATA();
                info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SP_DEVINFO_DATA));

                for (uint i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref info); i++)
                {
                    string instanceId = GetInstanceId(set, ref info);
                    if (string.IsNullOrEmpty(instanceId)) continue;

                    string friendly = GetStringProperty(set, ref info, Native.SPDRP_FRIENDLYNAME);
                    string portName = GetPortName(set, ref info, friendly);
                    if (string.IsNullOrEmpty(portName)) continue;
                    if (!portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) continue;  // skip LPTx

                    PortInfo p;
                    if (map.TryGetValue(instanceId, out p))
                    {
                        if (presentOnly) p.Present = true;
                        continue;
                    }

                    p = new PortInfo();
                    p.InstanceId = instanceId;
                    p.PortName = portName.ToUpperInvariant();
                    p.PortNumber = ParsePortNumber(p.PortName);
                    p.FriendlyName = friendly;
                    p.Description = GetStringProperty(set, ref info, Native.SPDRP_DEVICEDESC);
                    p.Manufacturer = GetStringProperty(set, ref info, Native.SPDRP_MFG);
                    p.LocationInfo = GetStringProperty(set, ref info, Native.SPDRP_LOCATION_INFORMATION);
                    p.HardwareIds = GetMultiStringProperty(set, ref info, Native.SPDRP_HARDWAREID);
                    p.Present = presentOnly;

                    string idSource = instanceId + " " + p.HardwareIds;
                    Match m = VidRx.Match(idSource);
                    if (m.Success) p.Vid = m.Groups[1].Value.ToUpperInvariant();
                    m = PidRx.Match(idSource);
                    if (m.Success) p.Pid = m.Groups[1].Value.ToUpperInvariant();

                    p.SerialNumber = ParseSerial(instanceId);
                    p.Bus = ClassifyBus(instanceId, p.Vid);

                    map[instanceId] = p;
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static BusKind ClassifyBus(string instanceId, string vid)
        {
            string id = instanceId.ToUpperInvariant();
            if (id.StartsWith("USB\\") || id.StartsWith("FTDIBUS\\") || id.StartsWith("USBSER") || id.StartsWith("WINUSB"))
                return BusKind.Usb;
            if (id.StartsWith("BTHENUM\\") || id.StartsWith("BTHLE") || id.StartsWith("BTHMODEM"))
                return BusKind.Bluetooth;
            if (id.StartsWith("PCI\\") || id.StartsWith("ACPI\\") || id.StartsWith("PNP") || id.StartsWith("ISAPNP"))
                return BusKind.Internal;
            if (id.StartsWith("ROOT\\") || id.StartsWith("SWD\\") || id.StartsWith("UMB\\") || id.StartsWith("VIRTUAL"))
                return BusKind.Virtual;
            // Some adapters expose a child devnode on a private bus but still carry a USB VID.
            if (!string.IsNullOrEmpty(vid)) return BusKind.Usb;
            return BusKind.Other;
        }

        private static string ParseSerial(string instanceId)
        {
            // USB\VID_0403&PID_6001\A5XK3RJT   -> A5XK3RJT
            // FTDIBUS\VID_0403+PID_6001+A5XK3RJTA\0000 -> A5XK3RJTA
            string[] parts = instanceId.Split('\\');
            if (parts.Length >= 3)
            {
                string last = parts[parts.Length - 1];
                if (last.IndexOf('&') < 0 && last != "0000" && last.Length > 1) return last;
            }
            if (parts.Length >= 2)
            {
                string[] plus = parts[1].Split('+');
                if (plus.Length >= 3) return plus[2];
            }
            return "";
        }

        private static int ParsePortNumber(string portName)
        {
            int n;
            if (portName.Length > 3 && int.TryParse(portName.Substring(3), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out n))
                return n;
            return int.MaxValue;
        }

        /// <summary>PortName lives in the device's hardware key; fall back to "(COMx)" in the friendly name.</summary>
        private static string GetPortName(IntPtr set, ref Native.SP_DEVINFO_DATA info, string friendly)
        {
            IntPtr hkey = Native.SetupDiOpenDevRegKey(set, ref info, Native.DICS_FLAG_GLOBAL, 0,
                Native.DIREG_DEV, Native.KEY_READ);
            if (hkey != Native.INVALID_HANDLE_VALUE && hkey != IntPtr.Zero)
            {
                try
                {
                    using (SafeRegistryHandle safe = new SafeRegistryHandle(hkey, true))
                    using (RegistryKey key = RegistryKey.FromHandle(safe))
                    {
                        object value = key.GetValue("PortName");
                        if (value != null)
                        {
                            string s = value.ToString().Trim();
                            if (s.Length > 0) return s;
                        }
                    }
                }
                catch { /* key vanished or ACL'd out; fall through to the name parse */ }
            }

            if (!string.IsNullOrEmpty(friendly))
            {
                Match m = ComRx.Match(friendly);
                if (m.Success) return m.Groups[1].Value;
            }
            return "";
        }

        private static string GetInstanceId(IntPtr set, ref Native.SP_DEVINFO_DATA info)
        {
            uint required;
            StringBuilder sb = new StringBuilder(512);
            if (Native.SetupDiGetDeviceInstanceId(set, ref info, sb, (uint)sb.Capacity, out required))
                return sb.ToString();
            if (required > 0 && required < 8192)
            {
                sb = new StringBuilder((int)required + 1);
                if (Native.SetupDiGetDeviceInstanceId(set, ref info, sb, (uint)sb.Capacity, out required))
                    return sb.ToString();
            }
            return "";
        }

        private static byte[] GetProperty(IntPtr set, ref Native.SP_DEVINFO_DATA info, uint prop, out uint regType)
        {
            uint required;
            regType = 0;
            byte[] buffer = new byte[1024];
            if (Native.SetupDiGetDeviceRegistryProperty(set, ref info, prop, out regType, buffer,
                    (uint)buffer.Length, out required))
                return Trim(buffer, required);
            if (required > 0 && required < 65536)
            {
                buffer = new byte[required];
                if (Native.SetupDiGetDeviceRegistryProperty(set, ref info, prop, out regType, buffer,
                        (uint)buffer.Length, out required))
                    return Trim(buffer, required);
            }
            return null;
        }

        private static byte[] Trim(byte[] buffer, uint used)
        {
            if (used == 0 || used > buffer.Length) return buffer;
            byte[] result = new byte[used];
            Array.Copy(buffer, result, (int)used);
            return result;
        }

        private static string GetStringProperty(IntPtr set, ref Native.SP_DEVINFO_DATA info, uint prop)
        {
            uint regType;
            byte[] data = GetProperty(set, ref info, prop, out regType);
            if (data == null) return "";
            string s = Encoding.Unicode.GetString(data);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return s.Trim();
        }

        private static string GetMultiStringProperty(IntPtr set, ref Native.SP_DEVINFO_DATA info, uint prop)
        {
            uint regType;
            byte[] data = GetProperty(set, ref info, prop, out regType);
            if (data == null) return "";
            string raw = Encoding.Unicode.GetString(data);
            string[] parts = raw.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("; ", parts);
        }
    }
}
