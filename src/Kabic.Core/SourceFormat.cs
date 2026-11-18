namespace Kabic;

/// <summary>Formats the source of one backend's language.</summary>
public interface ISourceFormatter
{
    /// <summary>The extension of the files this formatter takes, with its dot.</summary>
    string Extension { get; }

    /// <summary>Returns <paramref name="text"/> formatted.</summary>
    string Format(string text);
}

/// <summary>Applies a backend's formatter to what kabic wrote.</summary>
public static class SourceFormat
{
    /// <summary>Formats in place every file of the formatter's extension under <paramref name="directory"/>.</summary>
    public static void Directory(ISourceFormatter formatter, string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return;
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*" + formatter.Extension, SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var formatted = formatter.Format(text);
            if (formatted != text) File.WriteAllText(file, formatted);
        }
    }
}
