using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;
using Source2MapCompiler.LightmapPreview;

namespace Source2MapCompiler;

[SupportedOSPlatform("windows")]
// Keeps one preview window across compiles. Every compile that bakes on the GPU gets a fresh monitor, and the window opens
// on the first update and gets reused after that
internal sealed class LightmapPreviewController(Window owner, Action<string> log) : IDisposable
{
    private LightmapPreviewMonitor? monitor;
    private LightmapPreviewWindow? window;

    // If the window is closed during a compile it stays closed until the next one
    private bool closedDuringCompile;

    private LightmapPreviewStage? stage;

    public void Start(int compilerProcessId, string vrad3Folder)
    {
        Stop();

        closedDuringCompile = false;
        stage = null;

        var current = new LightmapPreviewMonitor(compilerProcessId, vrad3Folder);
        current.Message += (_, message) => log(message);
        // Updates that arrive after the compile ended, or from an older compile's monitor, get dropped
        current.Updated += (_, update) => Dispatcher.UIThread.Post(() =>
        {
            if (monitor == current)
            {
                OnUpdated(update);
            }
        });

        monitor = current;
        current.Start();
    }

    public void Stop()
    {
        if (monitor == null)
        {
            return;
        }

        monitor.Dispose();
        monitor = null;

        if (stage == LightmapPreviewStage.ProbeVolume)
        {
            window?.ShowEnded("The compile has ended. This is the last light probe volume vrad3 baked.");
        }
        else if (stage == LightmapPreviewStage.Processed)
        {
            window?.ShowEnded("The compile has ended. This is the lightmap vrad3 filtered, before it is compressed.");
        }
        else if (stage != null)
        {
            window?.ShowEnded("The compile has ended. This is the lightmap as vrad3 last baked it, before it was filtered and compressed.");
        }
    }

    private void OnUpdated(LightmapPreviewUpdate update)
    {
        if (closedDuringCompile)
        {
            return;
        }

        if (window == null)
        {
            window = new LightmapPreviewWindow();
            window.Closed += (_, _) =>
            {
                window = null;
                closedDuringCompile = monitor != null;
            };
            window.Show(owner);
            log("Showing the lightmap as vrad3 bakes it.");
        }

        stage = update.Stage;
        window.ShowUpdate(update);
    }

    public void Dispose()
    {
        Stop();
    }
}
