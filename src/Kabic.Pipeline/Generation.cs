using System.Text.Json.Nodes;

using Kabic.CSharp;

namespace Kabic.Pipeline;

/// <summary>
/// Writes the C# projection and the error-kind mapping of an API
/// description.
/// </summary>
public static class Generation
{
    /// <summary>
    /// The inputs of one C# projection: API JSON, namespaces, directories, domain, library,
    /// providers,
    /// callbacks, usings and convention.
    /// </summary>
    public sealed record CSharpRequest(
        string ApiJson,
        string Namespace,
        string NativeNamespace,
        string OutDir,
        string ContractDir,
        string? Domain,
        string? Library,
        IReadOnlyCollection<string> Providers,
        IReadOnlyCollection<string> Callbacks,
        IReadOnlyList<string> Usings,
        Convention Convention);

    /// <summary>
    /// Writes the C# projection of one API description into the output and contract
    /// directories. Throws <c>InvalidOperationException</c> when free functions exist and no library
    /// is given.
    /// </summary>
    public static void CSharp(CSharpRequest r)
    {
        var model = ApiReader.Read(JsonNode.Parse(r.ApiJson)!.AsObject());
        var convention = r.Convention;
        var classified = Classifier.Classify(model, new HashSet<string>(r.Providers), new HashSet<string>(r.Callbacks), convention);
        var ns = r.Namespace;
        var nativeNs = r.NativeNamespace;
        var outDir = r.OutDir;
        var contractDir = r.ContractDir;
        var usings = r.Usings.ToList();

        Directory.CreateDirectory(outDir);

        foreach (var value in model.Structs.Where(CSharpWriter.IsValue).Where(s => s.Fields.Any(f => f.Has("default"))))
            File.WriteAllText(Path.Combine(outDir, $"{Idioms.TypeName(value.Name, convention)}.Default.g.cs"),
                CSharpWriter.RenderDefault(model, value, nativeNs, convention));

        foreach (var borrowed in model.Structs.Where(CSharpWriter.IsBorrowed))
        {
            Directory.CreateDirectory(contractDir);
            File.WriteAllText(Path.Combine(contractDir, $"{Idioms.TypeName(borrowed.Name, convention)}.g.cs"),
                CSharpWriter.RenderBorrowed(model, borrowed, ns, usings, convention));
        }

        foreach (var view in model.Structs.Where(CSharpWriter.IsView))
        {
            Directory.CreateDirectory(contractDir);
            File.WriteAllText(Path.Combine(contractDir, $"{Idioms.TypeName(view.Name, convention)}.g.cs"),
                CSharpWriter.RenderView(model, view, ns, usings, convention));
        }

        foreach (var counted in model.Structs.Where(s => !s.External && !s.IsVtable
            && !convention.IsParamsType(s.Name)
            && !CSharpWriter.IsView(s)
            && !CSharpWriter.IsBorrowed(s)
            && CSharpWriter.SpannedFields(model, s, convention).Any()))
            File.WriteAllText(Path.Combine(outDir, $"{counted.Name}.Spans.g.cs"),
                CSharpWriter.RenderStructSpans(model, counted, nativeNs, convention));

        foreach (var provider in classified.Providers)
        {
            var typeName = Idioms.TypeName(provider.Name, convention);
            var source = CSharpWriter.RenderProvider(model, provider, classified, ns, nativeNs, usings, convention);
            File.WriteAllText(Path.Combine(outDir, $"{typeName}.g.cs"), source.Class);
            if (source.Contract is not null)
            {
                Directory.CreateDirectory(contractDir);
                File.WriteAllText(Path.Combine(contractDir, $"I{typeName}.g.cs"), source.Contract);
            }
        }

        foreach (var callback in classified.Callbacks)
            File.WriteAllText(Path.Combine(outDir, $"{Idioms.TypeName(callback.Name, convention)}Native.g.cs"),
                CSharpWriter.RenderCallbackInterface(callback, ns, nativeNs, convention));

        foreach (var (owner, fns) in classified.FreeFunctionGroups)
        {
            if (r.Library is null)
                throw new InvalidOperationException($"domain has free functions on '{owner}' but no library was given");
            File.WriteAllText(Path.Combine(outDir, $"{Idioms.TypeName(owner, convention)}Functions.g.cs"),
                CSharpWriter.RenderFreeFunctions(model, owner, fns, ns, nativeNs, usings, r.Library, convention));
        }
    }

    /// <summary>
    /// Returns the source of the mapping from each result code the convention names to its
    /// exception; a code it does not name reads as the general one.
    /// </summary>
    public static string ResultCodes(Convention convention)
    {
        var dot = convention.ResultHelperClass.LastIndexOf('.');
        var (ns, cls) = (convention.ResultHelperClass[..dot], convention.ResultHelperClass[(dot + 1)..]);
        var general = convention.ResultCodes.FirstOrDefault(r => r.Code.EndsWith("_GENERAL", StringComparison.Ordinal))
            ?? new ResultCodeSpec("", "InvalidOperationException", null);
        var o = new List<string>
        {
            "// <auto-generated/>",
            "// Derived from the result codes of api_domains.json. Do not edit.",
            "",
            "#nullable enable",
            "",
            $"namespace {ns};",
            "",
            "/// <summary>",
            "/// Turns the result code a native call returned into the exception that stands for it. The detail",
            "/// of a failure is in the failing library's log, never in the code, so the message names the call",
            "/// and the code only.",
            "/// </summary>",
            $"public static class {cls}",
            "{",
            $"    /// <summary>Throws the exception for <paramref name=\"code\"/> when it is not <c>{convention.ResultOk}</c>.</summary>",
            "    /// <param name=\"code\">What the call returned.</param>",
            "    /// <param name=\"context\">The name of the call, prefixed to the message.</param>",
            $"    public static void ThrowIfFailed({convention.ResultManagedType} code, string context)",
            "    {",
            $"        if (code != {convention.ResultManagedType}.{convention.ResultOk})",
            "            throw FromCode(code, context);",
            "    }",
            "",
            $"    /// <summary>The exception that stands for a code other than <c>{convention.ResultOk}</c>.</summary>",
            "    /// <param name=\"code\">What the call returned.</param>",
            "    /// <param name=\"context\">The name of the call, prefixed to the message.</param>",
            $"    public static Exception FromCode({convention.ResultManagedType} code, string context) =>",
            "        Create(code, $\"{context} failed with {Name(code)}; the library's log holds the detail\");",
            "",
            $"    static string Name({convention.ResultManagedType} code) =>",
            "        Enum.IsDefined(code) ? code.ToString() : $\"result code 0x{(uint)code:x8}\";",
            "",
            $"    static Exception Create({convention.ResultManagedType} code, string message) => code switch",
            "    {",
        };
        foreach (var r in convention.ResultCodes.Where(r => r != general))
            o.Add($"        {convention.ResultManagedType}.{r.Code} => new {r.Managed}(message),");
        o.Add($"        _ => new {general.Managed}(message),");
        o.Add("    };");
        o.Add("");
        o.Add("    /// <summary>");
        o.Add("    /// The code that stands for <paramref name=\"exception\"/>: the first code whose exception");
        o.Add("    /// type it is exactly, or the general code.");
        o.Add("    /// </summary>");
        o.Add("    /// <param name=\"exception\">What a managed handler threw.</param>");
        o.Add($"    public static {convention.ResultManagedType} ToCode(Exception exception) => exception.GetType().Name switch");
        o.Add("    {");
        foreach (var r in convention.ResultCodes.Where(r => r != general && r.Managed != general.Managed)
                     .GroupBy(r => r.Managed).Select(g => g.First()))
            o.Add($"        \"{r.Managed}\" => {convention.ResultManagedType}.{r.Code},");
        o.Add($"        _ => {convention.ResultManagedType}.{general.Code},");
        o.Add("    };");
        o.Add("}");
        foreach (var r in convention.ResultCodes.Where(r => r.Extends is not null))
        {
            o.Add("");
            o.Add($"/// <summary>The exception for the result code <c>{r.Code}</c>.</summary>");
            o.Add($"public class {r.Managed} : {r.Extends}");
            o.Add("{");
            o.Add("    /// <summary>Creates the exception for a result code.</summary>");
            o.Add($"    public {r.Managed}(string message) : base(message)");
            o.Add("    {");
            o.Add("    }");
            o.Add("}");
        }
        return string.Join('\n', o) + "\n";
    }
}
