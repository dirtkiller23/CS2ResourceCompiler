using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace CS2MapCompiler;

// Colours the runs of the compile log. The log only grows at its end, so its spans stay in order and the first one on a
// line can be found by bisecting
internal sealed class LogColorizer(List<LogSpan> spans, Func<LogSpan, IBrush?> brush) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        int low = 0, high = spans.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (spans[middle].End <= line.Offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var i = low; i < spans.Count && spans[i].Offset < line.EndOffset; i++)
        {
            if (brush(spans[i]) is { } foreground)
            {
                ChangeLinePart(Math.Max(spans[i].Offset, line.Offset), Math.Min(spans[i].End, line.EndOffset), element => element.TextRunProperties.SetForegroundBrush(foreground));
            }
        }
    }
}
