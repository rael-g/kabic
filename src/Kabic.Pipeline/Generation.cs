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
        {
            var moved = r.ContractDir != r.OutDir && value.Fields.All(f => !CTypes.IsPointer(f.Type) && f.Inline is null);
            Directory.CreateDirectory(moved ? contractDir : outDir);
            File.WriteAllText(Path.Combine(moved ? contractDir : outDir, $"{Idioms.TypeName(value.Name, convention)}.Default.g.cs"),
                CSharpWriter.RenderDefault(model, value, moved ? ns : nativeNs, convention));
        }

        foreach (var borrowed in model.Structs.Where(CSharpWriter.IsBorrowed))
        {
            Directory.CreateDirectory(contractDir);
            File.WriteAllText(Path.Combine(contractDir, $"{Idioms.TypeName(borrowed.Name, convention)}.g.cs"),
                CSharpWriter.RenderBorrowed(model, borrowed, ns, [.. usings, nativeNs], convention));
        }

        foreach (var view in model.Structs.Where(CSharpWriter.IsView))
        {
            Directory.CreateDirectory(contractDir);
            File.WriteAllText(Path.Combine(contractDir, $"{Idioms.TypeName(view.Name, convention)}.g.cs"),
                CSharpWriter.RenderView(model, view, ns, [.. usings, nativeNs], convention));
        }

        foreach (var counted in model.Structs.Where(s => !s.External && !s.IsVtable
            && !s.Has("bag")
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
                File.WriteAllText(Path.Combine(contractDir, $"I{typeName}.g.cs"), contractDir != outDir ? source.Contract.Replace($"using {nativeNs};\n", "") : source.Contract);
            }
        }

        foreach (var callback in classified.Callbacks)
            File.WriteAllText(Path.Combine(outDir, $"{Idioms.TypeName(callback.Name, convention)}Native.g.cs"),
                CSharpWriter.RenderCallbackInterface(callback, ns, nativeNs, convention));

        if (model.Opaques.Count > 0 || model.TypeAliases.Keys.Any(a => model.HandleOf(a) is not null))
            File.WriteAllText(Path.Combine(outDir, "Handles.g.cs"), CSharpWriter.RenderHandles(model, ns, convention));

        if (classified.Functions.Count > 0)
        {
            var domain = r.Domain ?? "functions";
            File.WriteAllText(Path.Combine(outDir, $"{Idioms.TypeName(domain, convention)}.Functions.g.cs"),
                CSharpWriter.RenderFunctions(model, classified, domain, ns, nativeNs, usings, convention));
        }
    }
}
