using System.Text.Json.Nodes;

using Kabic;
using Kabic.CSharp;

namespace Kabic.Tests;

internal static class GeneratorShapeCases
{
    static readonly Dictionary<string, Action<List<string>>> Registered = new();
    static int declared;

    public static IReadOnlyDictionary<string, Action<List<string>>> All()
    {
        if (declared++ == 0) Declare();
        return Registered;
    }

    static void Register(string what, Action<List<string>> run)
    {
        var name = what;
        for (var n = 2; Registered.ContainsKey(name); n++) name = $"{what} ({n})";
        Registered[name] = run;
    }

    static void Declare()
    {
        Expect("a void slot's single out parameter becomes the return",
            Vtable("ke_probe", Slot("size_of", "void",
                Param("out_size", "uint32_t *", "out"))),
            contains: ["public uint SizeOf("],
            absent: ["uint* outSize"]);

        Expect("a slot returning the result code throws on a code other than success",
            Vtable("ke_probe", Slot("flush", "ke_result", Param("force", "bool"))),
            contains: ["public void Flush(bool force)",
                       "KernelEngine.NativeResults.ThrowIfFailed(Handle->flush(Handle, force), \"flush\");"],
            absent: ["&err", "ke_error"]);

        Expect("a slot returning the result code hands its single out parameter back as the return",
            Vtable("ke_probe", Slot("count_of", "ke_result", Param("kind", "uint32_t"),
                ParamDoc("out_count", "uint32_t *", "out", "How many there are."))),
            contains: ["public uint CountOf(uint kind)",
                       "uint resultOut = default;",
                       "KernelEngine.NativeResults.ThrowIfFailed(Handle->count_of(Handle, kind, &resultOut), \"count_of\");",
                       "return resultOut;", "<returns>How many there are.</returns>"],
            absent: ["&err", "out uint count"]);

        Expect("a factory returning the result code writes the handle through its last parameter",
            Vtable("ke_probe", "provider", Slot("flush", "ke_result")),
            functions: [Function("ke_probe_create", "ke_result", Param("size", "uint32_t"),
                Param("out_probe", "ke_probe_handle *", "out"))],
            contains: ["(uint size)", "ke_probe_handle handle;",
                       "KernelEngine.NativeResults.ThrowIfFailed(global::Probe.Native.NativeMethods.probe_create(size, &handle), \"ke_probe_create\");"],
            absent: ["&err", "outProbe"]);

        Expect("a shutdown slot with no failure lane is called with its handle alone",
            Vtable("ke_probe", Slot("on_shutdown", "void", "lifecycle:shutdown")),
            contains: ["_native->on_shutdown(_native);"],
            absent: ["_native->on_shutdown(_native, null);"]);

        Expect("a result-returning slot that writes memory a projection owns answers with that projection",
            Vtable("ke_probe",
                Slot("open", "ke_result", Param("self", "ke_probe *"),
                    Param("out_bundle", "ke_probe_bundle **", "out")),
                Slot("close", "void", "releases:Bundle", Param("self", "ke_probe *"),
                    Param("bundle", "ke_probe_bundle *"))),
            contains: ["public Bundle Open(ke_probe* self)",
                       "ke_probe_bundle* resultOut = default;",
                       "return new Bundle(this, (ProbeBundle*)resultOut);"],
            absent: ["&err"],
            structs: [ViewJson("ke_probe_bundle")]);

        Expect("a utf8 parameter is a string on the public surface",
            Vtable("ke_probe", Slot("named", "ke_entity", Param("name", "const char *", "utf8"))),
            contains: ["public ulong Named(string name)", "Encoding.UTF8.GetBytes"],
            absent: ["sbyte* name)"]);

        Expect("a rooted parameter takes an object and is kept alive",
            Vtable("ke_probe",
                Slot("bind", "ke_result", Param("entity", "ke_entity"),
                     Param("instance", "void *", "rooted:entity")),
                Slot("drop", "void", "unroots:entity", Param("entity", "ke_entity")),
                Slot("instance_of", "bool", "try", Param("entity", "ke_entity"),
                     Param("out_type", "uint32_t *", "out"),
                     Param("out_instance", "void **", "out,rooted"))),
            contains: ["public void Bind(ulong entity, object instance)", "GCHandle.Alloc(instance)",
                       "_rooted[entity] = instanceHandle;", "freed.Free();",
                       "public bool TryInstanceOf(ulong entity, out uint type, out object? instance)",
                       "nint instanceLocal;",
                       "instance = instanceLocal == 0 ? null :"
                       + " System.Runtime.InteropServices.GCHandle.FromIntPtr(instanceLocal).Target;"],
            absent: ["public void Bind(ulong entity, nint instance)", "out nint instance)"]);

        Expect("a fallible unrooting slot lets go of the object only once the call succeeded",
            Vtable("ke_probe",
                Slot("bind", "ke_result", Param("entity", "ke_entity"),
                     Param("instance", "void *", "rooted:entity")),
                Slot("drop", "ke_result", "unroots:entity", Param("entity", "ke_entity"))),
            contains: ["KernelEngine.NativeResults.ThrowIfFailed(Handle->drop(Handle, entity), \"drop\");\n"
                       + "        if (_rooted.TryRemove(entity, out var freed)) freed.Free();"],
            absent: []);

        Expect("a rooted written-back parameter that is not an opaque pointer is refused",
            Vtable("ke_probe", Slot("instance_of", "bool", "try", Param("entity", "ke_entity"),
                Param("out_instance", "uint64_t *", "out,rooted"))),
            contains: [], absent: [],
            throws: "a rooted pointer is the one thing it never does");

        Expect("a slot that answers and writes spells the written value out",
            Vtable("ke_probe", Slot("ask", "ke_entity",
                Param("entity", "ke_entity"),
                Param("out_kind", "ke_probe_verdict *", "out,enum:ke_probe_verdict"))),
            contains: ["public ulong Ask(ulong entity, out ProbeVerdict kind)", "ProbeVerdict kindLocal;",
                       "(ke_probe_verdict*)&kindLocal", "kind = kindLocal;"],
            absent: ["ProbeVerdict* outKind", "public ProbeVerdict Ask("]);

        Expect("a slot answering with a declared enum answers in its names",
            Vtable("ke_probe", Slot("verdict_of", "ke_probe_verdict",
                Param("entity", "ke_entity"))),
            contains: ["public ProbeVerdict VerdictOf(ulong entity)",
                       "return (ProbeVerdict)Handle->verdict_of(Handle, entity);"],
            absent: ["public ke_probe_verdict VerdictOf("]);

        Expect("a stripped out_ name colliding with a declared parameter is refused",
            Vtable("ke_probe", Slot("ask_kind", "void",
                Param("kind", "uint32_t"),
                Param("out_kind", "uint32_t *", "out"))),
            contains: [], absent: [],
            throws: "already answers to kind");

        Expect("a returned pointer counted by an out parameter becomes a span",
            Vtable("ke_probe", Slot("samples", "const uint32_t *",
                Param("type", "uint32_t"),
                Param("out_count", "uint32_t *", "out"),
                Returning("array_of:out_count"))),
            contains: ["public ReadOnlySpan<uint> Samples(uint type)", "uint countLocal = 0;",
                       "var front = Handle->samples(Handle, type, &countLocal);",
                       "return front == null ? default : new ReadOnlySpan<uint>(front, (int)countLocal);"],
            absent: ["out uint outCount", "public uint* Samples("]);

        ExpectFreeFunctions("a free function honours [ctx], [out] and a counted return",
            [
                Function("ke_probe_ctx_samples", "const uint32_t *",
                    Param("ctx", "ke_probe_ctx *", "ctx"),
                    Param("query", "uint32_t"),
                    Param("out_count", "size_t *", "out"),
                    Returning("array_of:out_count")),
                Function("ke_probe_ctx_share", "void",
                    Param("ctx", "ke_probe_ctx *", "ctx"),
                    Param("out_index", "uint32_t *", "out"),
                    Param("out_count", "uint32_t *", "out")),
            ],
            contains: ["public static ReadOnlySpan<uint> Samples(nint ctx, uint query)",
                       "var front = Native.ke_probe_ctx_samples((ke_probe_ctx*)ctx, query, &countLocal);",
                       "nuint countLocal = 0;",
                       "return front == null ? default : new ReadOnlySpan<uint>(front, (int)countLocal);",
                       "public static (uint Index, uint Count) Share(nint ctx)",
                       "Native.ke_probe_ctx_share((ke_probe_ctx*)ctx, &index, &count);",
                       "return (index, count);"],
            absent: ["in ke_probe_ctx ctx", "fixed (ke_probe_ctx* p =",
                     "Samples(nint ctx, uint query, nuint* outCount)", "out uint index"]);

        ExpectFreeFunctions("an opaque payload counted in bytes takes the caller's own type",
            [
                Function("ke_probe_ctx_attach", "bool",
                    Param("ctx", "ke_probe_ctx *", "ctx"),
                    Param("entity", "uint64_t"),
                    Param("data", "const void *", "bytes_of:size"),
                    Param("size", "size_t")),
            ],
            contains: ["public static bool Attach<TData>(nint ctx, ulong entity, in TData data)"
                       + " where TData : unmanaged",
                       "fixed (TData* dataPtr = &data)",
                       "return Native.ke_probe_ctx_attach((ke_probe_ctx*)ctx, entity, (void*)dataPtr,"
                       + " (nuint)sizeof(TData));"],
            absent: ["public static bool Attach(nint"]);

        Expect("an opaque payload on a fallible slot takes the caller's own type",
            Vtable("ke_probe", Slot("attach", "ke_result",
                Param("entity", "uint64_t"),
                Param("data", "const void *", "bytes_of:size"),
                Param("size", "size_t"))),
            contains: ["public void Attach<TData>(ulong entity, in TData data) where TData : unmanaged",
                       "fixed (TData* dataPtr = &data)",
                       "(void*)dataPtr, (nuint)sizeof(TData))",
                       "KernelEngine.NativeResults.ThrowIfFailed("],
            absent: ["public void Attach(ulong"]);

        Expect("a typed pointer tagged as an opaque payload is refused",
            Vtable("ke_probe", Slot("write", "void",
                Param("data", "const uint32_t *", "bytes_of:size"),
                Param("size", "size_t"))),
            contains: [], absent: [],
            throws: "already names one");

        Expect("a callback and its context become one delegate parameter",
            Vtable("ke_probe", Slot("watch", "ke_result",
                Param("on_event", "ke_probe_event_func", "closure:event_ctx"),
                Param("event_ctx", "void *"))),
            contains: ["public delegate void ProbeEvent(uint tick);",
                       "public void Watch(ProbeEvent? onEvent)",
                       "GCHandle.Alloc(onEvent)",
                       "private static void WatchOnEventTrampoline(void* ctx, uint arg1)",
                       "(delegate* unmanaged[Cdecl]<void*, uint, void>)&WatchOnEventTrampoline",
                       "handler(arg1)",
                       "s_parkedCallbackException ??= ex;",
                       "throw parked;",
                       "onEventHandle.Free();"],
            absent: ["void* eventCtx", "ke_probe_event_func onEvent", "_retainedOnEvent"],
            aliases: new() { ["ke_probe_event_func"] = "void (*)(void *, uint32_t)" },
            callbacks: [Callback("ke_probe_event_func", "void",
                Param("ctx", "void *", "context"), Param("tick", "uint32_t"))]);

        Expect("a retained closure is kept per key and reports through the code it returns",
            Vtable("ke_probe", Slot("register_apply", "ke_result",
                Param("cid", "uint32_t"),
                Param("apply", "ke_probe_apply_fn", "closure:ctx,retained:cid"),
                Param("ctx", "void *"))),
            contains: ["public delegate void ProbeApply(uint tick);",
                       "private readonly Dictionary<uint, GCHandle> _retainedApply = new();",
                       "public void RegisterApply(uint cid, ProbeApply? apply)",
                       "private static KernelEngine.Result.Native.ke_result RegisterApplyTrampoline(void* ctx, uint arg1)",
                       "return KernelEngine.NativeResults.ToCode(ex);",
                       "if (_retainedApply.Remove(cid, out var replacedApply)) replacedApply.Free();",
                       "_retainedApply[cid] = applyHandle;",
                       "foreach (var retained in _retainedApply.Values) retained.Free();"],
            absent: ["void* ctx)", "applyHandle.Free();\n        _retainedApply"],
            aliases: new() { ["ke_probe_apply_fn"] = "ke_result (*)(void *, uint32_t)" },
            callbacks: [Callback("ke_probe_apply_fn", "ke_result",
                Param("ctx", "void *", "context"), Param("tick", "uint32_t"))]);

        Expect("an expanded params bag flattens the call and its shared context roots one object",
            Vtable("ke_probe", Slot("register_module", "ke_result",
                Param("p", "const ke_probe_module_params *", "expand"),
                Param("out_id", "uint64_t *", "out"))),
            contains: ["public ulong RegisterModule(string name, ProbeLoad? onLoad, ProbeUnload? onUnload = null)",
                       "private readonly Dictionary<ulong, GCHandle> _retainedUserData = new();",
                       "GCHandle.Alloc(new RegisterModuleClosures { OnLoad = onLoad, OnUnload = onUnload })",
                       "ke_probe_module_params p = default;",
                       "p.user_data = (void*)GCHandle.ToIntPtr(userDataHandle);",
                       "Handle->register_module(Handle, &p, &resultOut)",
                       "if (userDataHandle.IsAllocated) _retainedUserData[resultOut] = userDataHandle;",
                       "return resultOut;",
                       "private sealed class RegisterModuleClosures",
                       "state.OnLoad is { } handler",
                       "state.OnUnload is { } handler",
                       "s_parkedCallbackException ??= ex;",
                       "s_parkedCallbackException = null;\n        _destroy(_native);",
                       "foreach (var retained in _retainedUserData.Values) retained.Free();",
                       "throw parked;"],
            absent: ["ke_probe_module_params* p", "void* userData", "onLoadHandle", "onUnloadHandle"],
            aliases: new()
            {
                ["ke_probe_load_fn"] = "ke_result (*)(void *)",
                ["ke_probe_unload_fn"] = "void (*)(void *)"
            },
            callbacks: [Callback("ke_probe_load_fn", "ke_result",
                            Param("ctx", "void *", "context")),
                        Callback("ke_probe_unload_fn", "void",
                            Param("ctx", "void *", "context"))],
            structs: [new JsonObject
            {
                ["name"] = "ke_probe_module_params",
                ["doc"] = null,
                ["tags"] = new JsonArray(),
                ["fields"] = new JsonArray(
                    Param("name", "const char *", "utf8"),
                    Param("user_data", "void *", "context"),
                    Param("on_load", "ke_probe_load_fn", "closure:user_data"),
                    Param("on_unload", "ke_probe_unload_fn", "closure:user_data,retained:return,teardown,default:none")),
                ["slots"] = new JsonArray(),
            }]);

        Expect("a result-returning registration keeps its closure under the id it writes and its handler fails by code",
            Vtable("ke_probe",
                Slot("register_job", "ke_result",
                    Param("job", "ke_probe_job_fn", "closure:ctx,retained:return"),
                    Param("ctx", "void *"),
                    Param("out_id", "uint32_t *", "out"))),
            contains: ["public uint RegisterJob(ProbeJob? job)",
                       "uint resultOut = default;",
                       "Handle->register_job(Handle, ",
                       "&resultOut)",
                       "_retainedJob[resultOut] = jobHandle;",
                       "return resultOut;",
                       "private static KernelEngine.Result.Native.ke_result RegisterJobTrampoline(",
                       "(delegate* unmanaged[Cdecl]<void*, float, KernelEngine.Result.Native.ke_result>)&RegisterJobTrampoline",
                       "return KernelEngine.Result.Native.ke_result.KE_OK;",
                       "return KernelEngine.NativeResults.ToCode(ex);"],
            absent: ["&err", "ke_error"],
            aliases: new() { ["ke_probe_job_fn"] = "ke_result (*)(void *, float)" },
            callbacks: [Callback("ke_probe_job_fn", "ke_result",
                            Param("ctx", "void *", "context"), Param("dt", "float"))]);

        Expect("every pointer+count pair in a slot becomes its own span",
            Vtable("ke_probe", Slot("declare", "uint64_t",
                Param("name", "const char *", "utf8"),
                Param("terms", "const ke_probe_term *", "array_of:term_count"),
                Param("term_count", "uint32_t"),
                Param("tags", "const uint32_t *", "array_of:tag_count"),
                Param("tag_count", "uint32_t"))),
            contains: ["public ulong Declare(string name, ReadOnlySpan<ke_probe_term> terms, ReadOnlySpan<uint> tags)",
                       "fixed (ke_probe_term* termsPtr = terms)",
                       "fixed (uint* tagsPtr = tags)",
                       "termsPtr, (uint)terms.Length, tagsPtr, (uint)tags.Length"],
            absent: ["uint termCount", "uint tagCount"],
            structs: [new JsonObject
            {
                ["name"] = "ke_probe_term",
                ["doc"] = null,
                ["tags"] = new JsonArray(),
                ["fields"] = new JsonArray(Param("cid", "uint32_t")),
                ["slots"] = new JsonArray(),
            }]);

        Expect("a context lane is an nint the caller relays, not a native pointer",
            Vtable("ke_probe", Slot("spawn", "ke_entity",
                Param("sys", "ke_system_ctx *", "ctx"),
                Param("parent", "ke_entity"))),
            contains: ["public ulong Spawn(nint sys, ulong parent)", "(ke_system_ctx*)sys, parent"],
            absent: ["ke_system_ctx* sys"]);

        Expect("a handler relays a context lane as nint and reports its own failure by throwing",
            Vtable("ke_probe", Slot("register_body", "ke_result",
                Param("body", "ke_probe_body_fn", "closure:ctx"),
                Param("ctx", "void *"))),
            contains: ["public delegate void ProbeBody(nint sys, float dt);",
                       "handler((nint)arg0, arg2);",
                       "return KernelEngine.Result.Native.ke_result.KE_OK;"],
            absent: ["delegate KernelEngine.Result.Native.ke_result ProbeBody", "ke_system_ctx* sys"],
            aliases: new() { ["ke_probe_body_fn"] = "ke_result (*)(ke_system_ctx *, void *, float)" },
            callbacks: [Callback("ke_probe_body_fn", "ke_result",
                Param("sys", "ke_system_ctx *", "ctx"), Param("ctx", "void *", "context"),
                Param("dt", "float"))]);

        Expect("a handler called with the provider is handed the managed wrapper, not the pointer",
            Vtable("ke_probe", Slot("register_body", "ke_result",
                Param("body", "ke_probe_body_fn", "closure:ctx"),
                Param("ctx", "void *"))),
            contains: ["public delegate void ProbeBody(Probe probe);",
                       "public required Probe Owner;",
                       "handler(state.Owner);"],
            absent: ["_callbackFailures", "ke_probe* probe"],
            aliases: new() { ["ke_probe_body_fn"] = "ke_result (*)(ke_probe *, void *)" },
            callbacks: [Callback("ke_probe_body_fn", "ke_result",
                Param("probe", "ke_probe *"), Param("ctx", "void *", "context"))]);

        Expect("a contract spelling a pointer keeps the keyword",
            Vtable("ke_probe", "interface", Slot("samples", "const uint32_t *")),
            contains: ["public unsafe interface IProbe : IDisposable", "    uint* Samples();"],
            absent: ["public interface IProbe : IDisposable"]);

        ExpectStructSpans("a counted pointer field of a native struct reads as a span",
            CountedStruct("ke_probe_segment",
                ("entities", "const uint64_t *", "array_of:count"),
                ("count", "size_t", "")),
            contains: ["namespace Probe.Native;", "public unsafe partial struct ke_probe_segment",
                       "public readonly ReadOnlySpan<ulong> Entities => entities == null ? default"
                       + " : new ReadOnlySpan<ulong>(entities, (int)count);"],
            absent: ["public ulong* entities;", "MemoryMarshal", "Unsafe.As"]);

        ExpectStructSpans("a counted pointer field naming a length the struct lacks is refused",
            CountedStruct("ke_probe_segment",
                ("entities", "const uint64_t *", "array_of:total")),
            contains: [], absent: [],
            throws: "names a length field the struct does not declare");

        ExpectBorrowedStruct("a borrowed struct holds its counted pointers as memory and hides their counts",
            BorrowedStruct("ke_probe_decl",
                ("terms", "const ke_probe_term *", "array_of:term_count"),
                ("term_count", "uint32_t", "")),
            contains: ["public partial struct ProbeDecl", "public ReadOnlyMemory<ProbeTerm> Terms;"],
            absent: ["TermCount", "term_count", "ProbeTerm*", "StructLayout"],
            alongside: [ValueStruct("ke_probe_term", ("cid", "uint32_t"))]);

        ExpectBorrowedStruct("a borrowed struct refuses a pointer nothing counts",
            BorrowedStruct("ke_probe_decl", ("terms", "const ke_probe_term *", "")),
            contains: [], absent: [],
            throws: "names no count",
            alongside: [ValueStruct("ke_probe_term", ("cid", "uint32_t"))]);

        Expect("a sequence of borrowed structs pins every element's memory for the call",
            Vtable("ke_probe", Slot("declare", "uint64_t",
                Param("decls", "const ke_probe_decl *", "array_of:decl_count"),
                Param("decl_count", "uint32_t"))),
            contains: ["public ulong Declare(ReadOnlySpan<ProbeDecl> decls)",
                       "var declsPins = new System.Buffers.MemoryHandle[decls.Length];",
                       "var declsNative = new ke_probe_decl[decls.Length];",
                       "declsPins[declsAt] = decls[declsAt].Terms.Pin();",
                       "declsNative[declsAt].terms = (ProbeTerm*)declsPins[declsAt].Pointer;",
                       "declsNative[declsAt].term_count = (uint)decls[declsAt].Terms.Length;",
                       "fixed (ke_probe_decl* declsPtr = declsNative)",
                       "finally", "declsPins[declsAt].Dispose();",
                       "declsPtr, (uint)decls.Length"],
            absent: ["fixed (ProbeDecl*", "Span<ke_probe_decl>"],
            structs: [new JsonObject
            {
                ["name"] = "ke_probe_decl",
                ["doc"] = null,
                ["tags"] = new JsonArray((JsonNode)"borrowed"),
                ["fields"] = new JsonArray(
                    Param("terms", "const ke_probe_term *", "array_of:term_count"),
                    Param("term_count", "uint32_t")),
                ["slots"] = new JsonArray(),
            },
            new JsonObject
            {
                ["name"] = "ke_probe_term",
                ["doc"] = null,
                ["tags"] = new JsonArray((JsonNode)"value"),
                ["fields"] = new JsonArray(Param("cid", "uint32_t")),
                ["slots"] = new JsonArray(),
            }]);

        ExpectView("a view hands out its counted pointer, its text and its scalars, and keeps its fields private",
            ViewStruct("ke_probe_bundle",
                ("vertices", "ke_vertex *", "array_of:vertex_count"),
                ("vertex_count", "uint32_t", ""),
                ("material_index", "int32_t", ""),
                ("name", "char [64]", "")),
            contains: ["public unsafe partial struct ProbeBundle",
                       "private readonly Vertex* vertices;",
                       "private readonly uint vertex_count;",
                       "private fixed sbyte name[64];",
                       "public readonly ReadOnlySpan<Vertex> Vertices => vertices == null ? default"
                       + " : new ReadOnlySpan<Vertex>(vertices, (int)vertex_count);",
                       "public readonly int MaterialIndex => material_index;",
                       "fixed (sbyte* p = name) return Marshal.PtrToStringUTF8((nint)p) ?? \"\";"],
            absent: ["public readonly uint VertexCount", "ReadOnlySpan<ke_vertex>", "ke_vertex*",
                     "MemoryMarshal", "Unsafe.As"]);

        ExpectView("a view field reaching somewhere with no extent is refused",
            ViewStruct("ke_probe_bundle", ("pixels", "uint8_t *", "")),
            contains: [], absent: [],
            throws: "has to name the count bounding it");

        Expect("a slot hands out and takes back the projection of a [view], casting at the ABI",
            Vtable("ke_probe",
                Slot("open", "ke_probe_bundle *", Param("self", "ke_probe *")),
                Slot("close", "void", Param("self", "ke_probe *"),
                    Param("bundle", "ke_probe_bundle *"))),
            contains: ["public ProbeBundle* Open(", "return (ProbeBundle*)Handle->open(",
                       "ProbeBundle* bundle)", "(ke_probe_bundle*)bundle"],
            absent: ["ke_probe_bundle* Open", "ke_probe_bundle* bundle)"],
            structs: [ViewJson("ke_probe_bundle")]);

        Expect("[releases] pairs a handed-out pointer with the object that gives it back",
            Vtable("ke_probe",
                Slot("open", "ke_result", Param("self", "ke_probe *"),
                    Param("out_bundle", "ke_probe_bundle **", "out")),
                Slot("close", "void", "releases:Bundle", Param("self", "ke_probe *"),
                    Param("bundle", "ke_probe_bundle *"))),
            contains: ["public unsafe sealed class Bundle : IDisposable",
                       "public ProbeBundle Data => _native is null",
                       "_owner.Close(native);",
                       "public Bundle Open(", "return new Bundle(this, (ProbeBundle*)resultOut);",
                       "internal void Close(ke_probe* self, ProbeBundle* bundle)"],
            absent: ["public void Close(", "ProbeBundle* Open("],
            structs: [ViewJson("ke_probe_bundle")]);

        Expect("owned memory handed out with no failure channel is refused",
            Vtable("ke_probe",
                Slot("open", "ke_probe_bundle *", Param("self", "ke_probe *")),
                Slot("close", "void", "releases:Bundle", Param("self", "ke_probe *"),
                    Param("bundle", "ke_probe_bundle *"))),
            contains: [], absent: [],
            structs: [ViewJson("ke_probe_bundle")],
            throws: "has no failure channel");

        Expect("a completion handed the result code fails the Task with the code's exception",
            Vtable("ke_probe",
                Slot("open_async", "ke_task *", Param("self", "ke_probe *"),
                    Param("path", "const char *", "utf8"),
                    Param("on_done", "ke_probe_done_func", "completion:user_data"),
                    Param("user_data", "void *")),
                Slot("close", "void", "releases:Bundle", Param("self", "ke_probe *"),
                    Param("bundle", "ke_probe_bundle *"))),
            contains: ["public Task<Bundle> OpenAsync(ke_probe* self, string path)",
                       "private static void OpenAsyncOnDoneTrampoline(KernelEngine.Result.Native.ke_result arg0, ke_probe_bundle* arg1, void* ctx)",
                       "if (arg0 != KernelEngine.Result.Native.ke_result.KE_OK || arg1 == null)",
                       "state.Source.TrySetException(KernelEngine.NativeResults.FromCode(arg0, \"open_async\"));"],
            absent: ["ke_error"],
            structs: [ViewJson("ke_probe_bundle")],
            aliases: new() { ["ke_probe_done_func"] = "void (*)(ke_result, ke_probe_bundle *, void *)" },
            callbacks: [Callback("ke_probe_done_func", "void",
                Param("result", "ke_result"), Param("bundle", "ke_probe_bundle *"),
                Param("user_data", "void *", "context"))]);

        Expect("an answer nothing claims ownership of is refused, not handed over as a pointer",
            Vtable("ke_probe",
                Slot("open_async", "ke_task *", Param("self", "ke_probe *"),
                    Param("on_done", "ke_probe_done_func", "completion:user_data"),
                    Param("user_data", "void *"))),
            contains: [], absent: [],
            structs: [ViewJson("ke_probe_bundle")],
            aliases: new() { ["ke_probe_done_func"] = "void (*)(ke_result, ke_probe_bundle *, void *)" },
            callbacks: [Callback("ke_probe_done_func", "void",
                Param("result", "ke_result"), Param("bundle", "ke_probe_bundle *"),
                Param("user_data", "void *", "context"))],
            throws: "a Task cannot answer with a pointer");

        Expect("an operation that answers later is refused a result code of its own",
            Vtable("ke_probe",
                Slot("open_async", "ke_result", Param("self", "ke_probe *"),
                    Param("on_done", "ke_probe_done_func", "completion:user_data"),
                    Param("user_data", "void *"))),
            contains: [], absent: [],
            aliases: new() { ["ke_probe_done_func"] = "void (*)(ke_result, void *)" },
            callbacks: [Callback("ke_probe_done_func", "void",
                Param("result", "ke_result"), Param("user_data", "void *", "context"))],
            throws: "a value is not something this form may drop");

        Expect("[provider] asks for another domain's projection, not the pointer inside it",
            Vtable("ke_probe",
                Slot("attach", "void", Param("self", "ke_probe *"),
                    Param("pool", "ke_task_pool *", "provider"))),
            contains: ["Attach(ke_probe* self, INativeTaskPool pool)", "pool.Native"],
            absent: ["ke_task_pool* pool", "ke_task_pool *pool"]);

        void Expect(string what, JsonObject vtable, string[] contains, string[] absent,
            Dictionary<string, string>? aliases = null, JsonObject[]? callbacks = null,
            JsonObject[]? structs = null, string? throws = null, JsonObject[]? functions = null)

        {
            Register(what, failures =>
            {
                var api = new JsonObject
                {
                    ["enums"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "ke_probe_verdict",
                        ["doc"] = null,
                        ["values"] = new JsonArray(new JsonObject
                        { ["name"] = "KE_PROBE_VERDICT_NONE", ["rawValue"] = "0", ["isInt"] = true, ["doc"] = null }),
                    }),
                    ["structs"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "ke_probe_handle",
                        ["doc"] = null,
                        ["tags"] = new JsonArray("owner"),
                        ["fields"] = new JsonArray(
                        new JsonObject { ["name"] = "ref", ["type"] = "ke_probe *", ["tags"] = new JsonArray(), ["doc"] = null },
                        new JsonObject { ["name"] = "destroy", ["type"] = "void (*)(ke_probe *)", ["tags"] = new JsonArray(), ["doc"] = null }),
                        ["slots"] = new JsonArray(),
                    }),
                    ["vtables"] = new JsonArray(vtable),
                    ["callbacks"] = new JsonArray((callbacks ?? []).Cast<JsonNode>().ToArray()),
                    ["functions"] = new JsonArray((functions ?? []).Cast<JsonNode>().ToArray()),
                    ["type_aliases"] = new JsonObject { ["ke_entity"] = "uint64_t" },
                };
                foreach (var extra in structs ?? [])
                    api["structs"]!.AsArray().Add(extra);
                foreach (var (alias, target) in aliases ?? [])
                    api["type_aliases"]!.AsObject()[alias] = target;

                string emitted;
                try
                {
                    var model = ApiReader.Read(api);
                    var convention = ProbeConvention.Value;
                    var classified = Classifier.Classify(model, [], [], convention);
                    var provider = classified.Providers.Single();
                    var source = CSharpWriter.RenderProvider(model, provider, classified, "Probe", "Probe.Native", [], convention);
                    emitted = source.Class + (source.Contract ?? "");
                }
                catch (Exception ex)
                {
                    if (throws is null) failures.Add($"{what}: the writer threw {ex.GetType().Name}: {ex.Message}");
                    else if (!ex.Message.Contains(throws, StringComparison.Ordinal))
                        failures.Add($"{what}: refused for the wrong reason: {ex.Message}");
                    return;
                }
                if (throws is not null)
                {
                    failures.Add($"{what}: expected the writer to refuse, it emitted instead");
                    return;
                }
                if (Environment.GetEnvironmentVariable("KE_SHAPES_DUMP") == what) Console.WriteLine(emitted);

                foreach (var needle in contains)
                    if (!emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected to find \"{needle}\"");

                foreach (var needle in absent)
                    if (emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected NOT to find \"{needle}\"");
            });
        }

        Register("initialized data keeps its shape in Zig, whatever the shape", failures =>
        {
            var model = NewModel();
            model.Enums.Add(new ApiEnum("ke_kind", null, [new ApiEnumValue("KE_KIND_B", "0", true, null)]));
            model.Structs.Add(new ApiStruct("ke_row", null, [],
                [new ApiField("kind", "ke_kind", [], null), new ApiField("at", "uint32_t", [], null), new ApiField("size", "uint32_t", [], null)], []));
            model.Data.Add(new ApiData("ke_rows", "ke_row[1]", true,
                new(ApiValueKind.List, "", [new(ApiValueKind.List, "", [new(ApiValueKind.Name, "KE_KIND_B", []), new(ApiValueKind.OffsetOf, "ke_row, at", []), new(ApiValueKind.SizeOf, "float", [])])])));
            model.Data.Add(new ApiData("ke_grid", "float[1][2]", true,
                new(ApiValueKind.List, "", [new(ApiValueKind.List, "", [new(ApiValueKind.Number, "1.5", []), new(ApiValueKind.Number, "2", [])])])));
            model.Data.Add(new ApiData("ke_origin", "ke_row", false,
                new(ApiValueKind.List, "", [new(ApiValueKind.Name, "KE_KIND_B", []), new(ApiValueKind.Number, "0", []), new(ApiValueKind.Number, "4", [])])));
            model.Data.Add(new ApiData("ke_limit", "int", true, new(ApiValueKind.Number, "3", [])));
            var zig = Kabic.Zig.ZigWriter.Render(model, new Dictionary<string, string>(), false);
            foreach (var needle in new[] { "pub const ke_rows:", "@offsetOf(ke_row, \"at\")", "@sizeOf(f32)", "pub const ke_grid:", "pub var ke_origin:", "pub const ke_limit: c_int = 3;" })
                if (!zig.Contains(needle)) failures.Add($"expected in Zig: {needle}\n{zig}");
        });

        void ExpectFreeFunctions(string what, JsonObject[] functions, string[] contains, string[] absent)

        {
            Register(what, failures =>
            {
                var api = new JsonObject
                {
                    ["enums"] = new JsonArray(),
                    ["structs"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "ke_probe_ctx",
                        ["doc"] = null,
                        ["tags"] = new JsonArray(),
                        ["fields"] = new JsonArray(
                        new JsonObject { ["name"] = "handle", ["type"] = "void *", ["tags"] = new JsonArray(), ["doc"] = null }),
                        ["slots"] = new JsonArray(),
                    }),
                    ["vtables"] = new JsonArray(),
                    ["callbacks"] = new JsonArray(),
                    ["functions"] = new JsonArray(functions.Cast<JsonNode>().ToArray()),
                    ["type_aliases"] = new JsonObject(),
                };

                string emitted;
                try
                {
                    var model = ApiReader.Read(api);
                    var convention = ProbeConvention.Value;
                    var classified = Classifier.Classify(model, [], [], convention);
                    var (owner, group) = classified.FreeFunctionGroups.Single();
                    emitted = CSharpWriter.RenderFreeFunctions(model, owner, group, "Probe", "Probe.Native",
                        [], "ke_probe", convention);
                }
                catch (Exception ex)
                {
                    failures.Add($"{what}: the writer threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (Environment.GetEnvironmentVariable("KE_SHAPES_DUMP") == what) Console.WriteLine(emitted);

                foreach (var needle in contains)
                    if (!emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected to find \"{needle}\"");

                foreach (var needle in absent)
                    if (emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected NOT to find \"{needle}\"");
            });
        }

        static JsonObject Function(string name, string returns, params object[] rest)
        {
            var returnTags = new JsonArray();
            var ps = new JsonArray();
            foreach (var item in rest)
            {
                if (item is JsonObject p) ps.Add(p);
                else if (item is ReturnTags rt) foreach (var one in rt.Tags.Split(',')) returnTags.Add((JsonNode)one.Trim());
            }
            return new JsonObject
            {
                ["name"] = name,
                ["returns"] = returns,
                ["doc"] = null,
                ["return_doc"] = null,
                ["return_tags"] = returnTags,
                ["params"] = ps,
            };
        }

        static ApiModel NewModel()
        {
            var m = new ApiModel();
            foreach (var (name, lanes, tags) in new (string, string[], string[])[]
                {
                    ("ke_vec2", ["x", "y"], []), ("ke_vec3", ["x", "y", "z"], []),
                    ("ke_vec4", ["x", "y", "z", "w"], []), ("ke_quat", ["x", "y", "z", "w"], []),
                })
                m.Structs.Add(new ApiStruct(name, null, tags, lanes.Select(l => new ApiField(l, "float", [], null)).ToList(), []));
            m.Structs.Add(new ApiStruct("ke_mat4", null, [], [new ApiField("m", "float[16]", [], null)], []));
            return m;
        }

        static ApiStruct ValueStruct(string name, params (string Name, string Type)[] fields) =>
            new(name, null, ["value"],
                fields.Select(f => new ApiField(f.Name, f.Type, [], null)).ToList(), []);

        void ExpectStructSpans(string what, ApiStruct s, string[] contains, string[] absent, string? throws = null)

        {
            Register(what, failures =>
            {
                var model = NewModel();
                model.Structs.Add(s);

                string emitted;
                try
                {
                    emitted = CSharpWriter.RenderStructSpans(model, s, "Probe.Native", ProbeConvention.Value);
                }
                catch (Exception ex)
                {
                    if (throws is null) failures.Add($"{what}: the writer threw {ex.GetType().Name}: {ex.Message}");
                    else if (!ex.Message.Contains(throws, StringComparison.Ordinal))
                        failures.Add($"{what}: refused for the wrong reason: {ex.Message}");
                    return;
                }
                if (throws is not null)
                {
                    failures.Add($"{what}: expected the writer to refuse, it emitted instead");
                    return;
                }
                if (Environment.GetEnvironmentVariable("KE_SHAPES_DUMP") == what) Console.WriteLine(emitted);

                foreach (var needle in contains)
                    if (!emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected to find \"{needle}\"");

                foreach (var needle in absent)
                    if (emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected NOT to find \"{needle}\"");
            });
        }

        void ExpectView(string what, ApiStruct s, string[] contains, string[] absent, string? throws = null)

        {
            Register(what, failures =>
            {
                var model = NewModel();
                model.Structs.Add(s);
                model.Structs.Add(new ApiStruct("ke_vertex", null, ["value"],
                    [new ApiField("x", "float", [], null)], [])
                { External = true });

                string emitted;
                try
                {
                    emitted = CSharpWriter.RenderView(model, s, "Probe", ["Probe.Common"],
                        ProbeConvention.Value);
                }
                catch (Exception ex)
                {
                    if (throws is null) failures.Add($"{what}: the writer threw {ex.GetType().Name}: {ex.Message}");
                    else if (!ex.Message.Contains(throws, StringComparison.Ordinal))
                        failures.Add($"{what}: refused for the wrong reason: {ex.Message}");
                    return;
                }
                if (throws is not null)
                {
                    failures.Add($"{what}: expected the writer to refuse, it emitted instead");
                    return;
                }
                if (Environment.GetEnvironmentVariable("KE_SHAPES_DUMP") == what) Console.WriteLine(emitted);

                foreach (var needle in contains)
                    if (!emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected to find \"{needle}\"");

                foreach (var needle in absent)
                    if (emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected NOT to find \"{needle}\"");
            });
        }

        static ApiStruct BorrowedStruct(string name, params (string Name, string Type, string Tags)[] fields) =>
            new(name, null, ["borrowed"],
                fields.Select(f => new ApiField(f.Name, f.Type,
                    f.Tags is "" ? [] : f.Tags.Split(','), null)).ToList(), []);

        void ExpectBorrowedStruct(string what, ApiStruct s, string[] contains, string[] absent,
            string? throws = null, ApiStruct[]? alongside = null)

        {
            Register(what, failures =>
            {
                var model = NewModel();
                model.Structs.Add(s);
                foreach (var other in alongside ?? []) model.Structs.Add(other);

                string emitted;
                try
                {
                    emitted = CSharpWriter.RenderBorrowed(model, s, "Probe", [], ProbeConvention.Value);
                }
                catch (Exception ex)
                {
                    if (throws is null) failures.Add($"{what}: the writer threw {ex.GetType().Name}: {ex.Message}");
                    else if (!ex.Message.Contains(throws, StringComparison.Ordinal))
                        failures.Add($"{what}: refused for the wrong reason: {ex.Message}");
                    return;
                }
                if (throws is not null)
                {
                    failures.Add($"{what}: expected the writer to refuse, it emitted instead");
                    return;
                }
                if (Environment.GetEnvironmentVariable("KE_SHAPES_DUMP") == what) Console.WriteLine(emitted);

                foreach (var needle in contains)
                    if (!emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected to find \"{needle}\"");

                foreach (var needle in absent)
                    if (emitted.Contains(needle, StringComparison.Ordinal))
                        failures.Add($"{what}: expected NOT to find \"{needle}\"");
            });
        }

        static ApiStruct ViewStruct(string name, params (string Name, string Type, string Tags)[] fields) =>
            new(name, null, ["view"],
                fields.Select(f => new ApiField(f.Name, f.Type,
                    f.Tags is "" ? [] : f.Tags.Split(','), null)).ToList(), []);

        static ApiStruct CountedStruct(string name, params (string Name, string Type, string Tags)[] fields) =>
            new(name, null, [],
                fields.Select(f => new ApiField(f.Name, f.Type,
                    f.Tags is "" ? [] : f.Tags.Split(','), null)).ToList(), []);

        static JsonObject Vtable(string name, params object[] rest)
        {
            var tags = new JsonArray();
            var slots = new JsonArray();
            foreach (var item in rest)
            {
                if (item is JsonObject s) slots.Add(s);
                else if (item is string t) foreach (var one in t.Split(',')) tags.Add((JsonNode)one.Trim());
            }
            return new()
            {
                ["name"] = name,
                ["doc"] = null,
                ["tags"] = tags,
                ["fields"] = new JsonArray(new JsonObject { ["name"] = "handle", ["type"] = "void *", ["tags"] = new JsonArray(), ["doc"] = null }),
                ["slots"] = slots,
            };
        }

        static JsonObject ViewJson(string name) => new()
        {
            ["name"] = name,
            ["doc"] = null,
            ["tags"] = new JsonArray((JsonNode)"view"),
            ["fields"] = new JsonArray(new JsonObject
            { ["name"] = "count", ["type"] = "uint32_t", ["tags"] = new JsonArray(), ["doc"] = null }),
            ["slots"] = new JsonArray(),
        };

        static JsonObject Slot(string name, string returns, params object[] rest)
        {
            var tags = new JsonArray();
            var returnTags = new JsonArray();
            var ps = new JsonArray();
            foreach (var item in rest)
            {
                if (item is JsonObject p) ps.Add(p);
                else if (item is ReturnTags rt) foreach (var one in rt.Tags.Split(',')) returnTags.Add((JsonNode)one.Trim());
                else if (item is string t) foreach (var one in t.Split(',')) tags.Add((JsonNode)one.Trim());
            }
            return new JsonObject
            {
                ["name"] = name,
                ["returns"] = returns,
                ["tags"] = tags,
                ["doc"] = null,
                ["returnDoc"] = null,
                ["return_tags"] = returnTags,
                ["params"] = ps,
            };
        }

        static JsonObject Callback(string name, string returns, params JsonObject[] lanes) => new()
        {
            ["name"] = name,
            ["returns"] = returns,
            ["doc"] = null,
            ["lanes"] = new JsonArray(lanes.Cast<JsonNode>().ToArray()),
        };

        static ReturnTags Returning(string tags) => new(tags);

        static JsonObject ParamDoc(string name, string type, string tags, string doc)
        {
            var p = Param(name, type, tags);
            p["doc"] = doc;
            return p;
        }

        static JsonObject Param(string name, string type, string tags = "") => new()
        {
            ["name"] = name,
            ["type"] = type,
            ["tags"] = new JsonArray(tags.Length == 0 ? [] : tags.Split(',').Select(t => (JsonNode)t.Trim()).ToArray()),
            ["doc"] = null,
        };
    }

    sealed record ReturnTags(string Tags);
}
