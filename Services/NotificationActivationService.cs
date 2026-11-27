using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DynamicIslandWin.Services;

// UserNotificationListener exposes only the visual. Preserve the original default
// activation from the local Windows store, then invoke its protocol or callback.
// This store is undocumented: every lookup is read-only and may safely return null.
internal static class NotificationActivationService
{
    internal sealed class Target(string appId, string? arguments, Guid activator)
    {
        internal Target(string appId, Uri protocol, string? protocolPackage) : this(appId, null, Guid.Empty)
        {
            Protocol = protocol;
            ProtocolPackage = protocolPackage;
        }

        internal string AppId { get; } = appId;
        internal string? Arguments { get; } = arguments;
        internal Guid Activator { get; } = activator;
        internal Uri? Protocol { get; }
        internal string? ProtocolPackage { get; }
        public override string ToString() => nameof(Target); // Never expose chat/account arguments in diagnostics.
    }

    internal sealed class ToastAction(string? arguments, Uri? protocol = null, string? protocolPackage = null)
    {
        internal string? Arguments { get; } = arguments;
        internal Uri? Protocol { get; } = protocol;
        internal string? ProtocolPackage { get; } = protocolPackage;
    }

    private static readonly object CacheLock = new();
    private static DateTime _cacheUntil;
    private static Dictionary<string, Guid> _activators = new(StringComparer.Ordinal);

    internal static Task<Target?> CaptureAsync(uint id, string appId, DateTimeOffset created, string fingerprint,
        string? packageManifestPath = null)
        => Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(appId)) return null;
            try
            {
                // Capture the payload before shortcut discovery: senders can withdraw a toast quickly.
                ToastAction? action = null;
                foreach (int delay in new[] { 0, 40, 100 })
                {
                    if (delay != 0) await Task.Delay(delay).ConfigureAwait(false);
                    action = ReadAction(id, appId, created, fingerprint);
                    if (action != null) break;
                }
                if (action == null) return null;
                if (action.Protocol != null)
                    return new Target(appId, action.Protocol, action.ProtocolPackage);
                Guid activator = ResolveActivator(appId, packageManifestPath);
                return activator == Guid.Empty ? null : new Target(appId, action.Arguments, activator);
            }
            catch (Exception ex) { AppLog.Error("Capture notification activation", ex); return null; }
        });

    internal static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static ToastAction? ParseAction(byte[] payload, long arrival, DateTimeOffset created, string fingerprint,
        string? appId = null, string? notificationTag = null)
    {
        if (payload.Length is 0 or > 131072) return null;
        try
        {
            // Reject reused IDs, updates with different contents, and non-default actions.
            if (Math.Abs((DateTimeOffset.FromFileTime(arrival) - created).TotalSeconds) > 2) return null;
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 131072
            });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "toast") return null;
            string kind = (string?)root.Attribute("activationType") ?? "foreground";
            if (kind is not ("foreground" or "protocol")) return null;
            var bindings = root.Element("visual")?.Elements("binding");
            var binding = bindings?.FirstOrDefault(e => (string?)e.Attribute("template") == "ToastGeneric")
                ?? bindings?.FirstOrDefault(e => e.Elements("text").Any());
            if (binding == null) return null;
            string text = string.Join(" · ", binding.Elements("text").Select(e => e.Value)
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            if (!string.Equals(Fingerprint(text), fingerprint, StringComparison.Ordinal)) return null;
            string? launch = (string?)root.Attribute("launch");
            if (launch is { Length: > 16384 } || launch?.Contains('\0') == true) return null;
            if (kind == "protocol")
            {
                // Windows handlers may use their own escaping in the protocol payload.
                if (launch == null || launch.Any(char.IsControl) ||
                    !Uri.TryCreate(launch, UriKind.Absolute, out var uri) || uri.IsFile) return null;
                string? package = (string?)root.Attribute("protocolActivationTargetApplicationPfn");
                if (package != null && !System.Text.RegularExpressions.Regex.IsMatch(package, @"\A[A-Za-z0-9._-]{1,255}\z"))
                    return null;
                return new ToastAction(null, uri, package);
            }
            if (!string.IsNullOrEmpty(launch)) return new ToastAction(launch);
            // Electron's default toast body has no launch attribute. Its registered
            // activator routes clicks by the original Windows tag to the live
            // Notification object, which owns Discord's chat-opening callback.
            if (appId is "com.squirrel.Discord.Discord" or "com.squirrel.DiscordPTB.DiscordPTB" or "com.squirrel.DiscordCanary.DiscordCanary")
                return notificationTag != null && System.Text.RegularExpressions.Regex.IsMatch(notificationTag, @"\A[A-Za-z0-9._:-]{1,128}\z")
                    ? new ToastAction("type=click&tag=" + notificationTag) : null;
            return new ToastAction(null);
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static ToastAction? ReadAction(uint id, string appId, DateTimeOffset created, string fingerprint)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            // SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX. Never write or copy the notification store.
            if (sqlite3_open_v2(Utf8(path), out db, 0x1 | 0x8000, IntPtr.Zero) != 0) return null;
            sqlite3_busy_timeout(db, 80);
            const string sql = "SELECT n.ArrivalTime,n.Payload,n.Tag FROM Notification n " +
                "JOIN NotificationHandler h ON h.RecordId=n.HandlerId " +
                "WHERE n.Id=? AND h.PrimaryId=? AND n.Type='toast' LIMIT 2";
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out statement, IntPtr.Zero) != 0) return null;
            if (sqlite3_bind_int64(statement, 1, id) != 0 ||
                sqlite3_bind_text(statement, 2, Utf8(appId), -1, new IntPtr(-1)) != 0) return null;
            if (sqlite3_step(statement) != 100) return null;
            long arrival = sqlite3_column_int64(statement, 0);
            int length = sqlite3_column_bytes(statement, 1);
            if (length is <= 0 or > 131072) return null;
            var payload = new byte[length];
            Marshal.Copy(sqlite3_column_blob(statement, 1), payload, 0, length);
            IntPtr tagPointer = sqlite3_column_text(statement, 2);
            int tagLength = sqlite3_column_bytes(statement, 2);
            string? tag = tagPointer != IntPtr.Zero && tagLength is > 0 and <= 128
                ? Marshal.PtrToStringUTF8(tagPointer, tagLength) : null;
            // Ambiguous identity must never activate a different message.
            if (sqlite3_step(statement) != 101) return null;
            return ParseAction(payload, arrival, created, fingerprint, appId, tag);
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }

    internal static Guid ParseManifestActivator(byte[] manifest, string appId)
    {
        int separator = appId.IndexOf('!');
        if (separator < 1 || manifest.Length > 262144) return Guid.Empty;
        try
        {
            using var stream = new MemoryStream(manifest, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144
            });
            var document = XDocument.Load(reader);
            var application = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" &&
                (string?)e.Attribute("Id") == appId[(separator + 1)..]);
            var extension = application?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Extension" &&
                (string?)e.Attribute("Category") == "windows.toastNotificationActivation");
            string? value = (string?)extension?.Elements().FirstOrDefault(e => e.Name.LocalName == "ToastNotificationActivation")?
                .Attribute("ToastActivatorCLSID");
            return Guid.TryParse(value, out var clsid) ? clsid : Guid.Empty;
        }
        catch (XmlException) { return Guid.Empty; }
    }

    internal static Guid ResolveActivator(string appId, string? packageManifestPath = null)
    {
        if (packageManifestPath != null)
        {
            try
            {
                if (new FileInfo(packageManifestPath).Length <= 262144)
                {
                    var packaged = ParseManifestActivator(File.ReadAllBytes(packageManifestPath), appId);
                    if (packaged != Guid.Empty) return packaged;
                }
            }
            catch (Exception ex) { AppLog.Error("Read notification package manifest", ex); }
        }
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId\" + appId))
            if (Guid.TryParse(key?.GetValue("CustomActivator") as string, out var registered)) return registered;
        using (var key = Registry.ClassesRoot.OpenSubKey(@"AppUserModelId\" + appId))
            if (Guid.TryParse(key?.GetValue("CustomActivator") as string, out var registered)) return registered;
        int separator = appId.IndexOf('!');
        if (separator > 0)
        {
            string family = appId[..separator];
            if (System.Text.RegularExpressions.Regex.IsMatch(family, @"\A[A-Za-z0-9._-]{1,255}\z"))
            {
                // Desktop Bridge clients can register a virtualized shortcut with the
                // package-relative AppID instead of declaring a COM server in the manifest.
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", family, @"LocalCache\Roaming\Microsoft\Windows\Start Menu");
                var packaged = ReadShortcutActivators(root).GetValueOrDefault(appId[(separator + 1)..]);
                if (packaged != Guid.Empty) return packaged;
            }
        }
        lock (CacheLock)
        {
            if (DateTime.UtcNow >= _cacheUntil)
            {
                _activators = ReadShortcutActivators();
                _cacheUntil = DateTime.UtcNow.AddMinutes(2);
            }
            return _activators.GetValueOrDefault(appId);
        }
    }

    private static Dictionary<string, Guid> ReadShortcutActivators(string? packageRoot = null)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var roots = packageRoot == null
            ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) }
            : new[] { packageRoot };
        foreach (string root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
                }))
                {
                    IPropertyStore? store = null;
                    try
                    {
                        Guid iid = typeof(IPropertyStore).GUID;
                        if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store) < 0 || store == null) continue;
                        var idKey = new PropertyKey(5);
                        var clsidKey = new PropertyKey(26);
                        PropVariant id = default, activator = default;
                        try
                        {
                            if (store.GetValue(ref idKey, out id) < 0 || store.GetValue(ref clsidKey, out activator) < 0) continue;
                            string? appId = id.Type == 31 ? Marshal.PtrToStringUni(id.Pointer) : null;
                            if (!string.IsNullOrWhiteSpace(appId) && activator.Type == 72 && activator.Pointer != IntPtr.Zero)
                                result[appId] = Marshal.PtrToStructure<Guid>(activator.Pointer);
                        }
                        finally { PropVariantClear(ref id); PropVariantClear(ref activator); }
                    }
                    catch (Exception ex) { AppLog.Error("Read notification shortcut", ex); }
                    finally { if (store != null) Marshal.ReleaseComObject(store); }
                }
            }
            catch (Exception ex) { AppLog.Error("Resolve notification activators", ex); }
        }
        return result;
    }

    internal static Task<bool> ActivateAsync(Target target) => Task.Run(() => UseCallback(target, invoke: true));
    internal static Task<bool> ProbeCallbackAsync(Target target) => Task.Run(() => UseCallback(target, invoke: false));

    private static bool UseCallback(Target target, bool invoke)
    {
        object? callback = null;
        int initialized = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            Guid clsid = target.Activator, iid = typeof(INotificationActivationCallback).GUID;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out callback);
            if (hr < 0 || callback == null)
            {
                AppLog.Notification($"activation callback unavailable hresult=0x{hr:X8}");
                return false;
            }
            // Only the body's default action. No reply/mark-read button or user input is fabricated.
            if (!invoke) return true;
            hr = ((INotificationActivationCallback)callback).Activate(target.AppId, target.Arguments, IntPtr.Zero, 0);
            if (hr != 0) AppLog.Notification($"activation callback unhandled hresult=0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex) { AppLog.Error("Activate original notification", ex); return false; }
        finally
        {
            if (callback != null) Marshal.ReleaseComObject(callback);
            if (initialized >= 0) CoUninitialize();
        }
    }

    internal static void AllowForegroundActivation() => AllowSetForegroundWindow(uint.MaxValue);

    internal static bool OpenSourceApp(string appId)
    {
        // Execute the exact registered shell item, including Win32 Start menu entries.
        var launch = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = 0x0000000C | 0x00000400, // SEE_MASK_INVOKEIDLIST | SEE_MASK_FLAG_NO_UI
            File = @"shell:AppsFolder\" + appId,
            Show = 1
        };
        return ShellExecuteEx(ref launch);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        internal int Size;
        internal uint Mask;
        internal IntPtr Window;
        internal string? Verb, File, Parameters, Directory;
        internal int Show;
        internal IntPtr Instance, IdList;
        internal string? Class;
        internal IntPtr ClassKey;
        internal uint HotKey;
        internal IntPtr IconOrMonitor, Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(uint id)
    {
        internal Guid Format = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        internal uint Id = id;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal IntPtr Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr context, uint flags,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + '\0');
    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, IntPtr data, uint count);
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? instance);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
}
