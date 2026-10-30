using YamlDotNet.Serialization;

namespace Kabic.Reader;

/// <summary>
/// Tags kept beside a header that cannot be edited: a YAML map from a symbol to its members and
/// their tags, comma separated. A symbol is a function, a callback or <c>struct.slot</c>; a member
/// is a parameter, a field or <c>return</c>. The tags join the ones the header declares, so nothing
/// after the reader knows where a tag came from.
/// </summary>
public static class Annotations
{
    /// <summary>
    /// Merges the file at <paramref name="path"/> into <paramref name="model"/>. Fails naming every
    /// key that matches no declaration, so an annotation cannot go stale silently.
    /// </summary>
    public static void Apply(ApiModel model, string path)
    {
        var file = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path)) ?? [];
        var stale = new List<string>();
        foreach (var (symbol, members) in file)
        {
            if (!ApplyTo(model, symbol, members, stale)) stale.Add(symbol);
        }
        if (stale.Count > 0)
            throw new InvalidOperationException($"{path} names what no header declares: {string.Join(", ", stale)}");
    }

    static bool ApplyTo(ApiModel model, string symbol, Dictionary<string, string> members, List<string> stale)
    {
        var at = model.Functions.FindIndex(f => f.Name == symbol);
        if (at >= 0)
        {
            var f = model.Functions[at];
            model.Functions[at] = f with
            {
                Returns = Tags(members, "return").Contains("result") ? Convention.TaggedResult : f.Returns,
                ReturnTags = [.. f.ReturnTags, .. Tags(members, "return")],
                Params = Merged(symbol, f.Params, members, stale),
            };
            return true;
        }
        at = model.Callbacks.FindIndex(c => c.Name == symbol);
        if (at >= 0)
        {
            model.Callbacks[at] = model.Callbacks[at] with { Lanes = Merged(symbol, model.Callbacks[at].Lanes, members, stale) };
            return true;
        }
        var parts = symbol.Split('.');
        at = model.Structs.FindIndex(s => s.Name == parts[0]);
        if (at < 0) return false;
        var record = model.Structs[at];
        if (parts.Length == 1)
        {
            foreach (var member in members.Keys.Where(k => k != "self" && record.Fields.All(f => f.Name != k))) stale.Add($"{symbol}.{member}");
            model.Structs[at] = record with
            {
                Tags = [.. record.Tags, .. Tags(members, "self")],
                Fields = record.Fields.Select(f => f with { Tags = [.. f.Tags, .. Tags(members, f.Name)] }).ToList(),
            };
            return true;
        }
        var slot = record.Slots.FirstOrDefault(s => s.Name == parts[1]);
        if (slot is null) return false;
        model.Structs[at] = record with
        {
            Slots = record.Slots.Select(s => s != slot ? s : s with
            {
                Returns = Tags(members, "return").Contains("result") ? Convention.TaggedResult : s.Returns,
                ReturnTags = [.. s.ReturnTags, .. Tags(members, "return")],
                Params = Merged(symbol, s.Params, members, stale),
            }).ToList(),
        };
        return true;
    }

    static List<ApiParam> Merged(string symbol, IReadOnlyList<ApiParam> parameters, Dictionary<string, string> members, List<string> stale)
    {
        foreach (var member in members.Keys.Where(k => k != "return" && parameters.All(p => p.Name != k))) stale.Add($"{symbol}.{member}");
        return parameters.Select(p => p with { Tags = [.. p.Tags, .. Tags(members, p.Name ?? "")] }).ToList();
    }

    static IEnumerable<string> Tags(Dictionary<string, string> members, string member) =>
        members.TryGetValue(member, out var tags) ? tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
}
