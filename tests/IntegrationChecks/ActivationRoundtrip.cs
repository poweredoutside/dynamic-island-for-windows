using System.Runtime.InteropServices;
using DynamicIslandWin.Services;

public static class ActivationRoundtrip
{
    internal static bool Run(string host) => Task.Run(() =>
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
        uint registration = 0;
        var callback = new Callback();
        var factory = new Factory(callback);
        var clsid = Guid.NewGuid();
        const string appId = "DynamicIsland.Tests.Chat";
        const string arguments = "action=open&session=test-account&peer=test-chat&msg=123&topic=456&unicode=Привет";
        var desktop = new NotificationActivationBroker.DesktopSession();
        try
        {
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(ref clsid, factory, 4, 1, out registration));
            if (!ActivateAndCheck(arguments, 1) || !ActivateAndCheck(null, 2)) return false;
            var superseded = new NotificationActivationService.Target(appId, arguments, clsid);
            bool delivered = NotificationActivationBroker.RunAsync(superseded, executable: host,
                cancellation: new CancellationToken(canceled: true)).GetAwaiter().GetResult();
            lock (callback) if (delivered || callback.Calls != 2) return false;

            desktop.WarmUpAsync(host).GetAwaiter().GetResult();
            lock (callback) if (callback.Calls != 2) return false;
            return ActivateAndCheck(arguments, 3, desktop) && ActivateAndCheck(null, 4, desktop);
        }
        finally
        {
            desktop.CloseAsync().GetAwaiter().GetResult();
            if (registration != 0) CoRevokeClassObject(registration);
            GC.KeepAlive(factory);
            GC.KeepAlive(callback);
            CoUninitialize();
        }

        bool ActivateAndCheck(string? expectedArguments, int expectedCalls, NotificationActivationBroker.DesktopSession? session = null)
        {
            var target = new NotificationActivationService.Target(appId, expectedArguments, clsid);
            bool completed = (session?.RunAsync(target, executable: host) ??
                NotificationActivationBroker.RunAsync(target, executable: host)).GetAwaiter().GetResult();
            lock (callback)
                return completed && callback.Calls == expectedCalls && callback.AppId == appId &&
                    callback.Arguments == expectedArguments && callback.NoUserInput;
        }
    }).GetAwaiter().GetResult();

    [ComVisible(true), Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICallback
    {
        [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, IntPtr data, uint count);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Callback : ICallback
    {
        internal int Calls;
        internal string? AppId, Arguments;
        internal bool NoUserInput;
        public int Activate(string appId, string? arguments, IntPtr data, uint count)
        {
            lock (this)
            {
                Calls++;
                AppId = appId;
                Arguments = arguments;
                NoUserInput = data == IntPtr.Zero && count == 0;
            }
            return 0;
        }
    }

    [ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFactory
    {
        [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr value);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Factory(Callback callback) : IFactory
    {
        public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
            IntPtr unknown = Marshal.GetIUnknownForObject(callback);
            try { return Marshal.QueryInterface(unknown, ref iid, out value); }
            finally { Marshal.Release(unknown); }
        }
        public int LockServer(bool locked) => 0;
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoRegisterClassObject(ref Guid clsid,
        [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint registration);
    [DllImport("ole32.dll")] private static extern int CoRevokeClassObject(uint registration);
}
