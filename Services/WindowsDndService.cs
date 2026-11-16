using System.Runtime.InteropServices;

namespace DynamicIslandWin.Services;

internal sealed class WindowsDndService : IDisposable
{
    internal const string Off = "Microsoft.QuietHoursProfile.Unrestricted";
    internal const string Priority = "Microsoft.QuietHoursProfile.PriorityOnly";
    internal const string Alarms = "Microsoft.QuietHoursProfile.AlarmsOnly";
    private IQuietHoursSettings? _settings;

    internal readonly record struct State(string SelectedProfile, string ActiveProfile)
    {
        public bool IsActive => ActiveProfile != Off;
    }

    private IQuietHoursSettings Settings => _settings ??= (IQuietHoursSettings)new QuietHoursSettings();

    public bool TryRead(out State state)
    {
        try
        {
            state = new State(Settings.GetUserSelectedProfile(), Settings.GetActiveProfile());
            if (!IsKnown(state.SelectedProfile) || !IsKnown(state.ActiveProfile))
                throw new NotSupportedException("Windows returned an unknown Quiet Hours profile.");
            return true;
        }
        catch (Exception ex)
        {
            if (_settings != null) AppLog.Error("Read Windows Do Not Disturb", ex);
            Dispose(); // Reconnect after an Explorer/Shell restart.
            state = default;
            return false;
        }
    }

    public bool TrySetProfile(string profile, bool endAutomaticRule, out string? error)
    {
        try
        {
            if (!IsKnown(profile)) throw new ArgumentException("Unknown Quiet Hours profile.", nameof(profile));
            Settings.SetUserSelectedProfile(profile);
            if (endAutomaticRule && profile == Off && Settings.GetActiveProfile() != Off)
            {
                var manager = Settings.GetQuietMomentsManager();
                try { manager.TurnOffCurrentlyActiveQuietMoment(); }
                finally { Marshal.ReleaseComObject(manager); }
            }
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Set Windows Do Not Disturb", ex);
            Dispose();
            error = $"0x{ex.HResult:X8}";
            return false;
        }
    }

    private static bool IsKnown(string profile) => profile is Off or Priority or Alarms;

    public void Dispose()
    {
        if (_settings != null) Marshal.ReleaseComObject(_settings);
        _settings = null;
    }

    // Windows has no public API for the DND switch. This Shell COM interface is
    // undocumented: keep the ABI in one place, verify writes, and fail visibly.
    // ABI reference: https://withinrafael.com/2019/09/19/determine-if-your-app-is-in-a-focus-assist-profiles-priority-list/
    [ComImport, Guid("F53321FA-34F8-4B7F-B9A3-361877CB94CF")]
    private class QuietHoursSettings { }

    [ComImport, Guid("6BFF4732-81EC-4FFB-AE67-B6C1BC29631F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQuietHoursSettings
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetUserSelectedProfile();
        void SetUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
        IntPtr GetProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
        void GetAllProfileData(out uint count, out IntPtr data);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetDisplayNameForProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
        IQuietMomentsManager GetQuietMomentsManager();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetOffProfileId();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetActiveQuietMomentProfile();
        void SetActiveQuietMomentProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetActiveProfile();
    }

    [ComImport, Guid("B0217783-87B7-422C-B902-5C148C14F150"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQuietMomentsManager
    {
        void GetAllQuietMomentModes(out uint count, out IntPtr modes);
        IntPtr GetQuietMoment(uint id);
        void TurnOffCurrentlyActiveQuietMoment();
        uint GetActiveQuietMoment();
    }
}
