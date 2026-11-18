using Kabic.CSharp;

using Xunit;

namespace Kabic.Tests;

public class CSharpFormatterTests
{
    const string Config = "root = true\n[*.cs]\ndotnet_sort_system_directives_first = true\n";

    [Fact]
    public void Imports_are_ordered_and_a_member_is_indented()
    {
        var formatted = new CSharpFormatter(Config).Format("using Zeta;\nusing System;\nnamespace N;\nclass C\n{\nint x;\n}\n");
        Assert.True(formatted.IndexOf("using System;") < formatted.IndexOf("using Zeta;"));
        Assert.Contains("    int x;", formatted);
    }

    [Fact]
    public void Formatted_source_comes_back_unchanged()
    {
        var formatter = new CSharpFormatter(Config);
        var once = formatter.Format("class C\n{\nint x;\n}\n");
        Assert.Equal(once, formatter.Format(once));
    }
}
