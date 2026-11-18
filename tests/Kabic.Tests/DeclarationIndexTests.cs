using Kabic.Bindings;

namespace Kabic.Tests;

public class DeclarationIndexTests
{
    static readonly (string, string) Forward = ("a/forward.h", "typedef struct ke_thing ke_thing;\n");
    static readonly (string, string) Definition = ("b/thing.h", "typedef struct ke_thing\n{\n    int x;\n} ke_thing;\n");

    [Fact]
    public void A_definition_wins_over_a_forward_declaration_in_a_header_that_sorts_first()
    {
        Assert.Equal("b/thing.h", BindingsPlanner.DeclaredIn("ke_", [Forward, Definition])["ke_thing"]);
    }

    [Fact]
    public void The_index_does_not_depend_on_the_order_the_headers_arrive_in()
    {
        var one = BindingsPlanner.DeclaredIn("ke_", [Forward, Definition, ("c/other.h", "typedef struct ke_other ke_other;\n")]);
        var two = BindingsPlanner.DeclaredIn("ke_", [("c/other.h", "typedef struct ke_other ke_other;\n"), Definition, Forward]);
        Assert.Equal(one.OrderBy(x => x.Key), two.OrderBy(x => x.Key));
    }
}
