using Kabic.Pipeline;

namespace Kabic.Tests;

[CollectionDefinition(nameof(ExtractionCacheTests), DisableParallelization = true)]
public sealed class ExtractionCacheCollection;

[Collection(nameof(ExtractionCacheTests))]
public sealed class ExtractionCacheTests
{
    [Fact]
    public void ARepeatedExtractionIsAnsweredFromTheCacheAndAChangedIncludedHeaderIsKeptUnderANewKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "kabic_cache_test_" + Guid.NewGuid().ToString("N"));
        var include = Path.Combine(root, "include");
        var dir = Path.Combine(include, "kernel_engine", "thing");
        Directory.CreateDirectory(dir);
        var header = Path.Combine(dir, "thing.h");
        var width = Path.Combine(dir, "width.h");
        File.WriteAllText(width, "typedef int ke_width;\n");
        File.WriteAllText(header, "#include <kernel_engine/thing/width.h>\n/** A thing. */\ntypedef struct ke_thing { ke_width width; } ke_thing;\n");
        var cache = Path.Combine(root, "cache");
        var request = new Extraction.Request([header], [include], [], null, ProbeConvention.Value);
        try
        {
            Extraction.CacheDirectory = cache;
            var first = Extraction.Run(request);
            Assert.Single(Directory.GetFiles(cache));
            Assert.Equal(first, Extraction.Run(request));
            Assert.Single(Directory.GetFiles(cache));

            File.WriteAllText(width, "typedef long ke_width;\n");
            Extraction.Run(request);
            Assert.Equal(2, Directory.GetFiles(cache).Length);
        }
        finally
        {
            Extraction.CacheDirectory = null;
            Directory.Delete(root, recursive: true);
        }
    }
}
