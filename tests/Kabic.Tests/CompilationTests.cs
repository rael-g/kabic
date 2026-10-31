using Kabic.Cli;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kabic.Tests;

public sealed class CompilationTests
{
    const string ImplicitUsings =
        "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Threading; global using System.Threading.Tasks;";

    static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "kabic.slnx"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }

    static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, file);
            if (rel.Contains("Generated") || rel.Contains("/bin/") || rel.Contains("/obj/") || rel.StartsWith("build") || rel.EndsWith(".json") && rel.Contains("csharp")) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(to, rel))!);
            File.Copy(file, Path.Combine(to, rel));
        }
    }

    [Fact]
    public void TheGeneratedProjectionCompiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kabic_compile_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Copy(Path.Combine(RepositoryRoot(), "tests/e2e/sqlite"), root);
            Directory.CreateDirectory(Path.Combine(root, "src/csharp/Sqlite/Native"));
            Assert.Equal(0, KabicCli.Run(["bindings", "--root", root], []));
            Assert.Equal(0, KabicCli.Run(["generate", "--root", root], []));

            var trees = Directory.EnumerateFiles(Path.Combine(root, "src/csharp"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText).Append(ImplicitUsings)
                .Select(text => CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview)));
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("Sqlite", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(6).Select(d => d.ToString()).ToList();
            Assert.True(errors.Count == 0, string.Join('\n', errors));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
