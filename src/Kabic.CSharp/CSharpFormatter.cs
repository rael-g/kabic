using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Kabic.CSharp;

/// <summary>
/// Formats C# with Roslyn against an <c>.editorconfig</c>: the one the manifest names under
/// <c>format.csharp</c>, or the default <c>dotnet new editorconfig</c> writes.
/// </summary>
public sealed class CSharpFormatter(string editorConfig) : ISourceFormatter
{
    /// <summary>The formatter a convention selects.</summary>
    public static CSharpFormatter For(Convention convention) =>
        new(convention.FormatConfigs.GetValueOrDefault(Languages.CSharp) ?? DefaultConfig());

    static string DefaultConfig()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("default.editorconfig")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <inheritdoc/>
    public string Extension => ".cs";

    /// <inheritdoc/>
    public string Format(string text)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("format", LanguageNames.CSharp)
            .AddAnalyzerConfigDocument(".editorconfig", SourceText.From(editorConfig), filePath: "/format/.editorconfig").Project;
        var document = project.AddDocument("Source.cs", SourceText.From(text), filePath: "/format/Source.cs");
        document = Formatter.OrganizeImportsAsync(document).GetAwaiter().GetResult();
        document = Formatter.FormatAsync(document).GetAwaiter().GetResult();
        return document.GetTextAsync().GetAwaiter().GetResult().ToString();
    }
}
