using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TinyEXR;
using TinyEXR.IO;

namespace CS2MapCompiler.LightmapPreview;

public enum LightmapPreviewStage
{
    Waiting, // the script has been read and the blocks are laid out
    Baking,
    BlockDone,
    Processed, // showing vrad3's filtered, dilated and welded lightmap
}

public sealed record LightmapPreviewUpdate(LightmapAtlas Atlas, bool IsNewAtlas, PixelRegion Pixels, int BlockIndex, LightmapPreviewStage Stage);

// Follows a compile's lightmap bake by polling the addon's _vrad3 folder and vrad3 itself. resourcecompiler writes
// script-gpu.vrad3 there first, which gives the lightmap size and how many blocks there are. While vrad3 bakes, each block is
// read out of its memory as soon as it moves on to the next one. fireflies.exr is the first file vrad3 writes after the last
// block, so that's how we know the bake is over, and then irradiance.exr (sh2_dc.exr for SH2 maps) replaces the raw blocks
// with the filtered lightmap. If reading vrad3 fails, the grid and the filtered lightmap still show up. Events come in on the
// monitor's thread
public sealed class LightmapPreviewMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    // vrad3 writes this right after the last block
    private const string BakedFileName = "fireflies.exr";

    // How many rows of the filtered lightmap to read at a time
    private const int ProcessedRows = 64;

    private readonly int compilerProcessId;
    private readonly string vrad3Folder;
    private readonly Thread thread;
    private readonly ManualResetEventSlim stopping = new();
    private readonly HashSet<int> ignored = [];

    // resourcecompiler clears out _vrad3 before vrad3 starts, but until then the folder still has the last compile's files,
    // so anything older than this gets ignored
    private DateTime startedUtc;

    private Vrad3ScriptReader.Vrad3Script? script;
    private Vrad3Reader? reader;
    private Process? vrad3;

    private ushort[] red = [], green = [], blue = [];
    private bool newAtlas;

    private int current = -1;
    private bool baked;
    private bool processed;

    // vrad3 writes the filtered lightmap all at once, so it's only read after its size and time stay the same for two polls
    private (long Length, DateTime Time) processedFile;

    private bool anyRead;

    public LightmapPreviewMonitor(int compilerProcessId, string vrad3Folder)
    {
        this.compilerProcessId = compilerProcessId;
        this.vrad3Folder = vrad3Folder;
        thread = new Thread(Run) { IsBackground = true, Name = "Lightmap preview" };
    }

    public event EventHandler<string>? Message;

    public event EventHandler<LightmapPreviewUpdate>? Updated;

    public LightmapAtlas? Atlas { get; private set; }

    public void Start()
    {
        startedUtc = DateTime.UtcNow;
        thread.Start();
    }

    private void Run()
    {
        while (!stopping.IsSet)
        {
            try
            {
                if (script == null)
                {
                    TryReadScript();
                }
                else if (!processed)
                {
                    if (!baked)
                    {
                        ReadBlocks();
                    }

                    if (!baked && IsFromThisCompile(BakedFileName))
                    {
                        FinishBlocks();
                    }

                    TryReadProcessed();
                }
            }
            catch (Exception exception)
            {
                Message?.Invoke(this, $"Lightmap preview stopped: {exception.Message}");
                processed = true;
                Close();
            }

            stopping.Wait(PollInterval);
        }

        Close();
    }

    private bool IsFromThisCompile(string name)
    {
        var file = new FileInfo(Path.Combine(vrad3Folder, name));
        return file.Exists && file.LastWriteTimeUtc >= startedUtc;
    }

    private void TryReadScript()
    {
        if (!IsFromThisCompile(Vrad3ScriptReader.FileName) || Vrad3ScriptReader.TryRead(Path.Combine(vrad3Folder, Vrad3ScriptReader.FileName)) is not { } read)
        {
            return;
        }

        script = read;
        var atlas = new LightmapAtlas(read.Width, read.Height, read.BlockSize);
        Atlas = atlas;
        red = new ushort[read.BlockSize * read.BlockSize];
        green = new ushort[red.Length];
        blue = new ushort[red.Length];
        newAtlas = true;
        Raise(atlas, default, -1, LightmapPreviewStage.Waiting);
    }

    // If this fails it only stops the live blocks, the rest of the preview keeps going
    private void ReadBlocks()
    {
        try
        {
            if (reader == null)
            {
                TryOpen();
                return;
            }

            if (vrad3!.HasExited)
            {
                Close();
                return;
            }

            if (TryReadLightmap() is not { } lightmap)
            {
                return;
            }

            var index = lightmap.BlockIndex;

            if ((uint)index >= (uint)Atlas!.BlockCount || index == current)
            {
                return;
            }

            // vrad3 bakes blocks in order, so everything before the current block is finished
            ReadBlocksBefore(lightmap, index);
            current = index;
            Atlas.SetBaking(index);
            Raise(Atlas, default, index, LightmapPreviewStage.Baking);
        }
        catch (Exception exception)
        {
            StopReading(exception);
        }
    }

    // The last block has no block after it, so it gets read once the bake is over
    private void FinishBlocks()
    {
        baked = true;

        try
        {
            if (reader != null && TryReadLightmap() is { } lightmap)
            {
                ReadBlocksBefore(lightmap, Atlas!.BlockCount);
            }
        }
        catch (Exception exception)
        {
            StopReading(exception);
        }

        Close();
    }

    private Vrad3Lightmap? TryReadLightmap()
    {
        if (reader!.TryRead() is not { } lightmap)
        {
            return null;
        }

        var expected = script!.Value;

        if (lightmap.Width != expected.Width || lightmap.Height != expected.Height)
        {
            throw new NotSupportedException($"vrad3's lightmap is {lightmap.Width}x{lightmap.Height}, not the {expected.Width}x{expected.Height} its script says.");
        }

        return lightmap;
    }

    private void ReadBlocksBefore(in Vrad3Lightmap lightmap, int end)
    {
        var atlas = Atlas!;

        for (var index = 0; index < end; index++)
        {
            if (atlas.GetBlockState(index) == LightmapBlockState.Done)
            {
                continue;
            }

            var block = atlas.BlockRegion(index);
            reader!.ReadBlock(lightmap, block, red, green, blue);
            atlas.IngestRows(block, red, green, blue);
            atlas.SetDone(index);
            anyRead = true;
            Raise(atlas, block, index, LightmapPreviewStage.BlockDone);
        }
    }

    private void StopReading(Exception exception)
    {
        // Reads start failing once vrad3 frees the lightmap or exits, which is normal and not worth reporting
        if (!anyRead && vrad3 is { HasExited: false })
        {
            Message?.Invoke(this, $"Lightmap preview can not show the blocks as they bake: {exception.Message}");
        }

        if (vrad3 != null)
        {
            ignored.Add(vrad3.Id);
        }

        Close();
    }

    // vrad3 is found by name, then checked against this compile through its parent processes
    private void TryOpen()
    {
        foreach (var process in Process.GetProcessesByName("vrad3"))
        {
            if (reader != null || ignored.Contains(process.Id))
            {
                process.Dispose();
                continue;
            }

            if (!ProcessTree.IsDescendantOf(process.Id, compilerProcessId))
            {
                // Belongs to another compile
                ignored.Add(process.Id);
                process.Dispose();
                continue;
            }

            try
            {
                reader = Vrad3Reader.TryOpen(process);
            }
            catch (Exception exception)
            {
                ignored.Add(process.Id);
                Message?.Invoke(this, $"Lightmap preview can not show the blocks as they bake: {exception.Message}");
            }

            if (reader != null)
            {
                vrad3 = process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private void Close()
    {
        reader?.Dispose();
        reader = null;
        vrad3?.Dispose();
        vrad3 = null;
    }

    private void TryReadProcessed()
    {
        var file = new FileInfo(Path.Combine(vrad3Folder, script!.Value.ProcessedFileName));

        if (!file.Exists || file.LastWriteTimeUtc < startedUtc)
        {
            return;
        }

        var seen = processedFile;
        processedFile = (file.Length, file.LastWriteTimeUtc);

        if (seen != processedFile)
        {
            return;
        }

        FileStream stream;

        try
        {
            stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException)
        {
            // vrad3 is still writing it
            return;
        }

        using (stream)
        using (var exr = ExrReader.OpenSource(new StreamDataSource(stream, true), null!))
        {
            Check(exr.ParseHeader(), "read");

            var header = exr.GetHeader(0);
            var window = header.DataWindow;
            var atlas = Atlas!;

            if (window.Width != atlas.Width || window.Height != atlas.Height)
            {
                throw new InvalidDataException($"{file.Name} is {window.Width}x{window.Height}, not the {atlas.Width}x{atlas.Height} its script says.");
            }

            foreach (var name in new[] { "R", "G", "B" })
            {
                if (!header.Channels.Any(c => c.Name == name && c.PixelType == PixelType.Half))
                {
                    throw new InvalidDataException($"{file.Name} has no half float {name} channel.");
                }
            }

            for (var y = 0; y < atlas.Height; y += ProcessedRows)
            {
                var rows = Math.Min(ProcessedRows, atlas.Height - y);
                var read = exr.ReadScanlines(0, window.MinY + y, rows);
                Check(read.Operation, "read");
                var level = read.Value!.Levels[0];

                atlas.IngestRows(
                    new PixelRegion(0, y, atlas.Width, rows),
                    MemoryMarshal.Cast<byte, ushort>(level.GetChannel("R").Data),
                    MemoryMarshal.Cast<byte, ushort>(level.GetChannel("G").Data),
                    MemoryMarshal.Cast<byte, ushort>(level.GetChannel("B").Data));
            }

            processed = true;
            atlas.SetDone();
            Raise(atlas, new PixelRegion(0, 0, atlas.Width, atlas.Height), -1, LightmapPreviewStage.Processed);
        }

        void Check(ReaderResult result, string what)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidDataException($"{file.Name} could not be {what}: {result.Error?.Message ?? result.Status.ToString()}");
            }
        }
    }

    private void Raise(LightmapAtlas atlas, PixelRegion pixels, int index, LightmapPreviewStage stage)
    {
        var isNew = newAtlas;
        newAtlas = false;
        Updated?.Invoke(this, new LightmapPreviewUpdate(atlas, isNew, pixels, index, stage));
    }

    public void Dispose()
    {
        stopping.Set();

        if (thread.IsAlive)
        {
            thread.Join();
        }

        // In case the monitor was never started
        Close();
        stopping.Dispose();
    }
}
