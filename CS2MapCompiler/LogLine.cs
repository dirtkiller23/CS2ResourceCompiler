namespace CS2MapCompiler;

/// <summary>What a line of the compile log is, which picks its colour.</summary>
public enum LogKind
{
    /// <summary>What resourcecompiler prints.</summary>
    Normal,

    /// <summary>What resourcecompiler warns of.</summary>
    Warning,

    /// <summary>What resourcecompiler reports as an error, or the app when a compile is cancelled or fails to start.</summary>
    Error,

    /// <summary>The app's own messages, a compile starting and completing.</summary>
    App,
}

/// <summary>A line of the compile log.</summary>
public sealed record LogLine(string Text, LogKind Kind)
{
    public bool IsWarning => Kind == LogKind.Warning;

    public bool IsError => Kind == LogKind.Error;

    public bool IsApp => Kind == LogKind.App;

    /// <summary>The kind of a line resourcecompiler printed, by how it starts: its errors and warnings say so up front.</summary>
    public static LogKind Classify(string line)
    {
        var text = line.TrimStart();

        if (text.StartsWith("error", StringComparison.OrdinalIgnoreCase) || text.StartsWith("fatal", StringComparison.OrdinalIgnoreCase) || line.Contains(" error:", StringComparison.OrdinalIgnoreCase))
        {
            return LogKind.Error;
        }

        if (text.StartsWith("warning", StringComparison.OrdinalIgnoreCase) || line.Contains(" warning:", StringComparison.OrdinalIgnoreCase))
        {
            return LogKind.Warning;
        }

        return LogKind.Normal;
    }
}
