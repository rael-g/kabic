using System.Text.Json.Nodes;

namespace Kabic.Pipeline;

/// <summary>
/// One domain of the manifest, with its headers, its output locations and its providers.
/// </summary>
public sealed record DomainSpec(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string> IncludeDirs,
    IReadOnlyList<string> AuxHeaders,
    string ApiJson,
    string Namespace,
    string NativeNamespace,
    string OutDir,
    string? AbstractionsOutDir,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Usings,
    string? Library,
    string? Annotations = null)
{
    /// <summary>
    /// The domains of the manifest followed by its <c>zig</c> entries: headers that have a Zig
    /// package and no projection of their own, such as a library's factory header, given a name,
    /// headers, include directories and optional annotations.
    /// </summary>
    public static IReadOnlyList<DomainSpec> LoadForZig(string manifestPath)
    {
        var extra = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject()["zig"]?.AsArray() ?? [];
        return [.. Load(manifestPath), .. extra.Select(e => new DomainSpec(e!["name"]!.GetValue<string>(),
            e["headers"]!.AsArray().Select(n => n!.GetValue<string>()).ToList(),
            e["includeDirs"]!.AsArray().Select(n => n!.GetValue<string>()).ToList(),
            [], "", "", "", "", null, [], [], null, e["annotations"]?.GetValue<string>()))];
    }

    /// <summary>
    /// Reads the manifest at the given path and returns its domains in manifest order.
    /// </summary>
    public static IReadOnlyList<DomainSpec> Load(string manifestPath)
    {
        var domains = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject()["domains"]!.AsArray();
        return domains.Select(d => FromJson(d!.AsObject())).ToList();
    }

    static DomainSpec FromJson(JsonObject d)
    {
        static List<string> Strings(JsonNode? node) =>
            node?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? [];

        return new DomainSpec(
            d["name"]!.GetValue<string>(),
            Strings(d["headers"]),
            Strings(d["includeDirs"]),
            Strings(d["auxHeaders"]),
            d["apiJson"]!.GetValue<string>(),
            d["namespace"]!.GetValue<string>(),
            d["nativeNamespace"]!.GetValue<string>(),
            d["outDir"]!.GetValue<string>(),
            d["abstractionsOutDir"]?.GetValue<string>(),
            Strings(d["providers"]),
            Strings(d["usings"]),
            d["library"]?.GetValue<string>(),
            d["annotations"]?.GetValue<string>());
    }
}
