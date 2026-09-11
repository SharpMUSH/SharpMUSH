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

## Stage 1 result: shape → union table

What stage 1 actually declared, for stage 2 to rely on. Case order is OneOf's order throughout, so
`IsT{n}`/`AsT{n}` in the compat surface mean what they meant before. "struct" is a `union`
declaration; "class" is a `[Union] sealed partial class : IUnion` with a constructor per case, a
`Value` property and `Equals`/`GetHashCode` over `Value`.

### Case primitives — SharpMUSH.Contracts, `SharpMUSH.Library.DiscriminatedUnions`

`None`, `NotFound`, `Success`, `Error` and `Error<T>(T Value)`, as `readonly record struct`s in
`SharpMUSH.Contracts/DiscriminatedUnions/`. No other OneOf.Types member was used.

### Generic families (struct)

| Family | Cases | Declared in | Replaces |
|---|---|---|---|
| `Result<T>` | `T`, `Error<string>` | Contracts | every `OneOf<X, Error<string>>` — including `Success`, `string`, `long`, interfaces (`IReadOnlyList<string>`, `IManagedPackageBinarySource`, `IEnumerable<ConfigItem>`), tuples (`(string PageTarget, string Locale)`), `Dictionary<…>` and the generic `T` in `GitPackageSourceService` |
| `Found<T>` | `T`, `NotFound` | Contracts | every `OneOf<X, NotFound>` — including `None` (and its `OkNone` alias), `byte[]`, `IReadOnlyList<…>`, `(WikiAsset Asset, Stream Content)` |
| `FoundResult<T>` | `T`, `NotFound`, `Error<string>` | Contracts | `OneOf<ScenePose, NotFound, Error<string>>` |
| `PackageManifestResult<T>` | `T`, `PackageManifestFailure` | Library | `OneOf<ParsedPackageManifest \| PackageIndex \| CommunityRepoListing, PackageManifestFailure>` |
| `DiagnosticsResult<T>` | `T`, `DiagnosticsError` | Library | `OneOf<QueueDiagnosticsReport \| Guid \| Success, DiagnosticsError>` |
| `ApiResult<T>` | `T`, `ApiFailure` | Client (`Services/`) | every `OneOf<X, ApiFailure>` |
| `MessageResult<T>` | `T`, `string` (the message to show) | Client (`Services/`) | `OneOf<WikiArticle \| WikiBatchResult \| UploadedAssetInfo \| None, string>` |
| `Maybe<T>` | `T`, `None` | Client (`Services/`) | `OneOf<WikiArticle \| WikiRevisionInfo, None>` |
| `ServerResult<T>` | `T`, `Error` | Client (`Services/`) | `OneOf<bool, Error>`, `OneOf<IReadOnlyList<CharacterSummary>, Error>` (two different nested `CharacterSummary` types, hence generic) |
| `ValueOrResponse<T>` | `T`, `ActionResult` | Server (`Controllers/`) | `OneOf<PackageManifestSource, ActionResult>` and the manifest tuple |

### Former `OneOfBase` subclasses (class)

| Union | Cases | Declared in |
|---|---|---|
| `AnySharpObject` | `SharpPlayer`, `SharpRoom`, `SharpExit`, `SharpThing` | Library `DiscriminatedUnions/` |
| `AnySharpContainer` | `SharpPlayer`, `SharpRoom`, `SharpThing` | Library |
| `AnySharpContent` | `SharpPlayer`, `SharpExit`, `SharpThing` | Library |
| `AnyOptionalSharpObject` | `SharpPlayer`, `SharpRoom`, `SharpExit`, `SharpThing`, `None` | Library |
| `AnyOptionalSharpContainer` | `SharpPlayer`, `SharpRoom`, `SharpThing`, `None` | Library |
| `AnyOptionalSharpContent` | `SharpPlayer`, `SharpExit`, `SharpThing`, `None` | Library |
| `AnyOptionalSharpObjectOrError` | `SharpPlayer`, `SharpRoom`, `SharpExit`, `SharpThing`, `None`, `Error<string>` | Library |
| `AnySharpObjectOrErrorCallState` | `AnySharpObject`, `Error<CallState>` | Library |
| `ChannelCreationResult` | `Success`, `ChannelNameTaken`, `Error<string>` | Library |
| `LazySharpAttributesOrError` | `IAsyncEnumerable<LazySharpAttribute>`, `Error<string>` | Library |
| `OptionalLazySharpAttributeOrError` | `LazySharpAttribute[]`, `None`, `Error<string>` | Library |
| `OptionalSharpAttributeOrError` | `SharpAttribute[]`, `None`, `Error<string>` | Library |
| `SharpAttributesOrError` | `SharpAttribute[]`, `Error<string>` | Library |
| `Option<T>` | `T`, `None` | Library (plugin contract) |
| `MailUpdate` | `MailUpdate.Read`, `.Cleared`, `.Tagged`, `.Urgent` (each `(bool Value)`) | Library `Commands/Database/SendMailCommand.cs`; no compat part |
| `ChannelOrError` | `SharpChannel`, `Error<CallState>` | Implementation `Commands/ChannelCommand/ChannelHelper.cs` |
| `PrivilegeOrError` | `string[]`, `Error<string[]>` | Implementation `Commands/ChannelCommand/ChannelHelper.cs` |
| `ErrorOrMailList` | `Error<string>`, `IAsyncEnumerable<SharpMail>` | Implementation `Commands/MailCommand/MessageListHelper.cs` |

The helpers that were extension methods in `OneOfExtensions` (`Object()`, `Id()`, `WithNoneOption()`,
`WithErrorOption()`, `WithoutNone()`, `WithoutError()`, `IsValid()`) are members of these classes
now. `Known()`, `IsNone()` and `IsError()` duplicate a property of the same name, so they stay in
`Library/Extensions/ObjectUnionExtensions.cs`; stage 2 can switch their callers to the properties and
delete the file.

### Named inline shapes (struct)

| Shape | Union | Declared in |
|---|---|---|
| `OneOf<MString, string>` (also written `MarkupText`) | `SharpMessage` | Library |
| `OneOf<string, DBRef>` | `NameOrDbRef` | Library |
| `OneOf<DBRef, string>` | `DbRefOrName` | Library |
| `OneOf<DBRef, AnySharpContainer>` | `DbRefOrContainer` | Library |
| `OneOf<DBRef, AnySharpObject>` | `DbRefOrObject` | Library |
| `OneOf<HelpEntry, HelpCandidates, None>` | `HelpResolution` | Library |
| `OneOf<string, LockEvaluationFailure>` | `LockEvaluation` | Library |
| `OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>` | `ValidationTarget` | Library |
| `OneOf<AnySharpObject, DeliveryFailure>` | `DeliveryResult` | Library |
| `OneOf<WikiTranslation, WikiWriteConflict, Error<string>>` | `TranslationWriteResult` | Library |
| `OneOf<long, DBRef, DbRefAttribute>` | `SemaphoreTarget` | Library |
| `OneOf<(string db, string Attribute), None>` | none: `ObjectAttribute?` (`readonly record struct (string Object, string Attribute)`) | Library `Models/` |
| `OneOf<(string? db, string Attribute), bool>` | none: `AttributeWithOptionalObject?` (`readonly record struct (string? Object, string Attribute)`) | Library `Models/` |
| `OneOf<(string db, string? Attribute), bool>` | none: `ObjectWithOptionalAttribute?` (`readonly record struct (string Object, string? Attribute)`) | Library `Models/` |
| `OneOf<string, NotFound, Error>` | `ObjidResolution` | Client `Services/` |
| `OneOf<WikiTranslationInfo, WikiTranslationSaveError>` | `TranslationSaveResult` | Client `Services/` |
| `OneOf<RecallWindow, CallState>` | `RecallSelection` | Implementation `Commands/ChannelCommand/` |
| `OneOf<string, None>` | `DigestResult` | Implementation `Common/` |
| `OneOf<AnySharpContainer, ExitDestinationFailure>` | `ExitDestination` | Implementation `Commands/ExitDestination.cs`, nested private in `Commands` (its failure enum is private) |

`OneOf<SharpObject, None>` and `OneOf<SharpPlayer, SharpExit, SharpThing, None>` only appeared in
dead extension methods, which were deleted.

### Compat surface

One generated file per union, in the `Compat/` folder next to its declaration:
`SharpMUSH.Contracts/DiscriminatedUnions/Compat/` (3), `SharpMUSH.Library/DiscriminatedUnions/Compat/`
(30), `SharpMUSH.Client/Services/Compat/` (6), `SharpMUSH.Server/Controllers/Compat/` (1), and in
Implementation `Commands/Compat/`, `Commands/ChannelCommand/Compat/` (3), `Commands/MailCommand/Compat/`
and `Common/Compat/`. The generator is not in the repository; nothing needs regenerating, only
deleting. No union has `TryPickT{n}` or `Index`.

Remaining positional call sites at the end of stage 1 (counted by marking every compat member
`[Obsolete]` and building; `tools` is `tools/PackageValidator`):

| Project | IsT | AsT | Match | Switch | FromT | Total |
|---|---:|---:|---:|---:|---:|---:|
| SharpMUSH.Client | 8 | 11 | 11 | 9 | 22 | 61 |
| SharpMUSH.Database | 0 | 0 | 1 | 0 | 0 | 1 |
| SharpMUSH.Database.Lightning | 2 | 4 | 1 | 0 | 0 | 7 |
| SharpMUSH.Database.SurrealDB | 14 | 9 | 10 | 0 | 0 | 33 |
| SharpMUSH.Documentation | 1 | 2 | 0 | 0 | 0 | 3 |
| SharpMUSH.Implementation | 141 | 195 | 55 | 0 | 2 | 393 |
| SharpMUSH.Library | 51 | 48 | 26 | 1 | 3 | 129 |
| SharpMUSH.PackageTool | 0 | 0 | 1 | 0 | 0 | 1 |
| SharpMUSH.Plugins.Scene | 67 | 43 | 21 | 0 | 10 | 141 |
| SharpMUSH.Server | 57 | 90 | 42 | 7 | 11 | 207 |
| SharpMUSH.Tests | 496 | 564 | 81 | 0 | 0 | 1141 |
| SharpMUSH.Tests.BUnit | 19 | 17 | 3 | 0 | 0 | 39 |
| SharpMUSH.Tests.Infrastructure | 0 | 0 | 6 | 0 | 0 | 6 |
| SharpMUSH.Tests.Integration | 91 | 84 | 7 | 0 | 0 | 182 |
| SharpMUSH.Tests.ScenePlugin | 4 | 26 | 0 | 0 | 2 | 32 |
| tools | 0 | 0 | 0 | 3 | 0 | 3 |
| **Total** | 951 | 1093 | 265 | 20 | 50 | 2379 |

### Things stage 2 will trip over

- **Tuple cases cannot be named in a type pattern.** `x is (string db, string? Attribute) d` parses as a
  positional pattern, and `ValueTuple<string, string?>` loses the element names. The three
  object/attribute splitters (`SplitObjectAndAttr`, `SplitOptionalObjectAndAttr`,
  `SplitDbRefAndOptionalAttr`) were unions of a tuple and a failure case that carried nothing
  (`None`, or a `bool` that was always `false`), so they are no longer unions: each returns a
  nullable record struct from the rows above, `null` meaning "not a spec of that shape", and callers
  bind the halves with a property pattern
  (`if (HelperFunctions.SplitDbRefAndOptionalAttr(text) is not { Object: var obj, Attribute: var attr })`).
  Two tuple cases remain: `Result<(string PageTarget, string Locale)>` (`WikiCommandHelper.SplitLocaleTarget`)
  and `ValueOrResponse<(PackageManifest Manifest, …)>` (`PackagesController`); give each a named record
  struct the same way.
- **A switch statement is never exhaustive** for definite-return analysis: a method whose every path
  returns from `switch (union) { case A … case B … }` still fails CS0161. Use a switch expression, or
  return after the switch.
- **`x is null` on a class union tests `Value`**, not the reference (`== null` tests the reference). The
  domain unions never hold a null value, so the two agree today.
- **ToString** is the default (type name) on every union; OneOf printed `"<case type>: <value>"`.
  Nothing left depends on either.
- **Null in a case**: a union reports no case at all for a null value, where OneOf reported the index
  it was constructed with. No case type is nullable and no test hit it.
- The in-repo plugin fixtures and `SharpMUSH.Plugins.Scene` reference `SharpMUSH.Contracts` directly
  (a ProjectReference to Library does not flow it), and `PluginLoaderService.SharedContractTypes`
  lists `None` so plugins share the host's copy.
- **Baseline flakes.** `InputSessionCommandTests.CallbackQRegistersAreUsableAndFreshForEveryReply`,
  `TimeoutWorkerEndsCaptureAndRunsItsAdmittedCallback` and
  `PasteLinesRemainCapturedAndCallbackCanEndTheSession` fail intermittently in full SharpMUSH.Tests
  runs on the parent commit as well (two of two runs there), and pass when the class runs alone.
  They are not a union regression; rerun the class alone before chasing one.

## Stage 2 result

Six agents rewrote every positional call site to patterns (≈2,400, counted by marking the compat
members `[Obsolete]` and building), and the `Compat/` folders and `ObjectUnionExtensions` were then
deleted; the solution builds without them. Two conventions came out of it:

- **No cast to reach a case, and no helper that stands in for one.** C# does not narrow a union after
  `if (x is not T value)`, and has no let-else, so an early return that needs the failure is written
  as *switch and extract*: match the result once, where it is produced, with an exhaustive switch; the
  failure arms produce the response and the success arm calls a method holding the rest of the work
  (`RegisteredAsync(account)`, `DispatchInternalCommand(…)`). A switch *statement* is used only where a
  case must return from inside a loop or partway through a method. No union carries a two-out
  `TryGetValue(out value, out failure)`: `TryGetValue(out T)` is the compiler's own non-boxing union
  access pattern, and the docs consume unions with `switch` and `is`.
- **Tests bind the expected case with `Expect<T>()`** (`SharpMUSH.Tests.Infrastructure/UnionExpectations.cs`),
  which fails naming the case it got. NSubstitute matchers are expression trees and cannot hold a
  pattern; they call `TestHelpers.MessageIsString` / `MessageIsMarkup` / `MessagePlainText*`.

The last tuple case became `OpenedWikiAsset`; `WikiCommandHelper.LocaleTarget` replaced the other.
