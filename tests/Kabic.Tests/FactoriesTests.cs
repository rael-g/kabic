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
    public void A_positional_string_is_pinned_as_utf8_and_passed_before_the_config()
    {
        var output = new FactoryArgument("The group it writes.", null, "output", "string?", "(sbyte*)outputPtr", true);
        var text = FactoriesExtension.Render(Standalone(Pool, output, Depth), ProbeConvention.Value);
        Assert.Contains("fixed (byte* outputPtr = outputBytes)", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, (sbyte*)outputPtr, &config, &handle)", text);
    }

    [Fact]
    public void Only_the_arguments_after_the_last_required_provider_default()
    {
        var f = Standalone(Depth, Pool, Name);
        Assert.Equal("uint depth, Probe.INativePool pool, string? name = default", FactoriesExtension.Parameters(f));
    }

    [Fact]
    public void An_out_before_the_handle_is_written_by_the_call_and_kept_as_a_property()
    {
        var f = Standalone(Pool) with { Outputs = [new FactoryOutput("The ids it writes.", "Resources", "Probe.ProbeResources")] };
        var text = FactoriesExtension.Render(f, ProbeConvention.Value);
        Assert.Contains("public Probe.ProbeResources Resources { get; }", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, &config, &resources, &handle)", text);
        Assert.Contains("return (handle, resources);", text);
        Assert.Contains("_handle = made.Handle; Resources = made.Resources;", text);
    }

    [Fact]
    public void The_contracts_of_a_struct_of_contracts_are_gathered_into_it_and_passed_once()
    {
        var runtime = new FactoryArgument("Runs it.", null, "runtime", "Probe.INativeRuntime", "context.Runtime = runtime.Native", false) { Bundle = ("context", "Probe.Native.ProbeContext") };
        var device = new FactoryArgument("Draws it.", null, "device", "Probe.INativeDevice", "context.Device = device.Native", false) { Bundle = ("context", "Probe.Native.ProbeContext") };
        var text = FactoriesExtension.Render(Standalone(runtime, device), ProbeConvention.Value);
        Assert.Contains("Probe.Native.ProbeContext context = default;", text);
        Assert.Contains("context.Runtime = runtime.Native;", text);
        Assert.Contains("context.Device = device.Native;", text);
        Assert.Contains("NativeMethods.probe_create(&context, &config, &handle)", text);
    }
}

