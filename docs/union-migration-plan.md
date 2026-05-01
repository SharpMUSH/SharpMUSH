# OneOf → C# Native Union Migration Plan

## Background

C# 14 (Roslyn 5.7, shipped with .NET 11 Preview 3) includes a native `union` declaration
keyword. The BCL support types (`System.Runtime.CompilerServices.UnionAttribute` and
`System.Runtime.CompilerServices.IUnion`) are not yet in the .NET 11 Preview 3 runtime
assemblies, but the language spec explicitly states that users may define them locally.

This document plans the full migration away from `OneOf` (v3.0.271) and `OneOf.SourceGenerator`
to native C# union types.

---

## Current State

### Packages to remove (after migration)

| Project | Package |
|---|---|
| `SharpMUSH.Library` | `OneOf` + `OneOf.SourceGenerator` |
| `SharpMUSH.Database` | `OneOf` |
| `SharpMUSH.Implementation` | `OneOf` + `OneOf.SourceGenerator` |
| `SharpMUSH.Documentation` | `OneOf` |
| `SharpMUSH.Client` | `OneOf` |

### What needs to move

There are four distinct categories of `OneOf` usage across the codebase.

#### Category A — Named discriminated union types (13 files, `DiscriminatedUnions/`)

These are full class declarations that extend `OneOfBase<>`. Each becomes a named `union`
declaration.

| Current class | New declaration |
|---|---|
| `AnySharpObject` | `union AnySharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing)` |
| `AnySharpContainer` | `union AnySharpContainer(SharpPlayer, SharpRoom, SharpThing)` |
| `AnySharpContent` | `union AnySharpContent(SharpPlayer, SharpExit, SharpThing)` |
| `AnyOptionalSharpObject` | `union AnyOptionalSharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing, None)` |
| `AnyOptionalSharpContainer` | `union AnyOptionalSharpContainer(SharpPlayer, SharpRoom, SharpThing, None)` |
| `AnyOptionalSharpContent` | `union AnyOptionalSharpContent(SharpPlayer, SharpExit, SharpThing, None)` |
| `AnyOptionalSharpObjectOrError` | `union AnyOptionalSharpObjectOrError(SharpPlayer, SharpRoom, SharpExit, SharpThing, None, SharpError)` |
| `AnySharpObjectOrErrorCallState` | `union AnySharpObjectOrErrorCallState(AnySharpObject, SharpErrorCallState)` |
| `LazySharpAttributesOrError` | `union LazySharpAttributesOrError(IAsyncEnumerable<LazySharpAttribute>, SharpError)` |
| `OptionalLazySharpAttributeOrError` | `union OptionalLazySharpAttributeOrError(LazySharpAttribute[], None, SharpError)` |
| `OptionalSharpAttributeOrError` | `union OptionalSharpAttributeOrError(SharpAttribute[], None, SharpError)` |
| `SharpAttributesOrError` | `union SharpAttributesOrError(SharpAttribute[], SharpError)` |
| `Option<T>` | `union Option<T>(T, None)` |

#### Category B — Named inline union types outside `DiscriminatedUnions/`

These are classes or usages scattered in implementation/library code that were never moved to
the DU folder. Each becomes a named `union` declaration in an appropriate location.

| Current form | Declared in | New name |
|---|---|---|
| `class ChannelOrError : OneOfBase<SharpChannel, Error<CallState>>` | `ChannelHelper.cs` | `union ChannelOrError(SharpChannel, SharpErrorCallState)` |
| `class PrivilegeOrError : OneOfBase<string[], Error<string[]>>` | `ChannelHelper.cs` | `union PrivilegeOrError(string[], string[])` ⚠️ see note |
| `class ErrorOrMailList : OneOfBase<Error<string>, IAsyncEnumerable<SharpMail>>` | `MessageListHelper.cs` | `union ErrorOrMailList(SharpError, IAsyncEnumerable<SharpMail>)` |
| `class MailUpdate : OneOfBase<bool?, bool?, bool?, bool?>` | `SendMailCommand.cs` | ⚠️ cannot use `union` — see §Exceptions |

> **Note on `PrivilegeOrError`**: Both case types are `string[]`. `union` requires distinct
> types per case. Wrap in thin record structs: `record struct GrantedPrivileges(string[] Values)`
> and `record struct DeniedPrivileges(string[] Values)`.

#### Category C — Frequently used anonymous `OneOf<>` types

These are raw `OneOf<>` generic usages that appear throughout interfaces, services, queries,
and tests without ever being given a class name. Each needs a named union type.

| Anonymous form | Occurrences | Proposed named type |
|---|---|---|
| `OneOf<MString, string>` | ~200+ (tests + services) | `union SharpMessage(MString, string)` in `SharpMUSH.Library` |
| `OneOf<Success, Error<string>>` | ~10 (services + interfaces) | `union SharpResult(SharpSuccess, SharpError)` |
| `OneOf<DBRef, AnySharpObject>` | ~4 (queries) | `union DbRefOrObject(DBRef, AnySharpObject)` |
| `OneOf<DBRef, AnySharpContainer>` | ~4 (queries) | `union DbRefOrContainer(DBRef, AnySharpContainer)` |
| `OneOf<DBRef, string>` | ~3 (ArgHelpers, CommunicationService) | `union DbRefOrName(DBRef, string)` |
| `OneOf<string, DBRef>` | ~3 (TaskScheduler, ScheduleQuery) | use `DbRefOrName` (reverse order implicit, or add constructor) |
| `OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>` | ~3 (AttributeFunctions, ValidateService) | `union AttributeTarget(AnySharpObject, SharpAttributeEntry, SharpChannel, None)` |
| `OneOf<long, DBRef, DbRefAttribute>` | ~2 (ScheduleQuery) | `union SemaphoreTarget(long, DBRef, DbRefAttribute)` |
| `OneOf<string, None>` | ~2 (CryptoHelpers) | use `Option<string>` |
| `OneOf<WikiArticle, None>` | ~1 (WikiService) | use `Option<WikiArticle>` |
| `OneOf<Dictionary<string,string>, Error<string>>` | ~3 (Helpfiles) | `union HelpIndex(Dictionary<string,string>, SharpError)` |
| `OneOf<Dictionary<string,(long,long)>, Error<string>>` | ~1 (Helpfiles) | `union HelpIndexPositions(Dictionary<string,(long,long)>, SharpError)` |
| `OneOf<(string db, string Attribute), None>` | ~2 (HelperFunctions, AttributeFunctions) | replace with `record struct ObjAttrPair(string Db, string Attribute)` + `Option<ObjAttrPair>` |
| `OneOf<(string? db, string Attribute), bool>` | ~2 (HelperFunctions) | replace with `record struct OptionalObjAttr(string? Db, string Attribute)` + return as-is or use result type |
| `OneOf<(string db, string? Attribute), bool>` | ~2 (HelperFunctions) | similar record approach |
| `OneOf<IEnumerable<ConfigItem>, Error<string>>` | ~4 (AdminConfigService) | `union ConfigResult(IEnumerable<ConfigItem>, SharpError)` local to Client |

#### Category D — `OneOf.Types` replacements

`None`, `Error<T>`, and `Success` are used extensively from the `OneOf.Types` namespace.
These need local replacements that live in `SharpMUSH.Library`.

| OneOf type | Replacement |
|---|---|
| `None` | `record struct None;` |
| `Error<T>` | `record struct SharpError(string Value);` ¹ |
| `Error<CallState>` | `record struct SharpErrorCallState(CallState Value);` |
| `Error<string[]>` | `record struct SharpErrorList(string[] Values);` |
| `Success` | `record struct SharpSuccess;` |

> ¹ All existing `Error<string>` usages use `.Value` to get the string. `SharpError.Value`
> matches that call site exactly. Consider consolidating `SharpError` and `SharpErrorCallState`
> into a generic `SharpError<T>(T Value)` if preferred.

---

## What the Migration Does NOT Change

- `SharpPlayer`, `SharpRoom`, `SharpExit`, `SharpThing`, `SharpObject` — classes, unchanged
- All database and parser logic — only the union wrapper types change
- Test assertions — the test logic is unchanged; only `OneOf<MString, string>` type references
  become `SharpMessage` (a find-and-replace)

---

## Exceptions: Things That Cannot Become `union` Declarations

### `MailUpdate : OneOfBase<bool?, bool?, bool?, bool?>`

`union` requires each case type to be distinct. Having four `bool?` cases is not valid.
`MailUpdate` already wraps semantic meaning (Read/Clear/Tagged/Urgent edit) in positional index.

**Redesign to an enum + value pair:**

```csharp
public enum MailUpdateKind { Read, Clear, Tagged, Urgent }
public readonly record struct MailUpdate(MailUpdateKind Kind, bool? Value)
{
    public static MailUpdate ReadEdit(bool read)     => new(MailUpdateKind.Read,    read);
    public static MailUpdate ClearEdit(bool clear)   => new(MailUpdateKind.Clear,   clear);
    public static MailUpdate TaggedEdit(bool tagged) => new(MailUpdateKind.Tagged,  tagged);
    public static MailUpdate UrgentEdit(bool urgent) => new(MailUpdateKind.Urgent,  urgent);
}
```

All call sites use the static factory methods already, so the external API is unchanged.
Pattern matching becomes a switch on `Kind`.

---

## Syntax Reference

### BCL stubs (Step 1)

Create `SharpMUSH.Library/UnionSupport.cs`:

```csharp
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class UnionAttribute : Attribute { }

    public interface IUnion
    {
        object? Value { get; }
    }
}
```

This is the full required surface per the C# 14 spec resolution: *"users should provide them
explicitly, either by referencing assemblies or defining them locally."*

### Basic union declaration

```csharp
// Before
[GenerateOneOf]
public class AnySharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> input)
    : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing>(input) { ... }

// After
public union AnySharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing)
{
    // body members (methods, properties) go here
}
```

### Equality (AnySharpObject-specific)

`AnySharpObject` currently has custom `Equals`/`GetHashCode` by `DBRef`. Since union declarations
produce structs, implement `IEquatable<AnySharpObject>` explicitly in the body:

```csharp
public union AnySharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing)
{
    public bool Equals(AnySharpObject other) => this.Object().DBRef == other.Object().DBRef;
    public override int GetHashCode() => this.Object().DBRef.GetHashCode();
    public static bool operator ==(AnySharpObject a, AnySharpObject b) => a.Equals(b);
    public static bool operator !=(AnySharpObject a, AnySharpObject b) => !a.Equals(b);
}
```

### Pattern matching replaces `.Match()`

```csharp
// Before
var name = anySharpObject.Match(
    player => player.Object.Name,
    room    => room.Object.Name,
    exit    => exit.Object.Name,
    thing   => thing.Object.Name
);

// After
var name = anySharpObject switch
{
    SharpPlayer p => p.Object.Name,
    SharpRoom   r => r.Object.Name,
    SharpExit   e => e.Object.Name,
    SharpThing  t => t.Object.Name,
};
```

### `IsPlayer` / `AsPlayer` helpers

These move into the union body:

```csharp
public union AnySharpObject(SharpPlayer, SharpRoom, SharpExit, SharpThing)
{
    public bool IsPlayer => Value is SharpPlayer;
    public bool IsRoom   => Value is SharpRoom;
    public bool IsExit   => Value is SharpExit;
    public bool IsThing  => Value is SharpThing;

    public SharpPlayer AsPlayer => (SharpPlayer)Value!;
    public SharpRoom   AsRoom   => (SharpRoom)Value!;
    public SharpExit   AsExit   => (SharpExit)Value!;
    public SharpThing  AsThing  => (SharpThing)Value!;
}
```

### `FromT0` / `.IsT1` / `.AsT1` at call sites

```csharp
// Before
OneOf<DBRef, string>.FromT0(dbref)
OneOf<DBRef, string>.FromT1("name")

// After — implicit conversion handles this
DbRefOrName x = dbref;   // implicit union conversion
DbRefOrName y = "name";  // implicit union conversion
```

```csharp
// Before
if (result.IsT1) { var err = result.AsT1.Value; ... }

// After
if (result is SharpError err) { var msg = err.Value; ... }
```

### `SharpMessage` replaces `OneOf<MString, string>`

```csharp
// Before — service interface
ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, ...);

// After
ValueTask Notify(AnySharpObject who, SharpMessage what, ...);
```

```csharp
// Before — usage
await NotifyService.Notify(executor, someString);    // implicit
await NotifyService.Notify(executor, someMString);   // implicit
```

Both implicit conversions are synthesised by the union declaration, so call sites that pass
either a `string` or an `MString` directly require no change.

```csharp
// Before — test assertion
Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "..."))

// After
Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "..."))
```

`TestHelpers.MessagePlainTextEquals` signature changes from `OneOf<MString, string>` to
`SharpMessage` — a single-line change in `TestHelpers.cs` and `TestHelpers.cs` in
`SharpMUSH.Tests.Infrastructure`.

---

## Step-by-Step Migration Order

Steps are ordered to minimise broken-build time: each step keeps the build green before the next begins.

### Step 1 — Add BCL stubs and replacement primitive types

**Files to create:**
- `SharpMUSH.Library/UnionSupport.cs` — `UnionAttribute`, `IUnion`
- `SharpMUSH.Library/DiscriminatedUnions/Primitives.cs` — `None`, `SharpError`, `SharpErrorCallState`, `SharpSuccess`

**Files to change:** none yet  
**Verify:** `dotnet build` passes. No OneOf changes made yet.

---

### Step 2 — Replace the 13 named DU types in `DiscriminatedUnions/`

Replace each file one at a time. For each:

1. Rewrite the class as a `union` declaration with the same name and equivalent body members.
2. The constructor arguments, implicit operators, `.IsXxx`, `.AsXxx` properties all move into the union body.
3. Replace `new None()` constructions in the body with `default(None)` or `None default` parameter.
4. Replace `Error<string>` with `SharpError`, `Error<CallState>` with `SharpErrorCallState`.
5. Build and verify after each file.

**Recommended order** (by dependency, leaves first):

1. `Option.cs`
2. `SharpAttributesOrError.cs`
3. `LazySharpAttributesOrError.cs`
4. `OptionalSharpAttributeOrError.cs`
5. `OptionalLazySharpAttributeOrError.cs`
6. `AnySharpObject.cs`
7. `AnySharpContainer.cs`
8. `AnySharpContent.cs`
9. `AnyOptionalSharpObject.cs`
10. `AnyOptionalSharpContainer.cs`
11. `AnyOptionalSharpContent.cs`
12. `AnySharpObjectOrErrorCallState.cs`
13. `AnyOptionalSharpObjectOrError.cs`

**Key concern: `null` vs `None`**  
The current `AnyOptional*` types carry `None` as a case. Native union `null` handling per spec:
a `null` pattern match against a union checks if `Value is null`. Since `None` is a zero-size
struct that boxes to a non-null object, the semantics differ. Keep `None` as an explicit case
type rather than relying on `null`.

---

### Step 3 — Migrate `OneOfExtensions.cs`

`OneOfExtensions` contains static extension methods that call `.Match()` on the named DU types.
After Step 2 the named types no longer have `.Match()`. Convert each extension method body to a
`switch` expression.

Most of these methods are good candidates to move directly into the union body (Step 2 can
absorb them). Evaluate each:

- If the method only operates on its own type's members → move into union body
- If the method involves cross-type conversion (e.g. `WithRoomOption`, `WithNoneOption`) →
  keep as an extension method but update the switch syntax

**Files to change:** `SharpMUSH.Library/Extensions/OneOfExtensions.cs`  
Delete `using OneOf;` and `using OneOf.Types;`. Replace all `.Match(...)` with `switch`.

---

### Step 4 — Redesign `MailUpdate`

Replace `OneOfBase<bool?, bool?, bool?, bool?>` with the `enum + value` struct described in
§Exceptions. This is entirely self-contained in `SendMailCommand.cs`.

**Files to change:** `SharpMUSH.Library/Commands/Database/SendMailCommand.cs`  
**Impact:** Zero call-site changes (factory methods `ReadEdit`, `ClearEdit`, etc. are unchanged).
Internal `.Match()` in the database implementations becomes a `switch` on `Kind`.

---

### Step 5 — Replace `ChannelOrError`, `PrivilegeOrError`, `ErrorOrMailList`

Three inline OneOfBase subclasses outside the DU folder:

- `SharpMUSH.Implementation/Commands/ChannelCommand/ChannelHelper.cs`
- `SharpMUSH.Implementation/Commands/MailCommand/MessageListHelper.cs`

Rewrite each as a `union` declaration in-place. Introduce `GrantedPrivileges` /
`DeniedPrivileges` wrappers for `PrivilegeOrError`.

---

### Step 6 — Create `SharpMessage` and migrate `OneOf<MString, string>`

This is the highest-volume change by raw occurrence count (~200 test + service uses).

**Create:** `SharpMUSH.Library/DiscriminatedUnions/SharpMessage.cs`

```csharp
public union SharpMessage(MString, string)
{
    public MString AsMString => Value switch
    {
        MString m => m,
        string s  => MString.From(s),
        _         => throw new InvalidOperationException()
    };
}
```

**Batch find-and-replace** (one project at a time, build after each):

| Find | Replace |
|---|---|
| `OneOf<MString, string>` | `SharpMessage` |
| `OneOf.OneOf<MString, string>` | `SharpMessage` |
| `Arg.Is<OneOf<MString, string>>(` | `Arg.Is<SharpMessage>(` |
| `Arg.Is<OneOf.OneOf<MString, string>>(` | `Arg.Is<SharpMessage>(` |
| `Arg.Any<OneOf<MString, string>>()` | `Arg.Any<SharpMessage>()` |
| `Arg.Any<OneOf.OneOf<MString, string>>()` | `Arg.Any<SharpMessage>()` |
| `args[1] is OneOf<MString, string>` | `args[1] is SharpMessage` |
| `args[1] is not OneOf<MString, string>` | `args[1] is not SharpMessage` |

Update `TestHelpers.cs` in both `SharpMUSH.Tests` and `SharpMUSH.Tests.Infrastructure`:

```csharp
// Before
public static bool MessagePlainTextEquals(OneOf<MString, string> msg, string expected)

// After
public static bool MessagePlainTextEquals(SharpMessage msg, string expected)
```

---

### Step 7 — Create and migrate remaining anonymous `OneOf<>` types

Work through Category C in order of occurrence count (most-used first). For each:

1. Create the named union type in `SharpMUSH.Library/DiscriminatedUnions/` (or
   `SharpMUSH.Implementation/` if purely internal).
2. Find-and-replace the raw `OneOf<>` generic with the named type.
3. Replace `FromT0(...)` / `FromT1(...)` with direct assignment (implicit conversions).
4. Replace `.IsT0` / `.AsT0` with `is T0 x` pattern matching.
5. Build and verify.

**Order:**

1. `SharpResult` replacing `OneOf<Success, Error<string>>`
2. `DbRefOrObject` replacing `OneOf<DBRef, AnySharpObject>`
3. `DbRefOrContainer` replacing `OneOf<DBRef, AnySharpContainer>`
4. `DbRefOrName` replacing `OneOf<DBRef, string>` and `OneOf<string, DBRef>`
5. `AttributeTarget` replacing `OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None>`
6. `SemaphoreTarget` replacing `OneOf<long, DBRef, DbRefAttribute>`
7. `HelpIndex` / `HelpIndexPositions` replacing Helpfile result types
8. `ConfigResult` (local to `SharpMUSH.Client`) replacing AdminConfigService types
9. Replace tuple-returning unions in `HelperFunctions.cs` with named record structs + `Option<>`

**Note on `HelperFunctions.cs` tuple unions:**  
`SplitObjectAndAttr`, `SplitOptionalObjectAndAttr`, `SplitDbRefAndOptionalAttr` return
`OneOf<(tuple), bool/None>` combinations. These are helpers for parsing string input.
Consider replacing the return type with dedicated records and `Option<>`:

```csharp
// Before
public static OneOf<(string db, string Attribute), None> SplitObjectAndAttr(string objectAttr)

// After
public record struct ObjAttrPair(string Db, string Attribute);
public static Option<ObjAttrPair> SplitObjectAndAttr(string objectAttr)
```

---

### Step 8 — Remove `OneOf` package references

Once all usages in a project have been migrated, remove the `PackageReference` from its
`.csproj`. Remove in this order:

1. `SharpMUSH.Library` (last — it's the foundation)
2. `SharpMUSH.Implementation`
3. `SharpMUSH.Database`
4. `SharpMUSH.Documentation`
5. `SharpMUSH.Client`

The generated source file `SharpMUSH.Implementation/obj/.../GenerateOneOfAttribute.g.cs`
disappears automatically when the source generator is removed.

---

### Step 9 — Final verification

```bash
dotnet build
dotnet run --project SharpMUSH.Tests -- --output Detailed
dotnet run --project SharpMUSH.IntegrationTests -- --output Detailed
```

All 2,995 currently passing tests should still pass. The 205 currently skipped tests are
unaffected by this change.

---

## Risk Register

| Risk | Likelihood | Mitigation |
|---|---|---|
| BCL stub types change API in a later preview | Medium | `UnionSupport.cs` is one file; update when the official BCL ships |
| `None` boxing in `Value` field differs semantically from class-based `IsNone` check | Low | Keep `None` as explicit case type; `is None` pattern works correctly |
| `MailUpdate` redesign breaks database implementations | Low | Factory methods unchanged; internal `.Match()` → `switch on Kind` is mechanical |
| Struct copy semantics on `AnySharpObject` break ref-equality checks | Low | Custom `Equals`/`GetHashCode` on `DBRef` is preserved in union body |
| `PrivilegeOrError` wrapper records add friction at call sites | Low | Wrappers are thin; call sites simply use `new GrantedPrivileges(arr)` |
| Test mocking of `SharpMessage` (NSubstitute `Arg.Is<>`) differs | Low | `SharpMessage` is a struct; `Arg.Is<SharpMessage>` works identically |
| `IAsyncEnumerable<AnySharpObject>` with struct element type | Low | No boxing occurs; struct elements are stored inline in iterator state machine |

---

## File Change Summary

| Scope | Files affected | Nature of change |
|---|---|---|
| BCL stubs + primitives | 2 new files | New |
| `DiscriminatedUnions/` | 13 files | Rewrite |
| `Extensions/OneOfExtensions.cs` | 1 file | Rewrite |
| `Commands/Database/SendMailCommand.cs` | 1 file | Redesign |
| `Commands/ChannelCommand/ChannelHelper.cs` | 1 file | Rewrite |
| `Commands/MailCommand/MessageListHelper.cs` | 1 file | Rewrite |
| `DiscriminatedUnions/SharpMessage.cs` | 1 new file | New |
| New named union types (Category C) | ~10 new files | New |
| Library services + interfaces | ~15 files | Type rename + switch expressions |
| Implementation commands/functions | ~20 files | Type rename + switch expressions |
| Database implementations (3×) | ~27 files | `new None()` → `default(None)`, type renames |
| Test helpers (2 files) | 2 files | Signature change |
| Test files | ~40 files | `OneOf<MString,string>` → `SharpMessage` |
| `.csproj` files | 5 files | Remove `OneOf` packages |
| **Total** | **~140 files** | |

---

## Suggested PR Breakdown

| PR | Scope | Risk |
|---|---|---|
| PR 1 | Step 1: BCL stubs + primitive replacements | Zero — purely additive |
| PR 2 | Steps 2–3: Replace 13 named DU types + extensions | Low — isolated to Library |
| PR 3 | Step 4: `MailUpdate` redesign | Low — self-contained |
| PR 4 | Step 5: `ChannelOrError`, `PrivilegeOrError`, `ErrorOrMailList` | Low — self-contained |
| PR 5 | Step 6: `SharpMessage` + all `OneOf<MString,string>` sites | Medium — high volume, mechanical |
| PR 6 | Step 7: Remaining anonymous `OneOf<>` types | Medium — multiple files |
| PR 7 | Step 8: Remove `OneOf` package references | Low — final cleanup |

Each PR is independently buildable and testable. PRs 1–4 can be merged in sequence with
minimal review burden. PRs 5–6 are the most lines changed but are purely mechanical
find-and-replace with no logic changes.
