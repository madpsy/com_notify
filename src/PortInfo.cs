using System;
using System.Text;

namespace ComNotify
{
    internal enum BusKind
    {
        Usb,
        Bluetooth,
        Internal,   // PCI / ACPI / on-board
        Virtual,    // com0com, software loopbacks, root-enumerated
        Other
    }

    /// <summary>One COM port as reported by SetupAPI, present or previously installed.</summary>
    internal class PortInfo
    {
        public string PortName = "";        // "COM7"
        public int PortNumber = int.MaxValue;
        public string FriendlyName = "";    // "USB Serial Port (COM7)"
        public string Description = "";     // "USB Serial Port"
        public string Manufacturer = "";
        public string InstanceId = "";      // "USB\VID_0403&PID_6001\A5XK3RJT"
        public string HardwareIds = "";
        public string LocationInfo = "";
        public string Vid = "";
        public string Pid = "";
        public string SerialNumber = "";
        public bool Present;
        public BusKind Bus = BusKind.Other;

        public string Key { get { return InstanceId; } }

        public bool IsUsb { get { return Bus == BusKind.Usb; } }

        /// <summary>Friendly name with the trailing "(COMx)" stripped — the menu shows the port separately.</summary>
        public string DisplayName
        {
            get
            {
                string name = !string.IsNullOrEmpty(FriendlyName) ? FriendlyName : Description;
                if (string.IsNullOrEmpty(name)) name = "Serial port";
                int idx = name.LastIndexOf(" (" + PortName + ")", StringComparison.OrdinalIgnoreCase);
                if (idx > 0) name = name.Substring(0, idx);
                return name.Trim();
            }
        }

        /// <summary>Best-effort vendor label: the VID lookup wins over generic driver strings.</summary>
        public string VendorLabel
        {
            get
            {
                string known = UsbVendors.Lookup(Vid);
                if (!string.IsNullOrEmpty(known)) return known;
                if (!string.IsNullOrEmpty(Manufacturer) &&
                    Manufacturer.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) < 0 &&
                    Manufacturer.IndexOf("(Standard", StringComparison.OrdinalIgnoreCase) < 0)
                    return Manufacturer;
                return "";
            }
        }

        public string BusLabel
        {
            get
            {
                switch (Bus)
                {
                    case BusKind.Usb: return "USB";
                    case BusKind.Bluetooth: return "Bluetooth";
                    case BusKind.Internal: return "On-board";
                    case BusKind.Virtual: return "Virtual";
                    default: return "Other";
                }
            }
        }

        public string VidPid
        {
            get
            {
                if (string.IsNullOrEmpty(Vid)) return "";
                return "VID_" + Vid + " PID_" + Pid;
            }
        }

        /// <summary>One line for menus / toasts: "COM7  —  USB Serial Port (FTDI)".</summary>
        public string MenuText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(PortName).Append("   ").Append(DisplayName);
                string vendor = VendorLabel;
                if (!string.IsNullOrEmpty(vendor) &&
                    DisplayName.IndexOf(vendor, StringComparison.OrdinalIgnoreCase) < 0)
                    sb.Append(" (").Append(vendor).Append(")");
                return sb.ToString();
            }
        }

        public string ToolTipText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(PortName + " — " + DisplayName);
                if (!string.IsNullOrEmpty(VendorLabel)) sb.AppendLine("Vendor: " + VendorLabel);
                if (!string.IsNullOrEmpty(VidPid)) sb.AppendLine(VidPid);
                if (!string.IsNullOrEmpty(SerialNumber)) sb.AppendLine("Serial: " + SerialNumber);
                sb.AppendLine("Bus: " + BusLabel + (Present ? "" : "  (not connected)"));
                sb.Append(InstanceId);
                return sb.ToString();
            }
        }
    }

    /// <summary>Small VID table so common adapters get a human name even with a generic driver string.</summary>
    internal static class UsbVendors
    {
        public static string Lookup(string vid)
        {
            if (string.IsNullOrEmpty(vid)) return "";
            switch (vid.ToUpperInvariant())
            {
                case "0403": return "FTDI";
                case "10C4": return "Silicon Labs";
                case "1A86": return "QinHeng (CH34x)";
                case "067B": return "Prolific";
                case "2341": return "Arduino";
                case "2A03": return "Arduino";
                case "239A": return "Adafruit";
                case "303A": return "Espressif";
                case "0483": return "STMicroelectronics";
                case "2E8A": return "Raspberry Pi";
                case "16C0": return "Van Ooijen / Teensy";
                case "1B4F": return "SparkFun";
                case "04D8": return "Microchip";
                case "0525": return "Linux gadget";
                case "1FC9": return "NXP";
                case "1D50": return "OpenMoko (community)";
                case "0BDA": return "Realtek";
                case "1546": return "u-blox";
                case "12D1": return "Huawei";
                case "05C6": return "Qualcomm";
                case "1199": return "Sierra Wireless";
                case "0CF3": return "Atheros";
                case "046D": return "Logitech";
                case "0908": return "Siemens";
                case "1366": return "SEGGER";
                case "0D28": return "ARM mbed";
                case "C251": return "Keil";
                default: return "";
            }
        }
    }
}
