using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--notification-activation-broker")
            NotificationActivationBroker.ServeAsync(args[1]).GetAwaiter().GetResult();
    }
}
