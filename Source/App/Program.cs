using Avalonia;
using System;

namespace UnVault.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        string instanceName = Services.SingleInstance.DefaultName;
        if (Environment.GetEnvironmentVariable("UNVAULT_COMPARE") == "1")
            instanceName += ".Compare.Software";
        using var instance = Services.SingleInstance.Claim(instanceName);
        if (instance is null)
        {
            Services.SingleInstance.ShowRunning(instanceName);
            return;
        }
        Instance = instance;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>This copy, the running one; App shows the window when the app is started again.</summary>
    internal static Services.SingleInstance? Instance { get; private set; }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
