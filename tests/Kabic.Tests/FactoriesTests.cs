using Kabic.Bindings;
using Kabic.Factories;

namespace Kabic.Tests;

public class FactoriesTests
{
    static readonly BindingJob Job = new("Probe", "native", "out", "Probe.Native", "ke_probe", [], "probe.h", null,
        [], [], [], [], [], "", "Probe", "NativeMethods");

    static Factory Standalone(params FactoryArgument[] arguments) => new(Job, "src/Probe", "Probe",
        new ApiFunction("ke_probe_create", "ke_result", "Creates a probe.", null, []),
        new ApiStruct("ke_probe_config", null, [], [], []), null, null, "Probe", "ke_probe_handle", arguments);

    static readonly FactoryArgument Pool = new("The pool it runs on.", null, "pool", "Probe.INativePool", "pool.Native", false);
    static readonly FactoryArgument Name = new("Its name.", "name", "name", "string?", "name", true);
    static readonly FactoryArgument Depth = new("How deep.", "depth", "depth", "uint", "depth", false);

    [Fact]
    public void A_factory_without_a_contract_owns_and_releases_its_handle()
    {
        var text = FactoriesExtension.Render(Standalone(Pool), ProbeConvention.Value);
        Assert.Contains("public sealed unsafe partial class Probe : IDisposable", text);
        Assert.Contains("private Probe.Native.ke_probe_handle _handle;", text);
        Assert.Contains("public void Dispose()", text);
    }

    [Fact]
    public void A_required_provider_is_checked_and_passed_before_the_config()
    {
        var text = FactoriesExtension.Render(Standalone(Pool, Depth), ProbeConvention.Value);
        Assert.Contains("ArgumentNullException.ThrowIfNull(pool);", text);
        Assert.Contains("config.Depth = depth;", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, &config, &handle)", text);
    }

    [Fact]
    public void A_string_config_field_is_pinned_as_utf8()
    {
        var text = FactoriesExtension.Render(Standalone(Name), ProbeConvention.Value);
        Assert.Contains("var nameBytes = name is null ? null : System.Text.Encoding.UTF8.GetBytes(name + \"\\0\");", text);
        Assert.Contains("fixed (byte* namePtr = nameBytes)", text);
        Assert.Contains("config.Name = (sbyte*)namePtr;", text);
    }

    [Fact]
    public void Only_the_arguments_after_the_last_required_provider_default()
    {
        var f = Standalone(Depth, Pool, Name);
        Assert.Equal("uint depth, Probe.INativePool pool, string? name = default", FactoriesExtension.Parameters(f));
    }
}
