namespace Kabic.Pipeline;

/// <summary>
/// The committed paths the artifacts of one domain are written to.
/// </summary>
public sealed record DomainOutput(
    string ApiJson,
    string OutDir,
    string ContractDir);

/// <summary>
/// Plans, extracts and generates the artifacts of each domain in the manifest.
/// </summary>
public static class Regeneration
{
    /// <summary>
    /// Returns the committed paths of a domain, each passed through the placement function. The
    /// contract directory is the output directory when the domain has no abstractions
    /// directory.
    /// </summary>
    public static DomainOutput Plan(DomainSpec spec, Func<string, string> place)
    {
        var outDir = place(spec.OutDir);
        return new DomainOutput(
            place(spec.ApiJson),
            outDir,
            spec.AbstractionsOutDir is null ? outDir : place(spec.AbstractionsOutDir));
    }

    /// <summary>
    /// Returns the extraction request of a domain, with its header, include and auxiliary paths
    /// resolved under the root directory.
    /// </summary>
    public static Extraction.Request RequestFor(DomainSpec spec, string rootDir, string? zig, Convention convention)
    {
        List<string> Rooted(IEnumerable<string> paths) => paths.Select(p => Path.Combine(rootDir, p)).ToList();
        return new Extraction.Request(Rooted(spec.Headers), Rooted(spec.IncludeDirs),
            Rooted(spec.AuxHeaders), zig, convention)
        {
            Annotations = spec.Annotations is { } annotations ? Path.Combine(rootDir, annotations) : null,
        };
    }

    /// <summary>
    /// Extracts the headers of one domain and returns their API as JSON.
    /// </summary>
    public static string ExtractOne(DomainSpec spec, string rootDir, string? zig, Convention convention) =>
        Extraction.Run(RequestFor(spec, rootDir, zig, convention));

    /// <summary>
    /// Generates the C# projection of one domain from its API JSON.
    /// </summary>
    public static void GenerateOne(DomainSpec spec, string apiJson, DomainOutput output, Convention convention)
    {
        Generation.CSharp(new Generation.CSharpRequest(
            apiJson, spec.Namespace, spec.NativeNamespace, output.OutDir, output.ContractDir,
            spec.Name, spec.Library, spec.Providers, [], spec.Usings, convention));

    }

    /// <summary>
    /// Extracts every domain in parallel and returns each API JSON keyed by domain name. Throws
    /// <c>InvalidOperationException</c> listing every domain whose extraction failed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractAll(
        IReadOnlyList<DomainSpec> specs, string rootDir, string? zig, Convention convention)
    {
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var failures = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        Parallel.ForEach(specs, spec =>
        {
            try { results[spec.Name] = ExtractOne(spec, rootDir, zig, convention); }
            catch (Exception e) { failures[spec.Name] = e.Message; }
        });
        if (!failures.IsEmpty)
            throw new InvalidOperationException(string.Join('\n',
                specs.Where(s => failures.ContainsKey(s.Name)).Select(s => $"{s.Name}: {failures[s.Name]}")));
        return results;
    }
}
