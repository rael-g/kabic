using Kabic.Reader;

namespace Kabic.Tests;

public sealed class AnnotationsTests
{
    static ApiModel Model()
    {
        var model = new ApiModel();
        model.Functions.Add(new ApiFunction("open", "int", null, null, [new ApiParam("out_db", "void **", [], null)]));
        return model;
    }

    static void Apply(ApiModel model, string yaml)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);
        try { Annotations.Apply(model, path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TagsJoinTheParameterAndTheReturn()
    {
        var model = Model();
        Apply(model, "open:\n  return: result\n  out_db: out\n");
        Assert.True(model.Functions[0].Params[0].Has("out"));
        Assert.Contains("result", model.Functions[0].ReturnTags);
    }

    [Theory]
    [InlineData("gone:\n  x: out\n", "gone")]
    [InlineData("open:\n  gone: out\n", "open.gone")]
    public void AKeyNamingNothingFails(string yaml, string key)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Apply(Model(), yaml));
        Assert.Contains(key, failure.Message);
    }
}
