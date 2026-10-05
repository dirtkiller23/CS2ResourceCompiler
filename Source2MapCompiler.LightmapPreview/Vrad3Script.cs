using System.IO;
using System.Linq;

namespace Source2MapCompiler.LightmapPreview;

// script-gpu.vrad3 is the list of commands resourcecompiler writes for vrad3. We only need lightmap_type, lightmap_image_gpu
// with the width, height and block size, the lightmap_compute_block_gpu lines, one per block, and the run compute_lpv_<id>
// lines, one per light probe volume, in the order vrad3 bakes them after the lightmap
internal static class Vrad3ScriptReader
{
    private const string ProbeVolumeCommand = "compute_lpv_";

    public readonly record struct Vrad3Script(string Type, int Width, int Height, int BlockSize, int BlockCount, string[] ProbeVolumes)
    {
        // This is the irradiance whichever lightmap type it is
        public string ProcessedFileName => Type == "sh2" ? "sh2_dc.exr" : "irradiance.exr";
    }

    public const string FileName = "script-gpu.vrad3";

    public static Vrad3Script? TryRead(string path)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return null;
        }

        var type = "ahd";
        int width = 0, height = 0, blockSize = 0, blocks = 0;
        var probeVolumes = new List<string>();
        var ended = false;

        foreach (var line in lines)
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            switch (words.FirstOrDefault())
            {
                case "lightmap_type" when words.Length > 1:
                    type = words[1];
                    break;
                case "lightmap_image_gpu" when words.Length > 3 && int.TryParse(words[1], out var w) && int.TryParse(words[2], out var h) && int.TryParse(words[3], out var b):
                    width = w;
                    height = h;
                    blockSize = b;
                    break;
                case "lightmap_compute_block_gpu":
                    blocks++;
                    break;
                case "run" when words.Length > 1 && words[1].StartsWith(ProbeVolumeCommand, StringComparison.Ordinal):
                    probeVolumes.Add(words[1][ProbeVolumeCommand.Length..]);
                    break;
                case "exit":
                    ended = true;
                    break;
            }
        }

        // The script ends with exit, so if that's missing it's still being written
        if (!ended || width <= 0 || height <= 0 || blockSize <= 0 || blocks == 0)
        {
            return null;
        }

        return new Vrad3Script(type, width, height, blockSize, blocks, [.. probeVolumes]);
    }
}
