using System.Text.Json.Nodes;

namespace Kabic.Conventions;

/// <summary>Loads a manifest's convention.</summary>
public static class ConventionLoader
{
    /// <summary>
    /// Reads the <c>convention</c> object of the manifest at <paramref name="manifestPath"/>.
    /// </summary>
    public static Convention Load(string manifestPath) =>
        Convention.FromJson(JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject()["convention"]?.AsObject()
            ?? throw new InvalidOperationException($"{manifestPath} has no 'convention' object"));
}
