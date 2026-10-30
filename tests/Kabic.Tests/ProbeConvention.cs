
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
        .Symbols("ke_", "_handle", "_create")
        .Bindings("kernel_engine", "KernelEngine.Common.Native")
        .Managed("KernelEngine.Common")
        .Result("ke_result", "KE_OK", "KernelEngine.Result.Native.ke_result", "KernelEngine.NativeResults")
        .AddHandleType("ke_mesh_handle", "KernelEngine.Render.MeshHandle")
        .AddHandleType("ke_material_handle", "KernelEngine.Render.MaterialHandle")
        .AddHandleType("ke_texture_handle", "KernelEngine.Render.TextureHandle")
        .AddHandleType("ke_ui_font_handle", "KernelEngine.Render.FontHandle")
        .AddTypeName("ke_ecs", "EcsRegistry")
        .AddTypeName("ke_asset_resolver", "NativeAssetResolver")
        .AddTypeName("ke_input_actions", "NativeInputActions")
        .AddTypeName("ke_physics_2d", "Physics2D")
        .AddTypeName("ke_body_type_2d", "BodyType2D")
        .AddTypeName("ke_phase", "RuntimePhase")
        .AddTypeName("ke_access", "RuntimeAccess")
        .Build();
}
