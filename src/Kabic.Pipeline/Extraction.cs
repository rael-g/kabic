using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Kabic;
using Kabic.Reader;

namespace Kabic.Pipeline;

/// <summary>
/// Extracts the public API of C headers by running zig cc in AST-dump mode.
/// </summary>
public static class Extraction
{
    /// <summary>
    /// The inputs of one extraction: its headers, auxiliary and compose headers, include
    /// directories, zig override and convention.
    /// </summary>
    public sealed record Request(
        IReadOnlyList<string> Headers,
        IReadOnlyList<string> IncludeDirs,
        IReadOnlyList<string> AuxHeaders,
        string? Zig,
        Convention Convention)
    {
        /// <summary>The annotation file merged into what the headers declare, when there is one.</summary>
        public string? Annotations { get; init; }
    }

    /// <summary>
    /// Extracts the API of the request and returns it as indented JSON. Throws
    /// <c>InvalidOperationException</c> when zig produces no AST, a header fails to parse, or
    /// the extractor reports errors.
    /// </summary>
    public static string Run(Request request)
    {
        var zig = ResolveZig(request.Zig);
        var headerPaths = request.Headers.Select(Path.GetFullPath).ToList();
        var auxPaths = request.AuxHeaders.Select(Path.GetFullPath).ToList();
        var all = headerPaths.Concat(auxPaths).ToList();

        var tuDir = Path.Combine(Path.GetTempPath(), "kabic_extract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tuDir);
        var tuPath = Path.Combine(tuDir, "tu.c");
        var includes = all.Select(h => $"#include \"{h.Replace('\\', '/')}\"").ToList();
        File.WriteAllLines(tuPath, includes);

        JsonObject ast;
        ConstantReader.Typed typed;
        Dictionary<string, string> values = [];
        try
        {
            var written = ConstantReader.Candidates(headerPaths);
            File.WriteAllLines(tuPath, includes.Concat(written.Select(c => ConstantReader.ExpansionProbe(c.Name))));
            var candidates = ConstantReader.Valued(written, Run(zig, tuPath, request.IncludeDirs, ["-E", "-P"]));
            ast = JsonNode.Parse(Probed(tuPath, includes, ref candidates, c => ConstantReader.TypeProbe(c.Name),
                () => RunAstDump(zig, tuPath, request.IncludeDirs)))!.AsObject();
            typed = ConstantReader.ReadTypes(ast, candidates);
            if (typed.Integers.Count > 0)
            {
                var integers = typed.Integers;
                values = ConstantReader.ReadValues(JsonNode.Parse(Probed(tuPath, includes, ref integers, i => ConstantReader.ValueProbe(i.Name, i.Underlying),
                    () => RunAstDump(zig, tuPath, request.IncludeDirs, "-std=c23")))!.AsObject());
            }
        }
        finally
        {
            Directory.Delete(tuDir, recursive: true);
        }

        var sourceBytes = all.Distinct().ToDictionary(Path.GetFullPath, File.ReadAllBytes);
        var (api, errors) = Extractor.Extract(ast, headerPaths.ToHashSet(), sourceBytes, auxPaths.ToHashSet(), request.Convention.IncludeDirectoryName, request.Convention.SymbolPrefix);
        errors.AddRange(typed.Errors);
        var byName = typed.Read.ToDictionary(c => c.Name);
        foreach (var i in typed.Integers)
        {
            if (values.TryGetValue(i.Name, out var v)) byName[i.Name] = new ApiConstant(i.Name, i.Spelled, v, null, i.Doc);
        }
        api.Constants.AddRange(typed.Order.Where(byName.ContainsKey).Select(n => byName[n]));
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join('\n', errors.Select(e => $"ERROR: {e}")));

        if (request.Annotations is { } annotations) Annotations.Apply(api, annotations);
        return api.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Returns the zig executable: the override when given, else the first zig on the PATH,
    /// else the fallback under the home directory. Throws <c>InvalidOperationException</c> when
    /// none is found.
    /// </summary>
    public static string ResolveZig(string? overridePath)
    {
        if (overridePath is not null) return overridePath;
        var fromPath = FindOnPath("zig");
        if (fromPath is not null) return fromPath;
        var fallback = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "zig-x86_64-linux-0.16.0", "zig");
        if (File.Exists(fallback)) return fallback;
        throw new InvalidOperationException("zig not found on PATH; pass --zig <path>");
    }

    static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Runs a dump of a translation unit holding one probe per item, dropping each item whose probe
    /// does not compile: a macro that does not expand to a value is simply no constant.
    /// </summary>
    static string Probed<T>(string tuPath, IReadOnlyList<string> includes, ref List<T> items, Func<T, string> probe, Func<string> dump)
    {
        while (true)
        {
            File.WriteAllLines(tuPath, includes.Concat(items.Select(probe)));
            try { return dump(); }
            catch (InvalidOperationException e)
            {
                var failed = System.Text.RegularExpressions.Regex.Matches(e.Message, @"tu\.c:(\d+):\d+: error")
                    .Select(m => int.Parse(m.Groups[1].Value) - includes.Count - 1).Where(i => i >= 0).ToHashSet();
                if (failed.Count == 0) throw;
                items = items.Where((_, i) => !failed.Contains(i)).ToList();
            }
        }
    }

    static string RunAstDump(string zig, string tuPath, IReadOnlyList<string> includeDirs, params string[] dialect)
    {
        var stdout = Run(zig, tuPath, includeDirs, [.. dialect, "-Xclang", "-ast-dump=json", "-fsyntax-only", "-fparse-all-comments"]);
        if (stdout.Length < 2 || stdout[0] != '{')
            throw new InvalidOperationException("zig cc produced no AST");
        return stdout;
    }

    static string Run(string zig, string tuPath, IReadOnlyList<string> includeDirs, IReadOnlyList<string> mode)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(zig) { RedirectStandardOutput = true, RedirectStandardError = true },
        };
        process.StartInfo.Environment["ZIG_LOCAL_CACHE_DIR"] = Path.Combine(Path.GetDirectoryName(tuPath)!, "zig-cache");
        foreach (var a in mode.Prepend("cc").Append(tuPath)) process.StartInfo.ArgumentList.Add(a);
        foreach (var d in includeDirs) { process.StartInfo.ArgumentList.Add("-I"); process.StartInfo.ArgumentList.Add(d); }
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = stderrTask.Result;
        process.WaitForExit();

        var realErrors = stderr.Split('\n')
            .Where(l => l.Contains("error:") && !l.Contains($"{Path.GetFileName(tuPath)}:1:1: error: FileNotFound"))
            .ToList();
        if (realErrors.Count > 0)
            throw new InvalidOperationException("header failed to parse:\n" + string.Join('\n', realErrors));

        return stdout;
    }
}
