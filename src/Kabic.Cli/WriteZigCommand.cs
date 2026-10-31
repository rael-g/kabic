using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Kabic;
using Kabic.Conventions;
using Kabic.Pipeline;
using Kabic.Zig;

namespace Kabic.Cli;

/// <summary>
/// Implements the command that writes one Zig package per domain of the manifest.
/// </summary>
internal static class WriteZigCommand
{
    const string PackagesRoot = "src/zig/abi";

    /// <summary>A domain of the manifest and where its package is written.</summary>
    sealed record Unit(DomainSpec Spec, string PackageDir)
    {
        public string Key => Spec.Name;

        public string Package => ZigPackage.DomainPackage(Spec.Name);
    }

    /// <summary>
    /// Writes every package in place, or under the directory <c>--into</c> names as if it were
    /// the repository root, and returns zero. Returns two when headers could not be extracted
    /// or laid out.
    /// </summary>
    public static int Run(Options options) => Write(options, options.Into ?? options.Root);

    /// <summary>
    /// Writes the packages into a scratch directory and compares them with the committed ones.
    /// Returns zero when they are the same, one when they differ, and two when they could not
    /// be written.
    /// </summary>
    public static int CheckDrift(Options options)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "kabic_zig_check_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Write(options, scratch) is var code and not 0) return code;
            var generated = Files(scratch, [""]);
            var committed = Files(options.Root, [PackagesRoot]);
            var drift = generated.Union(committed).Order(StringComparer.Ordinal)
                .Where(rel => !generated.Contains(rel) || !committed.Contains(rel)
                    || !File.ReadAllBytes(Path.Combine(scratch, rel)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(options.Root, rel))))
                .ToList();
            foreach (var rel in drift) Console.WriteLine($"[DRIFT] {rel}");
            if (drift.Count == 0) return 0;
            Console.WriteLine("\n[FAIL] Drift detected! Run 'kabic write zig' and commit the changes.");
            return 1;
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    static HashSet<string> Files(string root, IEnumerable<string> dirs) =>
        dirs.Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Select(f => Slashed(Path.GetRelativePath(root, f)))
            .Where(rel => !rel.Split('/').Any(part => part.StartsWith('.'))).ToHashSet();

    static string Slashed(string path) => path.Replace('\\', '/');

    static List<Unit> Units(Options options) =>
        DomainSpec.LoadForZig(options.Manifest).OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new Unit(d, Path.Combine(options.Root, PackagesRoot, d.Name))).ToList();

    static int Write(Options options, string into)
    {
        var root = options.Root;
        var convention = ConventionLoader.Load(options.Manifest);
        var units = Units(options);

        var models = new System.Collections.Concurrent.ConcurrentDictionary<string, ApiModel>();
        var failures = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        Parallel.ForEach(units, unit =>
        {
            try
            {
                var json = Regeneration.ExtractOne(unit.Spec, root, options.Zig, convention);
                models[unit.Key] = ApiReader.Read(JsonNode.Parse(json)!.AsObject());
            }
            catch (Exception e) { failures[unit.Key] = e.Message; }
        });
        if (Report(failures)) return 2;

        var owner = new Dictionary<string, string>();
        foreach (var unit in units)
        {
            var m = models[unit.Key];
            foreach (var name in m.Structs.Where(s => !s.External).Select(s => s.Name).Concat(m.Enums.Where(e => !e.External).Select(e => e.Name))
                         .Concat(m.TypeAliases.Keys))
                owner.TryAdd(name, unit.Key);
        }
        foreach (var unit in units)
            foreach (var name in models[unit.Key].Opaques)
                owner.TryAdd(name, unit.Key);
        foreach (var unit in units)
            foreach (var name in models[unit.Key].Enums.Where(e => !e.External).SelectMany(e => e.Values).Select(v => v.Name)
                         .Concat(models[unit.Key].Constants.Select(c => c.Name)))
                owner.TryAdd(name, unit.Key);
        var texts = new Dictionary<string, string>();
        var abis = new Dictionary<string, string>();
        var imports = new Dictionary<string, List<string>>();
        foreach (var unit in units)
        {
            var foreign = owner.Where(kv => kv.Value != unit.Key).ToDictionary();
            try { (texts[unit.Key], abis[unit.Key]) = ZigWriter.RenderFiles(models[unit.Key], foreign, convention, $"{unit.Key}_c.zig"); }
            catch (NotSupportedException e) { failures[unit.Key] = e.Message; continue; }
            imports[unit.Key] = foreign.Values.Distinct().Where(k => texts[unit.Key].Contains($"@import(\"{k}\")"))
                .Concat(Included(root, unit, units)).Where(k => k != unit.Key)
                .Distinct().Order(StringComparer.Ordinal).ToList();
        }
        if (Report(failures)) return 2;

        var byKey = units.ToDictionary(u => u.Key);
        foreach (var unit in units)
        {
            var dir = Path.Combine(into, Path.GetRelativePath(root, unit.PackageDir));
            var package = new ZigPackage(unit.Key, unit.Package,
                unit.Spec.Headers.Select(h => IncludeName(unit.Spec, h)).ToList(),
                imports[unit.Key].Select(k => new ZigImport(k, byKey[k].Package, Slashed(Path.GetRelativePath(unit.PackageDir, byKey[k].PackageDir)))).ToList(),
                Reached(unit.Key, imports).SelectMany(k => byKey[k].Spec.IncludeDirs).Distinct()
                    .Select(d => Slashed(Path.GetRelativePath(unit.PackageDir, Path.Combine(root, d)))).ToList());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{unit.Key}.zig"), texts[unit.Key]);
            File.WriteAllText(Path.Combine(dir, $"{unit.Key}_c.zig"), abis[unit.Key]);
            File.WriteAllText(Path.Combine(dir, "build.zig"), package.BuildZig());
            File.WriteAllText(Path.Combine(dir, "build.zig.zon"), package.BuildZon());
            File.WriteAllText(Path.Combine(dir, "layout_test.zig"), ZigPackage.LayoutTest(models[unit.Key]));
        }
        var all = Path.Combine(root, PackagesRoot);
        var gathered = units.Select(u => new ZigImport(u.Key, u.Package, Slashed(Path.GetRelativePath(all, u.PackageDir)))).ToList();
        Directory.CreateDirectory(Path.Combine(into, PackagesRoot));
        File.WriteAllText(Path.Combine(into, PackagesRoot, "build.zig"), ZigPackage.AllBuildZig(gathered));
        File.WriteAllText(Path.Combine(into, PackagesRoot, "build.zig.zon"), ZigPackage.AllBuildZon(gathered));

        Console.WriteLine($"wrote {units.Count} Zig package(s) under {into}");
        return 0;
    }

    /// <summary>The unit and every unit its imports reach, in key order.</summary>
    static List<string> Reached(string key, IReadOnlyDictionary<string, List<string>> imports)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([key]);
        while (pending.TryPop(out var k))
            if (seen.Add(k)) foreach (var i in imports[k]) pending.Push(i);
        return [.. seen];
    }

    /// <summary>A header as an <c>#include</c> reaches it through its domain's include directories.</summary>
    static string IncludeName(DomainSpec spec, string header) =>
        spec.IncludeDirs.Select(d => Slashed(Path.GetRelativePath(d, header)))
            .Where(rel => !rel.StartsWith("..", StringComparison.Ordinal)).DefaultIfEmpty(Path.GetFileName(header)).MinBy(rel => rel.Length)!;

    /// <summary>The units declaring a header the headers of <paramref name="unit"/> include.</summary>
    static IEnumerable<string> Included(string root, Unit unit, IReadOnlyList<Unit> units) =>
        unit.Spec.Headers.SelectMany(h => File.ReadLines(Path.Combine(root, h)))
            .Select(l => Regex.Match(l, @"^\s*#\s*include\s*[<""]([^>""]+)[>""]"))
            .Where(m => m.Success)
            .SelectMany(m => units.Where(u => u.Spec.Headers.Any(h => IncludeName(u.Spec, h) == m.Groups[1].Value)).Select(u => u.Key).Take(1));

    static bool Report(IReadOnlyDictionary<string, string> failures)
    {
        foreach (var (key, message) in failures.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.Error.WriteLine($"error: {key}: {message}");
        return failures.Count > 0;
    }
}
