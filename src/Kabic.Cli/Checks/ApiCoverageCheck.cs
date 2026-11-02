using System.Text.Json.Nodes;

using Kabic.Conventions;

namespace Kabic.Cli.Checks;

/// <summary>
/// Checks that every public header is described by kabic, ClangSharp or a recorded exclusion.
/// </summary>
internal static class ApiCoverageCheck
{
    /// <summary>
    /// Returns zero when every public header is covered. Returns one after listing the
    /// uncovered headers on the error stream.
    /// </summary>
    public static int Execute(Options options)
    {
        var rootDir = options.Root;
        var args = options.Raw;
        var convention = ConventionLoader.Load(options.Manifest);
        var manifest = JsonNode.Parse(File.ReadAllText(options.Manifest))!.AsObject();
        var excluded = manifest["coverageExclusions"]?.AsObject().ToDictionary(e => e.Key, e => e.Value!.GetValue<string>()) ?? [];

        var describedByKabic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in manifest["domains"]!.AsArray())
            foreach (var key in (string[])["headers", "auxHeaders", "composeHeaders"])
                foreach (var h in d![key]?.AsArray() ?? [])
                    describedByKabic.Add(Norm(h!.GetValue<string>()));

        var describedByClangSharp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in Kabic.Bindings.BindingsPlanner.Plan(rootDir, options.Manifest))
            foreach (var header in job.Traverse)
                describedByClangSharp.Add(Norm(Path.GetFullPath(Path.Combine(job.NativeDir, header))));

        var holes = new List<string>();
        foreach (var dir in (string[])["src/c", "src/zig"])
            foreach (var header in Directory.EnumerateFiles(Path.Combine(rootDir, dir), "*.h", SearchOption.AllDirectories))
            {
                if (!header.Replace('\\', '/').Contains($"/{convention.IncludeDirectoryName}/")) continue;
                if (header.Contains(".zig-cache")) continue;
                var key = Norm(header);
                if (describedByKabic.Contains(key) || describedByClangSharp.Contains(key)) continue;
                if (excluded.ContainsKey(key)) continue;
                if (Path.GetFileName(header) == "component_fields.h") continue;
                if (IsInlineOnly(header)) continue;
                holes.Add(Path.GetRelativePath(rootDir, header).Replace('\\', '/'));
            }

        if (holes.Count == 0)
        {
            Console.WriteLine("Every public header is described by kabic, by ClangSharp, or by a recorded exclusion.");
            return 0;
        }

        Console.Error.WriteLine($"{holes.Count} header(s) reach no language:");
        foreach (var h in holes.Order()) Console.Error.WriteLine($"  {h}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Add each to scripts/api_domains.json, to a binding in it, or to this script's");
        Console.Error.WriteLine("exclusion list with the reason it stays unbound.");
        return 1;

        /// <summary>
        /// True when a header declares nothing a binding could reach: every function it
        /// defines is <c>static inline</c>, so there is no symbol to import and no vtable to
        /// project. Derived rather than listed, so a new header-only helper does not have to
        /// remember to register itself as an exception.
        /// </summary>
        static bool IsInlineOnly(string header)
        {
            var declaresSomething = false;
            foreach (var raw in File.ReadAllLines(header))
            {
                var line = raw.Trim();
                if (line.StartsWith("static inline", StringComparison.Ordinal)) continue;
                if (line.StartsWith("typedef", StringComparison.Ordinal)
                    || line.StartsWith("struct ", StringComparison.Ordinal)
                    || line.StartsWith("enum ", StringComparison.Ordinal)
                    || line.Contains("_API ", StringComparison.Ordinal)
                    || line.Contains("(*", StringComparison.Ordinal))
                {
                    declaresSomething = true;
                    break;
                }
            }
            return !declaresSomething;
        }

        string Norm(string path)
        {
            var p = path.Replace('\\', '/');
            var marker = convention.IncludeDirectoryName + "/";
            var i = p.IndexOf(marker, StringComparison.Ordinal);
            return i >= 0 ? p[(i + marker.Length)..] : p;
        }
    }
}
