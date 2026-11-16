using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel;

namespace DynamicIslandWin.Services;

internal static class NotificationIconService
{
    // Desktop senders can have an AppInfo but no DisplayInfo logo. Resolve their
    // registered AppsFolder entry rather than guessing an executable from its name.
    internal static Task<BitmapSource?> LoadAsync(string appId, AppInfo? appInfo) => Task.Run(async () =>
    {
        if (appInfo != null)
        {
            try
            {
                var logo = appInfo.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
                if (logo != null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var random = await logo.OpenReadAsync().AsTask(timeout.Token);
                    using var stream = random.AsStreamForRead();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return (BitmapSource)bitmap;
                }
            }
            catch (Exception ex) { AppLog.Notification($"icon logo unavailable hresult=0x{ex.HResult:X8}"); }
        }
        return LoadShellIcon(appId);
    });

    private static BitmapSource? LoadShellIcon(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        IShellItemImageFactory? factory = null;
        IntPtr handle = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(
                @"shell:AppsFolder\" + appId, IntPtr.Zero, ref iid, out factory));
            // ICONONLY, BIGGERSIZEOK: no thumbnails or shortcut overlays.
            Marshal.ThrowExceptionForHR(factory.GetImage(new NativeSize { Width = 48, Height = 48 }, 0x5, out handle));
            if (handle == IntPtr.Zero) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) { AppLog.Error("Load notification Shell icon", ex); return null; }
        finally
        {
            if (handle != IntPtr.Zero) DeleteObject(handle);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
