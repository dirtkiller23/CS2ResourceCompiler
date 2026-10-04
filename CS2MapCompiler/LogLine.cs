using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace CS2MapCompiler;

// Who wrote a run of the log, which picks how it's coloured
public enum LogKind
{
    // resourcecompiler, in the colour it gave
    Compiler,

    // the app's own messages, a compile starting and completing
    App,

    // the app reporting a compile cancelled or failing to start
    Error,
}

// A coloured run of the log's text
public readonly record struct LogSpan(int Offset, int Length, LogKind Kind, Color Color = default)
{
    public int End => Offset + Length;
}

// A line of the compile log, with its coloured runs placed from the line's start
public sealed partial record LogLine(string Text, IReadOnlyList<LogSpan> Spans)
{
    public static LogLine App(string text, LogKind kind)
    {
        return new LogLine(text, text.Length == 0 ? [] : [new LogSpan(0, text.Length, kind)]);
    }

    // resourcecompiler run with -html wraps coloured runs in <font color="#RRGGBB"> and escapes all its text as HTML
    // entities, the way Hammer shows it. The lines it splits with <br/> are split before they get here
    public static LogLine FromHtml(string html)
    {
        var text = new StringBuilder();
        var spans = new List<LogSpan>();
        Color? color = null;
        var start = 0;

        foreach (Match tag in Tag().Matches(html))
        {
            Add(html[start..tag.Index]);
            color = tag.Groups[1].Success ? Color.Parse("#" + tag.Groups[1].Value) : null;
            start = tag.Index + tag.Length;
        }

        Add(html[start..]);

        return new LogLine(text.ToString(), spans);

        void Add(string escaped)
        {
            var run = WebUtility.HtmlDecode(escaped);

            if (color is { } c && run.Length > 0)
            {
                spans.Add(new LogSpan(text.Length, run.Length, LogKind.Compiler, c));
            }

            text.Append(run);
        }
    }

    // any tag, with the colour of a font tag's run, other tags end a run
    [GeneratedRegex("""<font color="#([0-9A-Fa-f]{6})">|<[^>]*>""")]
    private static partial Regex Tag();
}
