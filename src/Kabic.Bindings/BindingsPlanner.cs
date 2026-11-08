using Kabic.CSharp;
using Kabic.Pipeline;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Kabic.Conventions;

namespace Kabic.Bindings;

/// <summary>
/// Derives, from the <c>bindings</c> array of the manifest and the headers it names, the ClangSharp
/// job of every binding assembly.
/// </summary>

public static class BindingsPlanner
{
    /// <summary>A variable a header defines as static: it has no symbol, so no binding can reach it.</summary>
    static readonly Regex StaticVariable = new(@"^\s*static\s+const\s+[\w\s\*]+?\b(\w+)\s*(?:\[[^\]]*\])?\s*=", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// The header each type is declared in, keyed by name. A definition wins over a forward
    /// declaration, and among headers of the same kind the first in ordinal path order wins, so
    /// the answer does not depend on the order <paramref name="headers"/> arrive in.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DeclaredIn(string symbolPrefix, IReadOnlyList<(string Path, string Text)> headers)
    {
        var symbol = Regex.Escape(symbolPrefix) + @"\w+";
        Regex[] definitions =
        [
            new($@"^\s*\}}\s*({symbol})\s*;", RegexOptions.Multiline),
            new($@"typedef\s+(?:struct|enum|union)\s+\w*\s*\{{[^{{}}]*\}}\s*({symbol})\s*;"),
        ];
        var opaque = new Regex($@"^\s*typedef\s+(?:struct|enum|union)\s+({symbol})\s+\1\s*;", RegexOptions.Multiline);
        var ordered = headers.OrderBy(h => h.Path, StringComparer.Ordinal).ToList();
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in ordered)
            foreach (var rx in definitions)
                foreach (Match m in rx.Matches(text))
                    found.TryAdd(m.Groups[1].Value, path);
        foreach (var (path, text) in ordered)
            foreach (Match m in opaque.Matches(text))
                found.TryAdd(m.Groups[1].Value, path);
        return found;
    }

    static readonly Regex Include = new(@"#\s*include\s*<([^>]+)>", RegexOptions.Compiled);
    static readonly Regex ProjectReference = new(@"ProjectReference\s+Include=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Plans the job of every binding in the manifest at <paramref name="manifestPath"/>, with
    /// <paramref name="rootDir"/> as the repository root. Throws
    /// <see cref="InvalidOperationException"/> when the manifest has no bindings array, or when a
    /// header belongs to more than one binding.
    /// </summary>
    public static IReadOnlyList<BindingJob> Plan(string rootDir, string manifestPath)
    {
        var convention = ConventionLoader.Load(manifestPath);
        var domains = DomainSpec.Load(manifestPath);
        var symbol = Regex.Escape(convention.SymbolPrefix) + @"\w+";
        var typeName = new Regex($@"\b{symbol}\b", RegexOptions.Compiled);
        var alias = new Regex($@"^\s*typedef\s+[\w \*]+?\s+({symbol})\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
        var functionPointer = new Regex($@"typedef\s+[^;(]*\(\s*\*\s*({symbol})\s*\)\s*\(([^;]*)\)\s*;", RegexOptions.Compiled);
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var bindings = manifest["bindings"]?.AsArray()
            ?? throw new InvalidOperationException("the manifest has no 'bindings' array");

        var includeRoots = Directory.EnumerateDirectories(Path.Combine(rootDir, "src"), convention.IncludeDirectoryName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(p => p is not null && !p.Contains(".zig-cache"))
            .Select(p => p!)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        string Repo(string abs) => Path.GetRelativePath(rootDir, abs).Replace('\\', '/');

        string? Resolve(string included)
        {
            foreach (var root in includeRoots)
            {
                var candidate = Path.Combine(root, included.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        HashSet<string> Closure(IEnumerable<string> seeds)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(seeds);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (!seen.Add(h) || !File.Exists(h)) continue;
                foreach (Match m in Include.Matches(File.ReadAllText(h)))
                    if (Resolve(m.Groups[1].Value) is { } target) queue.Enqueue(target);
            }
            return seen;
        }

        var declaredIn = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var sourceHeaders = Directory.EnumerateFiles(Path.Combine(rootDir, "src"), "*.h", SearchOption.AllDirectories)
            .Where(h => !h.Contains(".zig-cache") && h.Replace('\\', '/').Contains($"/{convention.IncludeDirectoryName}/"))
            .Order(StringComparer.Ordinal)
            .ToList();
        var sourceTexts = sourceHeaders.ToDictionary(h => h, File.ReadAllText);
        foreach (var (name, header) in DeclaredIn(convention.SymbolPrefix, sourceHeaders.Select(h => (h, sourceTexts[h])).ToList()))
            declaredIn[name] = header;
        foreach (var header in sourceHeaders)
            foreach (Match m in alias.Matches(sourceTexts[header]))
                if (!declaredIn.ContainsKey(m.Groups[1].Value)) aliases.Add(m.Groups[1].Value);
        var functionPointerParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in sourceHeaders)
            foreach (Match m in functionPointer.Matches(sourceTexts[header]))
                functionPointerParameters.TryAdd(m.Groups[1].Value, m.Groups[2].Value);

        var ownHeaders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerBinding = new Dictionary<string, string>(StringComparer.Ordinal);
        var claimedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var contested = new List<string>();
        foreach (var b in bindings)
        {
            var name = b!["name"]!.GetValue<string>();
            var hs = b["headers"]!.AsArray().Select(x => Path.Combine(rootDir, x!.GetValue<string>())).ToList();
            ownHeaders[name] = hs;
            foreach (var h in hs)
            {
                if (claimedBy.TryGetValue(h, out var first)) contested.Add($"  {Repo(h)} is claimed by {first} and {name}");
                else claimedBy[h] = name;
            }
            foreach (var (type, header) in declaredIn)
                if (hs.Contains(header))
                {
                    owner[type] = b["namespace"]!.GetValue<string>();
                    ownerBinding[type] = name;
                }
        }

        if (contested.Count > 0)
            throw new InvalidOperationException(
                $"{contested.Count} header(s) belong to more than one binding:\n{string.Join('\n', contested.Order())}\n\n"
                + "A type has one owning assembly. Leave the header with the binding that owns it;\n"
                + "the other reaches its types through a remap, and references that project.");

        HashSet<string> Reachable(string projectDir)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            foreach (var csproj in Directory.EnumerateFiles(Path.Combine(rootDir, projectDir), "*.csproj")) queue.Enqueue(csproj);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                if (!seen.Add(Path.GetFileNameWithoutExtension(f)) || !File.Exists(f)) continue;
                foreach (Match m in ProjectReference.Matches(File.ReadAllText(f)))
                {
                    var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
                    if (File.Exists(target)) queue.Enqueue(target);
                }
            }
            return seen;
        }

        var assembliesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var b in bindings)
        {
            var bindingNs = b!["namespace"]!.GetValue<string>();
            if (!assembliesOf.TryGetValue(bindingNs, out var assemblies)) assembliesOf[bindingNs] = assemblies = [];
            assemblies.Add(Path.GetFileName(b["project"]!.GetValue<string>()));
        }

        string AngleName(string abs)
        {
            foreach (var root in includeRoots)
                if (abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return Path.GetRelativePath(root, abs).Replace('\\', '/');
            return Path.GetFileName(abs);
        }

        var jobs = new List<BindingJob>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var b in bindings)
        {
            var name = b!["name"]!.GetValue<string>();
            var project = b["project"]!.GetValue<string>();
            var ns = b["namespace"]!.GetValue<string>();
            var library = b["library"]?.GetValue<string>();
            var extra = (b["additional"]?.AsArray() ?? []).Select(x => x!.GetValue<string>()).ToList();
            var macros = b["macroBindings"]?.GetValue<bool>() ?? false;
            var headers = ownHeaders[name];

            var nativeDir = Path.Combine(rootDir, project, "Native");
            var firstOfProject = seenProjects.Add(project);
            string Rel(string abs) => Path.GetRelativePath(nativeDir, abs).Replace('\\', '/');

            var dirs = Closure(headers)
                .Select(h => includeRoots.FirstOrDefault(r => h.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                .Where(r => r is not null).Select(r => r!)
                .Distinct().OrderBy(Repo, StringComparer.Ordinal).ToList();

            var reach = Reachable(project);

            var traversed = new List<string>(headers);
            var remaps = new List<string>();
            var excludes = new List<string>();
            for (var grew = true; grew;)
            {
                grew = false;
                var declaredHere = declaredIn.Where(kv => traversed.Contains(kv.Value)).Select(kv => kv.Key)
                                             .ToHashSet(StringComparer.Ordinal);
                var referenced = traversed.SelectMany(h => typeName.Matches(File.ReadAllText(h)).Select(m => m.Value))
                                          .ToHashSet(StringComparer.Ordinal);
                var pending = new Queue<string>(referenced.Where(functionPointerParameters.ContainsKey));
                while (pending.TryDequeue(out var pointer))
                    foreach (Match m in typeName.Matches(functionPointerParameters[pointer]))
                        if (referenced.Add(m.Value) && functionPointerParameters.ContainsKey(m.Value))
                            pending.Enqueue(m.Value);
                remaps.Clear();
                excludes.Clear();
                foreach (var type in referenced.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (aliases.Contains(type)) continue;
                    var mine = declaredHere.Contains(type);
                    if (owner.TryGetValue(type, out var o)
                        && assembliesOf.TryGetValue(o, out var assembliesOfOwner) && assembliesOfOwner.Any(reach.Contains))
                    {
                        if (o == ns)
                        {
                            if (ownerBinding[type] != name) excludes.Add(type);
                            continue;
                        }
                        remaps.Add($"{type}={o}.{type}");
                        excludes.Add(type);
                        continue;
                    }
                    if (mine) continue;
                    if (declaredIn.TryGetValue(type, out var source) && !traversed.Contains(source))
                    {
                        traversed.Add(source);
                        grew = true;
                    }
                }
            }

            foreach (var h in traversed)
                foreach (Match m in StaticVariable.Matches(File.ReadAllText(h)))
                    excludes.Add(m.Groups[1].Value);

            string file;
            string? umbrellaBody = null;
            if (headers.Count == 1)
            {
                file = Rel(headers[0]);
            }
            else
            {
                var body = new StringBuilder("#pragma once\n");
                foreach (var h in headers)
                    body.Append("#include <").Append(AngleName(h)).Append(">\n");
                umbrellaBody = body.ToString();
                file = name + ".umbrella.h";
            }

            var config = new List<string> { "multi-file", "latest-codegen" };
            if (firstOfProject) config.Add("generate-helper-types");
            config.Add("generate-file-scoped-namespaces");
            config.Add("exclude-funcs-with-body");
            config.Add("generate-disable-runtime-marshalling");
            if (macros) config.Add("generate-macro-bindings");

            jobs.Add(new BindingJob(
                name, nativeDir, b["output"]!.GetValue<string>(), ns, library,
                dirs.Select(Rel).ToList(), file, umbrellaBody,
                traversed.OrderBy(Repo, StringComparer.Ordinal).Select(Rel).ToList(),
                extra, [.. remaps, "__va_list_tag=void"], excludes.OrderBy(x => x, StringComparer.Ordinal).ToList(), config,
                convention.SymbolPrefix, convention.CommonBindingsNamespace, convention.BindingsMethodsClass));
        }

        var idiomatic = jobs.ToDictionary(j => j, j => Idiomatic(j, convention, domains).ToList());
        var ownedIds = idiomatic.Values.SelectMany(r => r).Where(r => r.Contains("=global::")).Distinct()
            .ToDictionary(r => r.Split('=')[0], r => r);
        foreach (var remaps in idiomatic.Values)
        {
            var foreignIds = remaps.Where(r => r.StartsWith('?')).Select(r => r[1..]).ToList();
            remaps.RemoveAll(r => r.StartsWith('?'));
            remaps.AddRange(foreignIds.Where(ownedIds.ContainsKey).Select(id => ownedIds[id]));
        }
        var renamed = idiomatic.Values.SelectMany(r => r).Select(r => r.Split('=', 2))
            .Where(r => !r[0].Contains("::") && !r[1].Contains('.')).DistinctBy(r => r[0]).ToDictionary(r => r[0], r => r[1]);
        var contracts = jobs.Select(j => (Job: j, Spec: ContractSpec(j)))
            .Where(x => x.Spec is not null).Select(x => (x.Job, Spec: x.Spec!, Types: AbstractTypes(x.Job, convention))).Where(x => x.Types.Count > 0).ToList();
        var contractOf = contracts.SelectMany(c => c.Types.Select(t => (t, c.Spec.Namespace))).DistinctBy(x => x.t).ToDictionary(x => x.t, x => x.Item2);
        string Located(string remap)
        {
            var source = remap.Split('=', 2)[0].TrimEnd();
            return contractOf.TryGetValue(source, out var ns) ? $"{source}=global::{ns}.{renamed.GetValueOrDefault(source, source)}" : remap;
        }
        var result = jobs.Select(j => j with
        {
            Remaps = [.. j.Remaps.Select(r => Located(Renamed(r, renamed))), .. idiomatic[j].Select(Located),
                .. contractOf.Where(c => idiomatic[j].Any(r => r.StartsWith(c.Key + "="))).Select(c => $"{c.Key}=global::{c.Value}.{renamed.GetValueOrDefault(c.Key, c.Key)}")],
            Excludes = [.. j.Excludes.Union(contractOf.Keys).Union((convention.TypeMap.GetValueOrDefault(Languages.CSharp) ?? new Dictionary<string, string>()).Keys)],
        }).ToList();
        foreach (var (job, spec, types) in contracts)
        {
            var dir = Path.GetFullPath(Path.Combine(rootDir, spec.AbstractionsOutDir!));
            Directory.CreateDirectory(dir);
            string Moved(string p) => Path.GetRelativePath(dir, Path.GetFullPath(Path.Combine(job.NativeDir, p))).Replace('\\', '/');
            result.Add(new BindingJob(job.Name + "Contract", dir, "Types/", spec.Namespace, null,
                job.IncludeDirectories.Select(Moved).ToList(), job.UmbrellaBody is null ? Moved(job.File) : job.File, job.UmbrellaBody,
                job.Traverse.Select(Moved).ToList(), job.Additional,
                [.. result.First(r => r.Name == job.Name).Remaps.Where(r => !types.Contains(r.Split('=')[0].TrimEnd()) && !r.Contains("::")),
                    .. idiomatic[job].Where(r => r.Contains("=global::") || types.Contains(r.Split("::")[0].Split('=')[0].TrimEnd()))], [],
                [.. job.Config.Where(c => c != "generate-helper-types")],
                convention.SymbolPrefix, convention.CommonBindingsNamespace, convention.BindingsMethodsClass) { Includes = types });
        }
        return result;

        DomainSpec? ContractSpec(BindingJob job) =>
            domains.FirstOrDefault(d => d.AbstractionsOutDir is not null && d.NativeNamespace == job.Namespace);
    }

    /// <summary>What a contract can hold of a job: its enums and its records with no pointer.</summary>
    static List<string> AbstractTypes(BindingJob job, Convention convention)
    {
        string Full(string p) => Path.GetFullPath(Path.Combine(job.NativeDir, p));
        var model = ApiReader.Read(JsonNode.Parse(Extraction.Run(new Extraction.Request(
            job.Traverse.Select(Full).ToList(), job.IncludeDirectories.Select(Full).ToList(), [], null, convention)))!.AsObject());
        var mapped = convention.TypeMap.GetValueOrDefault(Languages.CSharp) ?? new Dictionary<string, string>();
        var records = model.Structs.Where(CSharpWriter.IsValue).Where(s => !mapped.ContainsKey(s.Name)
            && s.Fields.All(f => !CTypes.IsPointer(f.Type) && f.Inline is null)).ToList();
        var known = records.Select(r => r.Name).ToHashSet();
        bool Holds(string type) => model.Structs.FirstOrDefault(s => s.Name == CType.Of(type).Name) is not { } held
            || known.Contains(held.Name) || mapped.ContainsKey(held.Name);
        while (records.RemoveAll(r => !r.Fields.All(f => Holds(f.Type))) > 0) known = records.Select(r => r.Name).ToHashSet();
        return [.. model.Enums.Where(e => !e.External && !mapped.ContainsKey(e.Name)).Select(e => e.Name), .. known];
    }

    static string Renamed(string remap, IReadOnlyDictionary<string, string> renamed)
    {
        var (source, target) = (remap.Split('=', 2)[0], remap.Split('=', 2)[1]);
        return renamed.TryGetValue(source, out var name) ? $"{source}={target[..(target.LastIndexOf('.') + 1)]}{name}" : remap;
    }

    static IEnumerable<string> Idiomatic(BindingJob job, Convention convention, IReadOnlyList<DomainSpec> domains)
    {
        string Full(string p) => Path.GetFullPath(Path.Combine(job.NativeDir, p));
        var model = ApiReader.Read(JsonNode.Parse(Extraction.Run(new Extraction.Request(
            job.Traverse.Select(Full).ToList(), job.IncludeDirectories.Select(Full).ToList(), [], null, convention)))!.AsObject());
        foreach (var id in model.ForeignIdTypes) yield return $"?{id}";
        var text = Regex.Replace(string.Concat(job.Traverse.Select(p => File.ReadAllText(Full(p)))), @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline);
        bool Captured(string value, ApiEnum owner) =>
            Regex.IsMatch(text, $@"[(=][ \t]*{value}\b") && model.Enums.Any(o => o != owner
                && o.Values.Any(v => Idioms.EnumMember(v.Name, o.Name) == Idioms.EnumMember(value, owner.Name)));
        foreach (var id in model.IdTypes)
            if (domains.FirstOrDefault(d => d.NativeNamespace == job.Namespace) is { } home)
                yield return $"{id}=global::{home.Namespace}.{Idioms.TypeName(id, convention)}";
        foreach (var (cType, target) in convention.TypeMap.GetValueOrDefault(Languages.CSharp) ?? new Dictionary<string, string>())
            yield return $"{cType}={target}";
        foreach (var e in model.Enums.Where(e => !e.External))
        {
            yield return $"{e.Name}={Idioms.TypeName(e.Name, convention)}";
            foreach (var v in e.Values.DistinctBy(v => Idioms.EnumMember(v.Name, e.Name)).Where(v => !Captured(v.Name, e)))
                yield return $"{e.Name}::{v.Name}={Idioms.EnumMember(v.Name, e.Name)}";
        }
        foreach (var s in model.Structs.Where(CSharpWriter.IsValue).Where(s => convention.TypeMap.GetValueOrDefault(Languages.CSharp)?.ContainsKey(s.Name) != true))
        {
            yield return $"{s.Name}={Idioms.TypeName(s.Name, convention)}";
            foreach (var f in s.Fields) yield return $"{s.Name}::{f.Name}={Idioms.Pascal(f.Name)}";
        }
    }
}
