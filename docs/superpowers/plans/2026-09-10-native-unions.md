# OneOf → C# 15 native unions

The second half of #585: retire the `OneOf` / `OneOf.SourceGenerator` packages in favour of the
union types C# 15 ships with .NET 11. Stacked on #1028 (Target .NET 11 RC1).

## Facts verified on 11.0.100-rc.1.26425.128

- `union U(A, B);` compiles at the default language version; `UnionAttribute` and `IUnion` are in
  CoreLib. No stubs, no `LangVersion=preview`.
- A `union` declaration is **always a struct** (`{ object? Value; }`, boxes value-type cases).
  `default(U).Value` is null and switching on it throws `SwitchExpressionException`. No `==`;
  `Equals` compares `Value`.
- A **custom union** is any class or struct with `[Union]`, `: IUnion`, a public
  `object? Value { get; }` and one public single-parameter constructor per case type. A
  `[Union] sealed class` gets the same implicit conversions from each case type, the same
  exhaustive `switch` (no `_` arm needed), property patterns, and a `null =>` arm on a nullable
  reference.
- A partial union's other parts are written `partial union U` (no case list) — `partial struct U`
  is CS0261.
- Union conversions do not chain: `Outer o = cat;` is an error when `Cat` is a case of `Pet` and
  `Pet` a case of `Outer`.
- In **generic** code a `T t` arm matched against a `U<T>` instance is CS8780. Match `u.Value`
  instead (`u.Value switch { T t => … }`). Concrete instantiations (`Found<WikiPage>`) match
  `WikiPage page` directly.
- A union switch warns CS8655 when a case type's null state is "maybe null" (unconstrained `T`,
  `string?`, `bool?`), so such unions need a `null` arm or a `notnull` constraint.

## Mapping rule: keep today's value/reference semantics

| Today | Becomes | Why |
|---|---|---|
| inline `OneOf<A, B, …>` (a struct) | a `union` declaration (a struct) | same semantics, same default-value hazards |
| `class X : OneOfBase<…>` | `[Union] public sealed partial class X : IUnion` | these are passed as `X?` everywhere; a struct would turn every `X?` into `Nullable<X>`, where `.Value` means something else |

Case types keep **OneOf's order**. The compat surface below indexes by position (`IsT0`), so a
union that reorders cases silently inverts every call site. Where the same types appear in both
orders (`OneOf<string, DBRef>` and `OneOf<DBRef, string>`), stage 1 keeps two unions; stage 2 may
unify them while rewriting the call sites.

`OneOf<bool?, bool?, bool?, bool?>` (`MailUpdate`) cannot survive: union cases must be
distinguishable by type. It becomes four record-struct cases (`ReadEdit(bool)`, …).

## Where things live

- **SharpMUSH.Contracts**, namespace `SharpMUSH.Library.DiscriminatedUnions` (Contracts keeps
  `SharpMUSH.Library.*` namespaces by design; both Library and the WASM Client see it):
  - the OneOf.Types replacements, as `readonly record struct`s with the same names and shapes:
    `None`, `NotFound`, `Success`, `Error`, `Error<T>(T Value)`, and any other OneOf.Types member
    the compiler asks for;
  - the shared generic families:
    - `Result<T>(T, Error<string>)` — every `OneOf<T, Error<string>>`, including
      `OneOf<Success, Error<string>>` → `Result<Success>`;
    - `Found<T>(T, NotFound)` — every `OneOf<T, NotFound>`, including `OneOf<None, NotFound>` →
      `Found<None>`;
    - `FoundResult<T>(T, NotFound, Error<string>)`.
- **SharpMUSH.Library**: `Option<T>` and the domain unions (`AnySharpObject`, `AnySharpContainer`,
  `AnySharpContent`, the `AnyOptional*` family, the `*OrError` family, `ChannelCreationResult`,
  `MailUpdate`), plus Library-only shapes: `SharpMessage(MString, string)` for the 470-odd
  `OneOf<MString, string>`, `DbRefOrName`, `NameOrDbRef`, `DbRefOrContainer`, `DbRefOrObject`,
  `HelpResolution(HelpEntry, HelpCandidates, None)`, …
- **Project-local shapes stay in their project**: Client (`ApiResult<T>(T, ApiFailure)`,
  `OneOf<X, string>` error-message results), Plugins.Scene, Server, Implementation
  (`ChannelOrError`, `PrivilegeOrError`, `ErrorOrMailList`).

Name a union for what it means (`HelpResolution`, `LockEvaluation`), not for its cases, unless the
cases are all it means (`DbRefOrName`). Reuse a generic family before declaring a new type.

## Stage 1 — swap the types, keep the call sites (one agent, serial)

The whole solution must build and pass at the end of this stage with call sites changed only as far
as the type swap forces.

1. Primitives and generic families in Contracts; domain unions in Library as `[Union]` classes,
   carrying the members their `OneOfBase` versions had (`Where`, `Aliases`, `MinusExit`, `IsPlayer`
   / `AsPlayer`, `RefOf`, `TryFromNode`, …) rewritten over `Value`. Equality and hash code must
   behave exactly as they do today — read OneOf 3.0.271's `OneOfBase.Equals` before deciding what
   "today" is.
2. **The compat surface**, generated, one file per union under a `Compat/` folder in the declaring
   project, as a second `partial` part: `IsT{i}`, `AsT{i}`, `Match<TResult>(Func<T0,TResult>, …)`,
   `Switch(Action<T0>, …)`, `static FromT{i}(T{i})`. Every member reads `Value`; `AsT{i}` throws
   `InvalidOperationException` on the wrong case. This is scaffolding: stage 2 deletes the folders,
   and anything still calling it stops compiling.
3. Replace every inline `OneOf<…>` in signatures, locals, NSubstitute `Arg.Is<…>`/`Arg.Any<…>` with
   its union (script the textual replacement from the shape table; whitespace-normalise; handle
   `OneOf.OneOf<…>`). `using OneOf; using OneOf.Types;` becomes
   `using SharpMUSH.Library.DiscriminatedUnions;`.
4. Rewrite directly, without compat: the 50 `TryPickT{i}` calls (nearly all `out _`; the help
   resolution chain uses the remainder), `.Index`, `[GenerateOneOf]`, `OneOfExtensions`
   (retarget to the unions, as members where the method is intrinsic to the type).
5. Remove both OneOf packages from every csproj and `Directory.*`.
6. Verify: `dotnet build SharpMUSH.sln` 0 warnings/0 errors; SharpMUSH.Tests, Tests.BUnit,
   Tests.ScenePlugin, Tests.Integration at the #1028 baseline (9536 / 667 / 42 / 442; the one
   integration flake in `AdminAccountsApiTests` is known and unrelated). A null case value is the
   likeliest behaviour change — OneOf reported `IsT1` for a null `T1`, a union's `Value` is just
   null — so any new failure is investigated, never papered over.
7. Commit per coherent step; the last commit of the stage builds and passes.

## Stage 2 — idiomatic call sites (parallel agents, one project each)

With the solution green on the compat surface, each agent owns one project's files (Library,
Implementation, SurrealDB + Lightning, Server, Client, Plugins.Scene, Tests split by folder,
Tests.Integration + Infrastructure + BUnit + ScenePlugin), works in its own worktree cut from the
stage-1 head, and rewrites its call sites:

- `if (x.IsT0) … x.AsT0 …` → `if (x is T0 t0) … t0 …`; `x.IsT1 ? … : …` flows become patterns.
- `x.Match(a => A, b => B)` → `x switch { T0 a => A, T1 b => B }`. Async arms: switch to the task
  and await it, or a switch statement when arms differ in shape.
- `x.Switch(…)` → a switch statement.
- `U.FromT0(v)` → the implicit conversion, or `new U(v)` where target typing cannot reach.
- Domain unions keep their named members (`IsPlayer`, `AsPlayer`, `Object()`, `Known`); positional
  `IsT3`/`AsT3` on them becomes the named member or a pattern.
- No `_ =>` arm on a switch the compiler already knows is exhaustive; a `null` arm only where the
  value can really be null.

Each agent builds its project, runs the tests that cover it, and commits once, in the foreground.
Integration is by cherry-pick in dependency order. The final commit deletes every `Compat/` folder
and fixes whatever that exposes; then the full suite runs again against the stage-1 baseline.

## Plugin contract

`Option<CallState>`, `CallState` and the parser interfaces are the plugin ABI. `Option<T>` changing
from a `OneOfBase` subclass to a `[Union]` class is a breaking change for compiled plugins: bump
`PluginContractVersion` and the SharpMUSH.Library / Implementation.Generated package major version
in the same PR, and rebuild the in-repo plugins and fixtures.
