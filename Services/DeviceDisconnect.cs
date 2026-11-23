using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DynamicIslandWin.Services;

internal static class DeviceDisconnect
{
    private const uint EjectSupported = 0x02;
    private const uint Removable = 0x04;
    private const uint BluetoothDisconnect = 0x41000C; // IOCTL_BTH_DISCONNECT_DEVICE, bthioctl.h

    internal static bool Request(ConnectedDevices.Device device)
    {
        if (device.BluetoothAddress is ulong address and > 0) return DisconnectBluetooth(address);
        if (!device.Bluetooth && device.UsbInstanceId is { } instance && IsPhysicalUsbNode(instance))
        {
            if (CM_Locate_DevNode(out uint node, instance, 0) != 0) return false;
            // Rely on the normal Plug and Play query/remove flow so an in-use device can veto.
            return CM_Request_Device_Eject(node, out _, new StringBuilder(260), 260, 0) == 0;
        }
        return false;
    }

    internal static bool CanEjectUsb(string id, uint capabilities, IEnumerable<string> compatibleIds) =>
        IsPhysicalUsbNode(id) && (capabilities & (EjectSupported | Removable)) != 0
        && !compatibleIds.Any(value => value.StartsWith(@"USB\Class_09", StringComparison.OrdinalIgnoreCase));

    private static bool IsPhysicalUsbNode(string id) =>
        id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    private static bool DisconnectBluetooth(ulong address)
    {
        uint size = sizeof(uint); // BLUETOOTH_FIND_RADIO_PARAMS has one DWORD.
        IntPtr search = BluetoothFindFirstRadio(ref size, out SafeFileHandle radio);
        if (search == IntPtr.Zero)
        {
            radio?.Dispose();
            return false;
        }
        try
        {
            do
            {
                using (radio)
                {
                    if (!radio.IsInvalid && DeviceIoControl(radio, BluetoothDisconnect, in address, sizeof(ulong),
                        IntPtr.Zero, 0, out _, IntPtr.Zero)) return true;
                }
            } while (BluetoothFindNextRadio(search, out radio));
            radio?.Dispose();
            return false;
        }
        finally { BluetoothFindRadioClose(search); }
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNode(out uint node, string id, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Request_Device_Eject(uint node, out uint veto, [Out] StringBuilder vetoName, uint nameLength, uint flags);

    [DllImport("bthprops.cpl")]
    private static extern IntPtr BluetoothFindFirstRadio(ref uint parameters, out SafeFileHandle radio);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr search, out SafeFileHandle radio);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr search);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, in ulong input, uint inputLength,
        IntPtr output, uint outputLength, out uint bytes, IntPtr overlapped);
}
