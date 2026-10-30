namespace Kabic.Tests;

public sealed class CTypeTests
{
    [Theory]
    [InlineData("const struct ke_x *", "ke_x", true, 1)]
    [InlineData("ke_x", "ke_x", false, 0)]
    [InlineData("void **", "void", false, 2)]
    [InlineData("const char *", "char", true, 1)]
    [InlineData("unsigned int", "unsigned int", false, 0)]
    [InlineData("ke_x *const", "ke_x", false, 1)]
    public void ReadsADeclaredType(string text, string name, bool isConst, int pointers)
    {
        var type = CType.Of(text);
        Assert.Equal(name, type.Name);
        Assert.Equal(isConst, type.Const);
        Assert.Equal(pointers, type.Pointers);
    }

    [Fact]
    public void ReadsArrayExtentsOutermostFirst() => Assert.Equal([1, 2], CType.Of("const float[1][2]").Extents);
}
