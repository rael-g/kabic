using Kabic.Bindings;
using Kabic.Factories;

namespace Kabic.Tests;

public class FactoriesTests
{
    static readonly BindingJob Job = new("Probe", "native", "out", "Probe.Native", "ke_probe", [], "probe.h", null,
        [], [], [], [], [], "", "Probe", "NativeMethods");

    static Factory Standalone(params FactoryArgument[] arguments) => Configured(null, arguments);

    static Factory Configured(ApiStruct? config, params FactoryArgument[] arguments) => new(Job, "src/Probe", "Probe",
        new ApiFunction("ke_probe_create", "ke_result", "Creates a probe.", null, []),
        config, null, null, "Probe", "ke_probe_handle", arguments);

    static readonly ApiStruct ValueConfig = new("ke_probe_config", null, [], [new ApiField("depth", "uint32_t", [], null)], []);
    static readonly ApiStruct BorrowedConfig = new("ke_probe_config", null, ["borrowed"],
        [new ApiField("name", "const char *", ["utf8"], null), new ApiField("depth", "uint32_t", [], null)], []);
    static readonly FactoryArgument Config = new("Its values.", null, "config", "Probe.ProbeConfig", "&nativeConfig", false) { IsConfig = true };

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
    public void A_required_provider_is_checked_and_passed_before_a_value_config_taken_whole()
    {
        var f = Configured(ValueConfig, Pool, Config);
        var text = FactoriesExtension.Render(f, ProbeConvention.Value);
        Assert.Contains("ArgumentNullException.ThrowIfNull(pool);", text);
        Assert.Contains("var nativeConfig = config;", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, &nativeConfig, &handle)", text);
        Assert.Equal("Probe.INativePool pool, in Probe.ProbeConfig config", FactoriesExtension.Parameters(f));
    }

    [Fact]
    public void A_borrowed_config_is_rebuilt_into_the_native_struct_with_its_strings_pinned_as_utf8()
    {
        var text = FactoriesExtension.Render(Configured(BorrowedConfig, Config), ProbeConvention.Value);
        Assert.Contains("var nameConfigBytes = config.Name is null ? null : System.Text.Encoding.UTF8.GetBytes(config.Name + \"\\0\");", text);
        Assert.Contains("fixed (byte* nameConfigPtr = nameConfigBytes)", text);
        Assert.Contains("Probe.Native.ke_probe_config nativeConfig = default;", text);
        Assert.Contains("nativeConfig.name = (sbyte*)nameConfigPtr;", text);
        Assert.Contains("nativeConfig.depth = config.Depth;", text);
    }

    [Fact]
    public void A_positional_string_is_pinned_as_utf8_and_passed_before_the_config()
    {
        var output = new FactoryArgument("The group it writes.", null, "output", "string?", "(sbyte*)outputPtr", true);
        var text = FactoriesExtension.Render(Configured(ValueConfig, Pool, output, Config), ProbeConvention.Value);
        Assert.Contains("fixed (byte* outputPtr = outputBytes)", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, (sbyte*)outputPtr, &nativeConfig, &handle)", text);
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
        Assert.Contains("NativeMethods.probe_create(pool.Native, &resources, &handle)", text);
        Assert.Contains("return (handle, resources);", text);
        Assert.Contains("_handle = made.Handle; Resources = made.Resources;", text);
    }

    [Fact]
    public void An_out_interface_pointer_is_kept_as_an_address_written_through_its_native_type()
    {
        var f = Standalone(Pool) with { Outputs = [new FactoryOutput("Completes when it is ready.", "Ready", "nint") { NativeCast = "(Probe.Native.ke_signal**)" }] };
        var text = FactoriesExtension.Render(f, ProbeConvention.Value);
        Assert.Contains("public nint Ready { get; }", text);
        Assert.Contains("public nint Ready { get; }\n\n    /// <summary>Creates a probe.</summary>", text);
        Assert.Contains("NativeMethods.probe_create(pool.Native, (Probe.Native.ke_signal**)&ready, &handle)", text);
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
        Assert.Contains("NativeMethods.probe_create(&context, &handle)", text);
    }
}

