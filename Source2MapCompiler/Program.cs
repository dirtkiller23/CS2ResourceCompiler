using System.Globalization;
using Avalonia;

namespace Source2MapCompiler;

public static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            // Skia only keeps about 28 MB of textures on the GPU by default, and an 8192x8192 lightmap preview is 256 MB plus its
            // mipmaps, so without this it gets uploaded again on every frame and panning and zooming crawl
            .With(new SkiaOptions { MaxGpuResourceSizeBytes = 512L * 1024 * 1024 })
            .LogToTrace();
    }
}
