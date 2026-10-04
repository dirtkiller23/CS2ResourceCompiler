using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace CS2MapCompiler;

// Colours each line of the compile log by its kind. The log adds a kind for each line it appends, so line N's kind is kinds[N - 1]
internal sealed class LogColorizer(List<LogKind> kinds, Func<LogKind, IBrush?> brush) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.LineNumber > kinds.Count || brush(kinds[line.LineNumber - 1]) is not { } foreground)
        {
            return;
        }

        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(foreground));
    }
}
