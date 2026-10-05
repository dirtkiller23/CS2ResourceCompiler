using System.Threading;

namespace Source2MapCompiler.LightmapPreview;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public enum LightmapBlockState : byte
{
    Pending,
    Baking,
    Done,
}

// Holds every texel of the lightmap as half floats so the exposure can be changed later, and remembers for each block whether
// vrad3 has baked it yet so the view can draw the grid. A texel with no colour is one no chart covers. The monitor fills it on
// its own thread while the UI draws it, which is why everything locks
public sealed class LightmapAtlas
{
    private readonly ushort[] hdr;
    private readonly byte[] tonemap = new byte[65536];
    private readonly LightmapBlockState[] blocks;
    private readonly Lock sync = new();

    public LightmapAtlas(int width, int height, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);

        Width = width;
        Height = height;
        BlockSize = blockSize;
        BlocksX = (width + blockSize - 1) / blockSize;
        BlocksY = (height + blockSize - 1) / blockSize;

        blocks = new LightmapBlockState[BlocksX * BlocksY];
        hdr = new ushort[(long)width * height * 3];

        SetExposure(0);
    }

    public int Width { get; }

    public int Height { get; }

    public int BlockSize { get; }

    public int BlocksX { get; }

    public int BlocksY { get; }

    public int BlockCount => blocks.Length;

    public float Exposure { get; private set; }

    public LightmapBlockState GetBlockState(int blockX, int blockY)
    {
        return GetBlockState(blockY * BlocksX + blockX);
    }

    public LightmapBlockState GetBlockState(int index)
    {
        lock (sync)
        {
            return blocks[index];
        }
    }

    // Blocks on the right and bottom edges can be smaller than the rest
    public PixelRegion BlockRegion(int index)
    {
        var x = index % BlocksX * BlockSize;
        var y = index / BlocksX * BlockSize;
        return new PixelRegion(x, y, Math.Min(BlockSize, Width - x), Math.Min(BlockSize, Height - y));
    }

    internal void SetBaking(int index)
    {
        lock (sync)
        {
            if (blocks[index] != LightmapBlockState.Done)
            {
                blocks[index] = LightmapBlockState.Baking;
            }
        }
    }

    // Passing no index marks every block as done
    internal void SetDone(int? index = null)
    {
        lock (sync)
        {
            if (index is { } one)
            {
                blocks[one] = LightmapBlockState.Done;
            }
            else
            {
                Array.Fill(blocks, LightmapBlockState.Done);
            }
        }
    }

    // Both the live blocks and the filtered EXR come in through here, as half floats for each colour along the region's rows
    internal void IngestRows(PixelRegion texels, ReadOnlySpan<ushort> red, ReadOnlySpan<ushort> green, ReadOnlySpan<ushort> blue)
    {
        lock (sync)
        {
            for (var y = 0; y < texels.Height; y++)
            {
                var o = ((long)(texels.Y + y) * Width + texels.X) * 3;

                for (var x = 0; x < texels.Width; x++, o += 3)
                {
                    var i = y * texels.Width + x;
                    hdr[o] = red[i];
                    hdr[o + 1] = green[i];
                    hdr[o + 2] = blue[i];
                }
            }
        }
    }

    // Instead of tonemapping every texel again, the exposure goes into a lookup table with the final 8 bit value for each of the
    // 65536 possible half floats. The change shows up the next time the pixels are rendered
    public void SetExposure(float exposure)
    {
        lock (sync)
        {
            Exposure = exposure;
            var scale = MathF.Pow(2, exposure);

            for (var i = 0; i < tonemap.Length; i++)
            {
                var value = (float)BitConverter.UInt16BitsToHalf((ushort)i) * scale;
                tonemap[i] = float.IsFinite(value) && value > 0 ? (byte)Math.Clamp(LinearToSrgb(Aces(value)) * 255f + 0.5f, 0, 255) : (byte)0;
            }
        }
    }

    // Writes straight into the caller's bitmap through the lookup table
    // This runs on the UI thread with the view's bitmap locked, so it must not wait on anything. A Parallel.For here made the
    // UI thread wait for its workers, and while waiting Avalonia painted, which needs that bitmap, and the app deadlocked
    public unsafe void Render(PixelRegion region, nint destination, int stride)
    {
        lock (sync)
        {
            for (var y = region.Y; y < region.Bottom; y++)
            {
                var row = new Span<uint>((void*)(destination + y * stride), Width);

                for (var x = region.X; x < region.Right; x++)
                {
                    var o = ((long)y * Width + x) * 3;

                    // Texels that aren't baked or covered show as a dark checkerboard
                    row[x] = (hdr[o] | hdr[o + 1] | hdr[o + 2]) == 0
                        ? ((x >> 3) + (y >> 3)) % 2 == 0 ? 0xFF1C1C1Cu : 0xFF242424u
                        : 0xFF000000u | (uint)tonemap[hdr[o]] << 16 | (uint)tonemap[hdr[o + 1]] << 8 | tonemap[hdr[o + 2]];
                }
            }
        }
    }

    // Krzysztof Narkowicz's fit of the ACES filmic curve
    private static float Aces(float x)
    {
        return x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f);
    }

    private static float LinearToSrgb(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - 0.055f;
    }
}
