
namespace Kabic.Tests;

internal static class ProbeConvention
{
    public static readonly Convention Value = new ConventionBuilder()
        .AddType(Languages.CSharp, "ke_vec2", "System.Numerics.Vector2")
        .AddType(Languages.CSharp, "ke_vec3", "System.Numerics.Vector3")
        .AddType(Languages.CSharp, "ke_vec4", "System.Numerics.Vector4")
        .AddType(Languages.CSharp, "ke_quat", "System.Numerics.Quaternion")
        .AddType(Languages.CSharp, "ke_mat4", "System.Numerics.Matrix4x4")
        .AddNameRule(Languages.CSharp, "^out_(.+)$", "$1")
        .Symbols("ke_")
        .Bindings("kernel_engine", "KernelEngine.Common.Native")
        .Managed("KernelEngine.Common", "global::KernelEngine.Interop.NativeOwner")
        .Result("ke_result", "KE_OK", "KernelEngine.Result.Native.ke_result", "KernelEngine.NativeResults")
        .AddType(Languages.CSharp, "ke_mesh_handle", "KernelEngine.Render.MeshHandle")
        .AddType(Languages.CSharp, "ke_material_handle", "KernelEngine.Render.MaterialHandle")
        .AddType(Languages.CSharp, "ke_texture_handle", "KernelEngine.Render.TextureHandle")
        .AddType(Languages.CSharp, "ke_ui_font_handle", "KernelEngine.Render.FontHandle")
        .AddNameRule("csharp-type", "^ke_ecs$", "EcsRegistry")
        .AddNameRule("csharp-type", "^ke_asset_resolver$", "NativeAssetResolver")
        .AddNameRule("csharp-type", "^ke_input_actions$", "NativeInputActions")
        .AddNameRule("csharp-type", "^ke_physics_2d$", "Physics2D")
        .AddNameRule("csharp-type", "^ke_body_type_2d$", "BodyType2D")
        .AddNameRule("csharp-type", "^ke_phase$", "RuntimePhase")
        .AddNameRule("csharp-type", "^ke_access$", "RuntimeAccess")
        .Build();
}
