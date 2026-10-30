# kabic

kabic enriches the semantics of C headers. It is not an application framework and knows nothing
about any project that uses it.

## What kabic is

The problem is one sentence: read C declarations and their doc tags, then describe them
idiomatically in a target language.

- **The C code already has semantics.** A `static const P pts[]` already says constant, array of
  `P`, with these values. What the clang AST carries is read, never restated by a tag.
- **Tags add only what C cannot say**: that a pointer is written back (`out`), that a pointer and a
  count are one sequence (`array_of`), that a value is borrowed or optional. A tag states a fact
  about a declaration. It never names a consumer, a use, or a mechanism of a binding.
- **Writers spell that semantics idiomatically** in their language. A writer holds no logic beyond
  how its language says the thing.
- **ClangSharp is the C# base.** It emits what the AST says, under names kabic remaps. kabic adds
  only what depends on enrichment; a `Render*` that adds nothing to ClangSharp's output must go.
- **A header that needs a tag to rebuild something is often the header to fix**: use `bool`, the
  enum type, a `ke_vec2`, the field's real name.
- **Anyone who wants additional logic uses an extension.** Extensions live with whoever needs them,
  outside this repository's core.

It must stay small. If kabic is growing, something is wrong.

## How to work here

**Think about the semantics first.** Before anything else ask what the declaration is, as any C
programmer would see it, with no project in mind. Then ask how another language says the same
thing; it is often one to one.

**Answer a need in this order:**

1. Is the generic output already enough? Accept it, even if it differs from what existed before.
2. If not, is the concept generic after all? Then it belongs in the core.
3. Only what is neither goes to an extension.

**The generic output is the goal, never a reproduction.** kabic grew wrong because it was shaped
to regenerate code that already existed. Matching an earlier result is not a requirement, and no
consumer's expected output is a reason to change kabic.

**Do not add:**

- a concept of a consumer in the core: a domain word, a field name, a type, a namespace;
- a tag for what the AST already says, or one with a value naming a use (no `[config:]`);
- runtime code held as strings in a generator;
- a discriminator based on a name suffix when the declaration can say it;
- anything to `Convention` other than spellings.
- a workaround for a design problem of a consumer (drains was one): fix the design there.

If a design needs one of these, the design is wrong; go back to the semantics.

## Layout

- `src/Kabic.Reader`: reads the clang AST and doc tags.
- `src/Kabic.Core`: the model, the slot classifier and the convention.
- `src/Kabic.CSharp`, `src/Kabic.Zig`: the writers.
- `src/Kabic.Pipeline`, `src/Kabic.Cli`, `src/Kabic`: orchestration and the command line.
- `src/Kabic.Bindings`: raw bindings through ClangSharp.
- `tests/Kabic.Tests`: shape tests of the writers.

## Spellings

`Convention` declares, per language, ordered regex name rules and a type map from C types to target
types. A rule carries no meaning; meaning comes from the code and its tags. `[owner]` marks the
struct owning a vtable, a factory is a function making an owner, `[bag]` marks a struct that has
function pointers and is no vtable.

## Commands

```bash
dotnet build kabic.slnx
dotnet test kabic.slnx
tests/e2e/run.sh    # generates a sample header and compiles the output
```

Tools run in the distrobox `dev`.

## Conventions

- No comments in source. A doc comment states what a caller relies on.
- Conventional Commits, one logical task per commit, staged selectively. No `Co-Authored-By`.
- Every commit builds alone.
- Answer the owner in Brazilian Portuguese; code and documents are in English.
- Status and plans live in `docs/kanban/`, which is not versioned.

## Annotations

A header that cannot be edited takes its tags from a YAML file named by the domain's `annotations`
key: `symbol: { member: tags }`, where a symbol is a function, a callback or `struct.slot`, and a
member is a parameter, a field or `return`. An unnamed parameter is `argN`, counted from one. An
inline function pointer is the callback `function_parameter`, with lanes `argN`. A key that matches
no declaration fails.
