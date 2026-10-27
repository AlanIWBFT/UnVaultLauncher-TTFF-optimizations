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
            instanceName += Environment.GetEnvironmentVariable("UNVAULT_RENDERING") switch
            {
                "warp" => ".Compare.Warp",
                "hardware" => ".Compare.Hardware",
                _ => ".Compare.Deferred"
            };
        using var instance = Services.SingleInstance.Claim(instanceName);
        if (instance is null)
        {
            Services.SingleInstance.ShowRunning(instanceName);
            return;
        }
        Instance = instance;
        using var shaderCache = Environment.GetEnvironmentVariable("UNVAULT_WARP_CACHE") == "0" ||
            Environment.GetEnvironmentVariable("UNVAULT_RENDERING") == "hardware" ? null :
            new Services.WarpProgramCache(System.IO.Path.Combine(Core.AppPaths.DataDirectory, "graphics", "warp-programs-v1.bin"));
        ShaderCache = shaderCache;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>This copy, the running one; App shows the window when the app is started again.</summary>
    internal static Services.SingleInstance? Instance { get; private set; }

    internal static Services.WarpProgramCache? ShaderCache { get; private set; }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(CreateGraphicsOptions())
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    // Start on WARP and prepare hardware in the background. Fixed modes remain available for startup comparisons.
    private static Win32PlatformOptions CreateGraphicsOptions()
    {
        string? mode = Environment.GetEnvironmentVariable("UNVAULT_RENDERING");
        var options = new Win32PlatformOptions { UseDeferredHardwareRendering = mode is not ("warp" or "hardware"), WarpProgramCache = ShaderCache };
        if (mode == "warp")
        {
            options.RenderingMode = [Win32RenderingMode.AngleEgl];
            options.GraphicsAdapterSelectionCallback = adapters =>
            {
                for (int i = 0; i < adapters.Count; i++)
                    if (string.Equals(adapters[i].Description, "Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase))
                        return i;
                throw new InvalidOperationException("The WARP experiment requires the Microsoft Basic Render Driver adapter.");
            };
        }
        return options;
    }
}
