
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kabic.Reader;

/// <summary>
/// Reads the doc comment the AST carries for a declaration and splits it into the summary, the
/// documentation of each named parameter and the return documentation, each with its leading tags.
/// </summary>
public static class DocParser
{
    static readonly Regex TagBlock = new(@"^\s*\[([^\]]+)\]\s*");

    /// <summary>
    /// Parses the doc comment of <paramref name="node"/>. A null node, or one with no full
    /// comment, yields an empty summary, no parameter documentation and no return documentation.
    /// </summary>
    public static (List<string> SummaryTags, string Summary, Dictionary<string, (List<string> Tags, string Doc)> Params,
                   List<string> ReturnTags, string ReturnDoc)
        Parse(JsonObject? node)
    {
        var summary = new List<string>();
        var paramChunks = new Dictionary<string, List<string>>();
        var returnChunks = new List<string>();

        void Walk(JsonObject n, List<string> sink)
        {
            var kind = n["kind"]?.GetValue<string>();
            if (kind == "ParamCommandComment")
            {
                var pname = n["param"]?.GetValue<string>() ?? "";
                sink = paramChunks.TryGetValue(pname, out var existing) ? existing
                     : paramChunks[pname] = [];
            }
            else if (kind == "BlockCommandComment" && n["name"]?.GetValue<string>() == "return")
            {
                sink = returnChunks;
            }
            else if (kind == "TextComment")
            {
                sink.Add(n["text"]?.GetValue<string>() ?? "");
            }
            else if (kind == "InlineCommandComment" && n["args"] is JsonArray words)
            {
                foreach (var w in words) sink.Add(w!.GetValue<string>());
            }
            if (n["inner"] is JsonArray inner)
                foreach (var c in inner) Walk(c!.AsObject(), sink);
        }

        var full = node?["inner"]?.AsArray().FirstOrDefault(c => c!["kind"]?.GetValue<string>() == "FullComment");
        if (full is not null) Walk(full.AsObject(), summary);

        var pmap = paramChunks.ToDictionary(kv => kv.Key, kv => SplitTags(kv.Value));
        var (summaryTags, summaryText) = SplitTags(summary);
        var (returnTags, returnText) = SplitTags(returnChunks);
        return (summaryTags, summaryText, pmap, returnTags, returnText);
    }

    /// <summary>
    /// The tags and text of the <c>///</c> block written directly above the declaration that starts
    /// at <paramref name="offset"/> in <paramref name="source"/>. Clang attaches only one comment to a
    /// declaration, so a field documented both above and with a trailing <c>///&lt;</c> keeps the
    /// block above through this.
    /// </summary>
    public static (List<string> Tags, string Doc) LeadingBlock(byte[] source, int offset)
    {
        var text = System.Text.Encoding.UTF8.GetString(source, 0, Math.Min(offset, source.Length));
        var lines = text.Split('\n');
        var block = new List<string>();
        for (var i = lines.Length - 2; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith("///", StringComparison.Ordinal) || t.StartsWith("///<", StringComparison.Ordinal)) break;
            block.Insert(0, t[3..]);
        }
        return SplitTags(block);
    }

    static (List<string> Tags, string Doc) SplitTags(List<string> chunks)
    {
        var raw = string.Join(' ', chunks).Trim();
        var tags = new List<string>();
        while (TagBlock.Match(raw) is { Success: true } m)
        {
            tags.AddRange(m.Groups[1].Value.Split(',').Select(t => t.Trim()));
            raw = TagBlock.Replace(raw, "", 1);
        }
        return (tags, string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }
}
