using System.Runtime.InteropServices;
using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace DynamicIslandWin.Services;

internal static class ConnectedDevices
{
    private const string Connected = "System.Devices.Connected";
    private const string Local = "System.Devices.LocalMachine";
    private const string Container = "System.Devices.ContainerId";
    private const string Category = "System.Devices.CategoryIds";
    private const string ConnectedAep = "System.Devices.Aep.IsConnected";
    private const string AepContainer = "System.Devices.Aep.ContainerId";
    private const string Address = "System.Devices.Aep.DeviceAddress";
    private const string Problem = "System.Devices.DeviceHasProblem";
    private const string Capabilities = "System.Devices.DeviceCapabilities";
    private const string CompatibleIds = "System.Devices.CompatibleIds";
    private const string Parent = "System.Devices.Parent";
    // Windows Bluetooth drivers expose the reported battery percentage on their PnP nodes.
    private const string BatteryLevel = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string True = ":=System.StructuredQueryType.Boolean#True";

    internal sealed record Device(string Name, string Kind, bool Bluetooth, int? Percent = null, bool? Charging = null,
        bool HasProblem = false, ulong? BluetoothAddress = null, string? UsbInstanceId = null)
    {
        internal bool CanDisconnect => BluetoothAddress.HasValue || UsbInstanceId != null;
    }

    internal static IDisposable Observe(Action changed) => new ChangeMonitor(changed);

    private sealed class ChangeMonitor : IDisposable
    {
        private readonly List<DeviceWatcher> _watchers = [];
        private readonly Action _changed;
        private volatile bool _disposed;

        internal ChangeMonitor(Action changed)
        {
            _changed = changed;
            Watch(Connected + True, [Connected, BatteryLevel], DeviceInformationKind.DeviceContainer);
            Watch(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                [ConnectedAep, BatteryLevel], DeviceInformationKind.AssociationEndpoint);
            Watch(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                [ConnectedAep, BatteryLevel], DeviceInformationKind.AssociationEndpoint);
        }

        private void Watch(string selector, string[] properties, DeviceInformationKind kind)
        {
            DeviceWatcher? watcher = null;
            try
            {
                watcher = DeviceInformation.CreateWatcher(selector, properties, kind);
                watcher.Added += Added;
                watcher.Updated += Updated;
                watcher.Removed += Updated;
                watcher.EnumerationCompleted += Completed;
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (DeviceUnavailable(ex))
            {
                if (watcher != null) Stop(watcher);
                AppLog.Error("Observe connected devices", ex);
            }
        }

        private void Added(DeviceWatcher sender, DeviceInformation device) => NotifyIfReady(sender);
        private void Updated(DeviceWatcher sender, DeviceInformationUpdate device) => NotifyIfReady(sender);
        private void Completed(DeviceWatcher sender, object args) { if (!_disposed) _changed(); }
        private void NotifyIfReady(DeviceWatcher sender)
        {
            if (!_disposed && sender.Status == DeviceWatcherStatus.EnumerationCompleted) _changed();
        }

        private void Stop(DeviceWatcher watcher)
        {
            try
            {
                watcher.Added -= Added;
                watcher.Updated -= Updated;
                watcher.Removed -= Updated;
                watcher.EnumerationCompleted -= Completed;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }
            catch (Exception ex) when (DeviceUnavailable(ex)) { }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var watcher in _watchers) Stop(watcher);
            _watchers.Clear();
        }
    }

    internal static async Task<IReadOnlyList<Device>?> ReadAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var containers = FindAsync(Connected + True, [Connected, Local, Category, BatteryLevel], DeviceInformationKind.DeviceContainer, timeout.Token);
        var nodes = FindAsync("System.Devices.Present" + True, [Container, BatteryLevel, Problem, Capabilities, CompatibleIds, Parent],
            DeviceInformationKind.Device, timeout.Token);
        var classic = FindAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            [AepContainer, Container, ConnectedAep, Address, BatteryLevel], DeviceInformationKind.AssociationEndpoint, timeout.Token);
        var lowEnergy = FindAsync(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            [AepContainer, Container, ConnectedAep, Address, BatteryLevel], DeviceInformationKind.AssociationEndpoint, timeout.Token);
        var batteries = FindAsync(Battery.GetDeviceSelector(), [Container], DeviceInformationKind.DeviceInterface, timeout.Token);
        await Task.WhenAll(containers, nodes, classic, lowEnergy, batteries);
        if (containers.Result == null && classic.Result == null && lowEnergy.Result == null) return null;
        var candidates = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeGroups = (nodes.Result ?? []).Where(node => ContainerId(node, Container) != null)
            .ToLookup(node => ContainerId(node, Container)!);
        foreach (var container in containers.Result ?? [])
        {
            if (!Guid.TryParse(container.Id, out var id)) continue;
            string key = id.ToString("D");
            if (Property(container, Local) is true) { localIds.Add(key); continue; }
            if (Property(container, Connected) is false || string.IsNullOrWhiteSpace(container.Name)) continue;
            var children = nodeGroups[key];
            bool bluetooth = children.Any(node => IsBluetoothNode(node.Id));
            var categories = Property(container, Category) as string[] ?? [];
            bool problem = children.Any(node => Property(node, Problem) is true);
            string? usbId = bluetooth ? null : UsbEjectTarget(children);
            candidates[key] = new Device(container.Name, Kind(categories), bluetooth, Percent(container) ?? NodePercent(children),
                HasProblem: problem, UsbInstanceId: usbId);
        }

        var bluetoothKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var liveBluetooth = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in (classic.Result ?? []).Concat(lowEnergy.Result ?? []))
        {
            if (Property(endpoint, ConnectedAep) is not true || string.IsNullOrWhiteSpace(endpoint.Name)) continue;
            string? address = Property(endpoint, Address) is string value ? NormalizeAddress(value) : null;
            string? id = ContainerId(endpoint, Container) ?? ContainerId(endpoint, AepContainer);
            var matchingNodes = address == null ? [] : (nodes.Result ?? []).Where(node => IsAddressNode(node.Id, address)).ToArray();
            string key = matchingNodes.Select(node => ContainerId(node, Container)).OfType<string>().FirstOrDefault(candidates.ContainsKey)
                ?? id ?? endpoint.Id;
            if (address != null && bluetoothKeys.TryGetValue(address, out var previous)) key = previous;
            else if (address != null) bluetoothKeys.Add(address, key);
            if (localIds.Contains(key)) continue;
            liveBluetooth.Add(key);
            candidates.TryGetValue(key, out var current);
            int? percent = Percent(endpoint) ?? NodePercent(matchingNodes) ?? NodePercent(nodeGroups[key]) ?? current?.Percent;
            ulong? radioAddress = address != null && ulong.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong mac) && mac != 0
                ? mac : current?.BluetoothAddress;
            candidates[key] = new Device(endpoint.Name, current?.Kind ?? "Bluetooth", true, percent, HasProblem: current?.HasProblem == true,
                BluetoothAddress: radioAddress);
        }

        // Windows keeps paired Bluetooth containers and PnP nodes present after power-off.
        foreach (string id in candidates.Where(pair => pair.Value.Bluetooth && !liveBluetooth.Contains(pair.Key)).Select(pair => pair.Key).ToArray())
            candidates.Remove(id);

        foreach (var battery in batteries.Result ?? [])
        {
            string? id = ContainerId(battery, Container);
            if (id == null || !candidates.TryGetValue(id, out var current)) continue;
            try
            {
                var batteryDevice = await Battery.FromIdAsync(battery.Id).AsTask(timeout.Token);
                if (batteryDevice != null) candidates[id] = ApplyReport(current, batteryDevice.GetReport());
            }
            catch (Exception ex) when (DeviceUnavailable(ex)) { }
        }

        var controllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in RawGameController.RawGameControllers)
        {
            try
            {
                string? id = Guid.TryParse(controller.NonRoamableId, out var controllerId) ? controllerId.ToString("D") : null;
                if (id == null || !candidates.ContainsKey(id))
                    id = candidates.Where(pair => !controllers.Contains(pair.Key)
                        && pair.Value.Name.Equals(controller.DisplayName, StringComparison.OrdinalIgnoreCase))
                        .Select(pair => pair.Key).FirstOrDefault();
                id ??= "controller:" + controller.NonRoamableId;
                controllers.Add(id);
                if (!candidates.TryGetValue(id, out var current))
                    current = new Device(controller.DisplayName, "Геймпад", false);
                candidates[id] = ApplyReport(current, controller.TryGetBatteryReport()) with { Kind = "Геймпад" };
            }
            catch (Exception ex) when (DeviceUnavailable(ex)) { }
        }
        return candidates.Values.OrderByDescending(device => device.Percent.HasValue || device.Charging.HasValue)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static int? ReadPercent(object? value) => value switch
    {
        byte percent when percent <= 100 => percent,
        ushort percent when percent <= 100 => percent,
        uint percent when percent <= 100 => (int)percent,
        int percent when percent is >= 0 and <= 100 => percent,
        _ => null
    };

    private static Device ApplyReport(Device device, BatteryReport? report)
    {
        if (report == null || report.Status == BatteryStatus.NotPresent) return device;
        int? percent = report.FullChargeCapacityInMilliwattHours is > 0 and var full
            && report.RemainingCapacityInMilliwattHours is >= 0 and var remaining
            ? (int)Math.Clamp(Math.Round(100d * remaining / full), 0, 100) : device.Percent;
        return device with { Percent = percent, Charging = report.Status == BatteryStatus.Charging };
    }

    private static async Task<DeviceInformation[]?> FindAsync(string selector, string[] properties, DeviceInformationKind kind, CancellationToken token)
    {
        try { return (await DeviceInformation.FindAllAsync(selector, properties, kind).AsTask(token)).ToArray(); }
        catch (Exception ex) when (DeviceUnavailable(ex)) { return null; }
    }

    private static bool DeviceUnavailable(Exception ex) =>
        ex is COMException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or ArgumentException;

    private static object? Property(DeviceInformation device, string key) => device.Properties.TryGetValue(key, out var value) ? value : null;
    private static int? Percent(DeviceInformation device) => ReadPercent(Property(device, BatteryLevel));
    private static int? NodePercent(IEnumerable<DeviceInformation> nodes) => nodes.Select(Percent).FirstOrDefault(value => value.HasValue);
    private static string? ContainerId(DeviceInformation device, string key) =>
        Property(device, key) is Guid id && id != Guid.Empty ? id.ToString("D") : null;
    private static bool IsBluetoothNode(string id) => id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase);
    private static bool IsAddressNode(string id, string address) =>
        IsBluetoothNode(id) && id.Contains(address, StringComparison.OrdinalIgnoreCase);
    private static string? NormalizeAddress(string address)
    {
        var hex = address.Replace(":", "").Replace("-", "");
        return hex.Length == 12 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static string? UsbEjectTarget(IEnumerable<DeviceInformation> nodes)
    {
        var children = nodes.ToArray();
        var ids = children.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = children.Where(node => Property(node, Capabilities) is uint caps
            && DeviceDisconnect.CanEjectUsb(node.Id, caps, Property(node, CompatibleIds) as string[] ?? [])
            && (Property(node, Parent) is not string parent || !ids.Contains(parent))).Take(2).ToArray();
        return targets.Length == 1 ? targets[0].Id : null;
    }

    private static string Kind(string[] categories)
    {
        bool Includes(string value) => categories.Any(category => category.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (Includes("Headphone") || Includes("Headset")) return "Наушники";
        if (Includes("Mouse")) return "Мышь";
        if (Includes("Keyboard")) return "Клавиатура";
        if (Includes("Gaming")) return "Геймпад";
        if (Includes("Display")) return "Дисплей";
        return "Устройство";
    }
}
