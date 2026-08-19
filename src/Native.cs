using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ComNotify
{
    /// <summary>P/Invoke surface: SetupAPI device enumeration + device-change notifications.</summary>
    internal static class Native
    {
        // ---- window messages -------------------------------------------------
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        public const int DBT_DEVNODES_CHANGED = 0x0007;
        public const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
        public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        public const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004;

        // ---- device class / interface guids ----------------------------------
        // Ports (COM & LPT)
        public static readonly Guid GuidDevClassPorts = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");
        // Modems (some USB adapters land here)
        public static readonly Guid GuidDevClassModem = new Guid("4D36E96D-E325-11CE-BFC1-08002BE10318");
        // COM port device interface
        public static readonly Guid GuidDevInterfaceComPort = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

        // ---- SetupAPI --------------------------------------------------------
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_HARDWAREID = 0x00000001;
        public const uint SPDRP_SERVICE = 0x00000004;
        public const uint SPDRP_CLASS = 0x00000007;
        public const uint SPDRP_MFG = 0x0000000B;
        public const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        public const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;

        public const uint DICS_FLAG_GLOBAL = 0x00000001;
        public const uint DIREG_DEV = 0x00000001;
        public const uint KEY_READ = 0x00020019;

        public const int REG_SZ = 1;
        public const int REG_MULTI_SZ = 7;

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
            public byte[] dbcc_name;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_HDR
        {
            public int dbch_size;
            public int dbch_devicetype;
            public int dbch_reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            uint scope, uint hwProfile, uint keyType, uint samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ---- device notification registration --------------------------------
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const int HWND_BROADCAST = 0xffff;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const int SM_CXSMICON = 49;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}
