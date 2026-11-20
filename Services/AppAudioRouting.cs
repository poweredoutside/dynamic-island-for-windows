using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DynamicIslandWin.Services;

// Windows Volume Mixer uses this internal WinRT class for per-app output.
// The interface ID changed in Windows 11; the relevant ABI slots did not.
internal sealed class AppAudioRouting : IDisposable
{
    private const string RuntimeClass = "Windows.Media.Internal.AudioPolicyConfig";
    private const string RenderPrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const int NotFound = unchecked((int)0x80070490);
    private const int InvalidArgument = unchecked((int)0x80070057);
    private static readonly Role[] Roles = [Role.Communications, Role.Multimedia, Role.Console];
    private readonly IntPtr _factory;
    private readonly RouteWrite _write;
    private readonly RouteRead _read;

    internal AppAudioRouting()
    {
        var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
        var legacy = new Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258");
        IntPtr className = MakeString(RuntimeClass);
        try
        {
            int result = RoGetActivationFactory(className, ref iid, out _factory);
            if (result < 0) Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref legacy, out _factory));
        }
        finally { WindowsDeleteString(className); }
        var table = Marshal.ReadIntPtr(_factory);
        _write = Marshal.GetDelegateForFunctionPointer<RouteWrite>(Marshal.ReadIntPtr(table, 25 * IntPtr.Size));
        _read = Marshal.GetDelegateForFunctionPointer<RouteRead>(Marshal.ReadIntPtr(table, 26 * IntPtr.Size));
    }

    internal void SetOutput(uint pid, string? id)
    {
        string? packed = id == null ? null : RenderPrefix + id + RenderSuffix;
        var previous = Roles.Select(role => ReadRoute(pid, role)).ToArray();
        int applied = 0;
        try
        {
            foreach (var role in Roles)
            {
                WriteRoute(pid, role, packed);
                applied++;
            }
        }
        catch (COMException)
        {
            for (int i = 0; i < applied; i++)
            {
                try { WriteRoute(pid, Roles[i], previous[i]); }
                catch (COMException ex) { AppLog.Error("Restore app audio output", ex); }
            }
            throw;
        }
    }

    internal string? GetOutput(uint pid)
    {
        foreach (var role in Roles)
        {
            string? value = ReadRoute(pid, role);
            if (value != null && value.StartsWith(RenderPrefix, StringComparison.OrdinalIgnoreCase)
                && value.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase))
                return value[RenderPrefix.Length..^RenderSuffix.Length];
        }
        return null;
    }

    private string? ReadRoute(uint pid, Role role)
    {
        int result = _read(_factory, pid, DataFlow.Render, role, out IntPtr value);
        try
        {
            if (value == IntPtr.Zero && (result == NotFound || result == InvalidArgument)) return null;
            CheckResult(result);
            return value == IntPtr.Zero ? null : Marshal.PtrToStringUni(WindowsGetStringRawBuffer(value, out uint length), (int)length);
        }
        finally { WindowsDeleteString(value); }
    }

    private void WriteRoute(uint pid, Role role, string? value)
    {
        IntPtr endpoint = value == null ? IntPtr.Zero : MakeString(value);
        try { CheckResult(_write(_factory, pid, DataFlow.Render, role, endpoint)); }
        finally { WindowsDeleteString(endpoint); }
    }

    private static IntPtr MakeString(string value)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint)value.Length, out IntPtr handle));
        return handle;
    }

    private static void CheckResult(int result)
    {
        if (result < 0) throw new COMException("Windows audio routing failed.", result);
    }

    public void Dispose() => Marshal.Release(_factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RouteWrite(IntPtr self, uint processId, DataFlow flow, Role role, IntPtr endpoint);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RouteRead(IntPtr self, uint processId, DataFlow flow, Role role, out IntPtr endpoint);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string text, uint length, out IntPtr handle);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr handle, out uint length);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr handle);
}
