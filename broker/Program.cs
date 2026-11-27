using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--notification-activation-broker")
        {
            if (args.Length == 2)
                NotificationActivationBroker.ServeAsync(args[1]).GetAwaiter().GetResult();
            else if (args.Length == 3 && args[2] == "--keep-alive")
                NotificationActivationBroker.ServeAsync(args[1], keepAlive: true).GetAwaiter().GetResult();
        }
    }
}
