using System.Text.Json.Nodes;

namespace Kabic.Conventions;

/// <summary>Loads a manifest's convention.</summary>
public static class ConventionLoader
{
    /// <summary>
    /// Reads the <c>convention</c> object of the manifest at <paramref name="manifestPath"/>.
    /// </summary>
    public static Convention Load(string manifestPath)
    {
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject()["convention"]?.AsObject()
            ?? throw new InvalidOperationException($"{manifestPath} has no 'convention' object");
        var convention = Convention.FromJson(json);
        var dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        convention.FormatConfigs = (json["format"]?.AsObject() ?? []).ToDictionary(
            f => f.Key, f => File.ReadAllText(Path.Combine(dir, f.Value!.GetValue<string>())));
        return convention;
    }
}
