using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using Iced.Intel;

namespace Source2MapCompiler.LightmapPreview;

// TexelInfoFlags is the offset of the baked flag byte inside a texel info entry
internal sealed record Vrad3Addresses(ulong Image, ulong BlockIndex, ulong TexelInfo, int TexelInfoFlags);

// Finds where vrad3 keeps its lightmap by decoding its block bake function, which is the only code that uses the
// "Compute block %d/%d on GPU" message. Three globals are picked out by what the instructions do rather than their exact
// bytes, so this keeps working when vrad3 gets rebuilt. The block index is loaded right before the message is printed, the
// lightmap image is where the width and height come from when the blocks are counted, and the texel info array is where
// each texel gets marked as baked after the readback
internal static class Vrad3Locator
{
    private const string BakeMessage = "Compute block %d/%d on GPU";

    public static Vrad3Addresses Locate(byte[] image, ulong moduleBase)
    {
        var message = FindUnique(image, Encoding.ASCII.GetBytes(BakeMessage), "the block bake message");
        var messageUse = FindUniqueRipLea(image, message);
        var function = FindFunction(image, messageUse);
        var code = Decode(image, moduleBase, function.Begin, function.End);
        var at = code.FindIndex(i => i.IP == moduleBase + (ulong)messageUse);

        if (at < 0)
        {
            throw new NotSupportedException("The block bake in this vrad3.dll could not be decoded.");
        }

        // The block index gets loaded right before the message is printed
        var blockIndex = LastRipLoad(code, at, 6, _ => true) ?? throw new NotSupportedException("The block index is not where it was expected in this vrad3.dll.");

        // To count the blocks, vrad3 divides the width and then the height by the block size
        var divisions = code.Take(at).Select((i, n) => (i, n)).Where(x => x.i.Mnemonic == Mnemonic.Idiv && x.i.Op0Kind == OpKind.Register).Take(2).ToArray();

        if (divisions.Length < 2)
        {
            throw new NotSupportedException("The block count is not worked out as expected in this vrad3.dll.");
        }

        var width = LastRipLoad(code, divisions[0].n, 8, i => i.Op0Register == Register.EAX);
        var height = LastRipLoad(code, divisions[1].n, 8, i => i.Op0Register == Register.EAX);

        if (width == null || height != width + 4)
        {
            throw new NotSupportedException("The lightmap size is not where it was expected in this vrad3.dll.");
        }

        // After the readback, each texel that was written gets marked as baked
        var mark = code.Skip(at).Select((i, n) => (i, n: n + at)).FirstOrDefault(x =>
            x.i.Mnemonic == Mnemonic.Or && x.i.Op0Kind == OpKind.Memory && x.i.MemorySize == MemorySize.UInt8 &&
            x.i.Op1Kind == OpKind.Immediate8 && x.i.Immediate8 == 1 && x.i.MemoryBase != Register.None && !x.i.IsIPRelativeMemoryOperand);

        if (mark.i.Mnemonic != Mnemonic.Or)
        {
            throw new NotSupportedException("The texels are not marked as baked as expected in this vrad3.dll.");
        }

        var infoRegister = mark.i.MemoryBase.GetFullRegister();
        var texelInfo = LastRipLoad(code, mark.n, 12, i => i.Op0Register.GetFullRegister() == infoRegister);

        if (texelInfo == null)
        {
            throw new NotSupportedException("The texel information is not where it was expected in this vrad3.dll.");
        }

        return new Vrad3Addresses(width.Value, blockIndex, texelInfo.Value, (int)mark.i.MemoryDisplacement64);
    }

    // Finds the nearest mov reg, [rip + address] before an instruction and returns the address
    private static ulong? LastRipLoad(List<Instruction> code, int before, int within, Func<Instruction, bool> register)
    {
        for (var n = before - 1; n >= Math.Max(0, before - within); n--)
        {
            var i = code[n];

            if (i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register && i.Op1Kind == OpKind.Memory && i.IsIPRelativeMemoryOperand && register(i))
            {
                return i.IPRelativeMemoryAddress;
            }
        }

        return null;
    }

    private static List<Instruction> Decode(byte[] image, ulong moduleBase, int begin, int end)
    {
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image, begin, end - begin), moduleBase + (ulong)begin);
        var code = new List<Instruction>();

        while (decoder.IP < moduleBase + (ulong)end)
        {
            code.Add(decoder.Decode());
        }

        return code;
    }

    // Gets the function's bounds from the x64 unwind table
    private static (int Begin, int End) FindFunction(byte[] image, int rva)
    {
        var directory = new PEHeaders(new MemoryStream(image), image.Length, isLoadedImage: true).PEHeader!.ExceptionTableDirectory;
        var table = directory.RelativeVirtualAddress;
        var size = directory.Size;

        var count = size / 12;
        int Begin(int n) => BitConverter.ToInt32(image, table + n * 12);
        int End(int n) => BitConverter.ToInt32(image, table + n * 12 + 4);

        for (var n = 0; n < count; n++)
        {
            if (rva < Begin(n) || rva >= End(n))
            {
                continue;
            }

            // The compiler sometimes splits a function into parts, and each part has its own entry right after the last
            var first = n;
            var last = n;

            while (first > 0 && End(first - 1) == Begin(first))
            {
                first--;
            }

            while (last + 1 < count && Begin(last + 1) == End(last))
            {
                last++;
            }

            return (Begin(first), End(last));
        }

        throw new NotSupportedException("The block bake in this vrad3.dll is not in its unwind table.");
    }

    private static int FindUnique(byte[] image, byte[] value, string what)
    {
        var found = image.AsSpan().IndexOf(value);

        if (found < 0)
        {
            throw new NotSupportedException($"{what} is not in this vrad3.dll.");
        }

        if (image.AsSpan(found + 1).IndexOf(value) >= 0)
        {
            throw new NotSupportedException($"{what} is in more than one place in this vrad3.dll.");
        }

        return found;
    }

    private static int FindUniqueRipLea(byte[] image, int target)
    {
        var found = -1;

        for (var i = 0; i + 7 <= image.Length; i++)
        {
            // REX.W, with or without REX.R, followed by lea with a RIP relative operand
            if ((image[i] == 0x48 || image[i] == 0x4C) && image[i + 1] == 0x8D && (image[i + 2] & 0xC7) == 0x05 && i + 7 + BitConverter.ToInt32(image, i + 3) == target)
            {
                if (found >= 0)
                {
                    throw new NotSupportedException("The block bake message is used in more than one place in this vrad3.dll.");
                }

                found = i;
            }
        }

        if (found < 0)
        {
            throw new NotSupportedException("The block bake message is not used in this vrad3.dll.");
        }

        return found;
    }
}
