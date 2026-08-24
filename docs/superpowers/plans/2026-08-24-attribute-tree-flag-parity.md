# Attribute-Tree Flag Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring SharpMUSH's attribute-tree flag propagation to parity with PennMUSH, closing a disclosure leak where a `mortal_dark` branch fails to hide its leaf.

**Architecture:** PennMUSH re-walks an attribute's `` ` ``-separated ancestor path on every access through three independent gates (read, write, command/inherit). SharpMUSH's exact-path API already does this; the pattern-matching path does not, because it builds prefix sets from query results that may not contain the ancestor. The fix assembles the real ancestor path per match — from the result set where present, via a cached query where absent — and hands it to the existing permission checks.

**Tech Stack:** .NET 10, C#, TUnit, ArangoDB / SurrealDB / Memgraph, source-generated Mediator.

**Spec:** `docs/superpowers/specs/2026-08-24-attribute-tree-flag-parity-design.md`

## Global Constraints

- **Parity target is PennMUSH's C source, not its help text.** They disagree on four flags. `no_clone` and `veiled` do **not** propagate; `wizard` gates writes only, not reads; `debug` never propagates. Penn source lives at `/home/grave/RiderProjects/SharpMUSH/pennmush` — **read-only, never modify it.**
- **Never store computed inherited flags on a leaf.** Penn doesn't. `@examine`, `flags()`, and `@decompile` must keep showing an attribute's own flags only.
- **`Inheritable` is the object-parent axis.** It must not be consulted for tree descent. Every propagation site gets a comment naming which gate it serves and which Penn function it mirrors.
- C# style: **tabs**, indent size 2. On `FORMAT001`, run `dotnet format whitespace --folder <dir> --exclude "**/bin/**" --exclude "**/obj/**"` **twice** (it needs two passes to converge).
- **Never `git add -A`.** Stage only your own paths.
- `TreatWarningsAsErrors` is on in `SharpMUSH.Library`, `SharpMUSH.Implementation`, `SharpMUSH.Tests`.
- TUnit, not xUnit. `HasCount().EqualTo(n)` is `[Obsolete]` and fails the build — use `Count().IsEqualTo(n)`.
- **Fail-first is mandatory on every task.** Six unfalsifiable tests were caught on the predecessor branch. Confirm each new test goes red without its fix and record which.
- Tests using `NotifyService` (a `SharedType.PerTestSession` substitute) need `ClearReceivedCalls()` after setup and immediately before the command under test, plus bare `[NotInParallel]` on the class.

---

### Task 1: Ancestor path assembly

**Files:**
- Create: `SharpMUSH.Library/Services/AttributeAncestry.cs`
- Test: `SharpMUSH.Tests/Services/AttributeAncestryTests.cs`

**Interfaces:**
- Produces:

```csharp
internal static class AttributeAncestry
{
	/// Returns the root..leaf path for <paramref name="leaf"/>.
	/// Ancestors present in <paramref name="known"/> are taken from there;
	/// absent ones are fetched via <paramref name="fetch"/>, which receives the
	/// split path and returns null when no such attribute exists.
	public static ValueTask<SharpAttribute[]> PathAsync(
		SharpAttribute leaf,
		IReadOnlyDictionary<string, SharpAttribute> known,
		Func<string[], ValueTask<SharpAttribute?>> fetch);
}
```

Taking `fetch` as a delegate rather than an `IMediator` keeps this unit-testable with no database and no mediator, which is what makes the fail-first cycle cheap.

- [ ] **Step 1: Write the failing tests**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class AttributeAncestryTests
{
	private static SharpAttribute Attr(string longName) => TestAttributeFactory.Named(longName);

	private static Func<string[], ValueTask<SharpAttribute?>> NeverFetches()
		=> _ => throw new InvalidOperationException("fetch should not have been called");

	private static Func<string[], ValueTask<SharpAttribute?>> FetchesNothing()
		=> _ => ValueTask.FromResult<SharpAttribute?>(null);

	[Test]
	public async Task TopLevelAttribute_HasOnlyItself()
	{
		var leaf = Attr("FOO");
		var path = await AttributeAncestry.PathAsync(leaf, new Dictionary<string, SharpAttribute>(), NeverFetches());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO" });
	}

	[Test]
	public async Task AncestorsPresentInKnown_AreNotFetched()
	{
		var branch = Attr("FOO");
		var leaf = Attr("FOO`BAR");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase)
		{
			["FOO"] = branch, ["FOO`BAR"] = leaf
		};

		var path = await AttributeAncestry.PathAsync(leaf, known, NeverFetches());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR" });
	}

	[Test]
	public async Task AbsentAncestor_IsFetched()
	{
		var leaf = Attr("FOO`BAR");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase) { ["FOO`BAR"] = leaf };
		var fetched = new List<string>();

		var path = await AttributeAncestry.PathAsync(leaf, known, parts =>
		{
			fetched.Add(string.Join('`', parts));
			return ValueTask.FromResult<SharpAttribute?>(Attr(string.Join('`', parts)));
		});

		await Assert.That(fetched).IsEquivalentTo(new[] { "FOO" });
		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR" });
	}

	[Test]
	public async Task DeepPath_IsRootToLeafInOrder()
	{
		var leaf = Attr("A`B`C`D");
		var path = await AttributeAncestry.PathAsync(leaf, new Dictionary<string, SharpAttribute>(),
			parts => ValueTask.FromResult<SharpAttribute?>(Attr(string.Join('`', parts))));

		await Assert.That(path.Select(a => a.LongName).ToArray())
			.IsEquivalentTo(new[] { "A", "A`B", "A`B`C", "A`B`C`D" });
	}

	[Test]
	public async Task OrphanedLeaf_OmitsMissingAncestorRatherThanFailing()
	{
		// A leaf whose branch node genuinely does not exist means "no ancestor",
		// NOT a denial. Penn treats a missing branch as absent, not as forbidding.
		var leaf = Attr("GONE`LEAF");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase) { ["GONE`LEAF"] = leaf };

		var path = await AttributeAncestry.PathAsync(leaf, known, FetchesNothing());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "GONE`LEAF" });
	}

	[Test]
	public async Task LookupIsCaseInsensitive()
	{
		var leaf = Attr("Foo`Bar");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase)
		{
			["FOO"] = Attr("FOO"), ["Foo`Bar"] = leaf
		};

		var path = await AttributeAncestry.PathAsync(leaf, known, NeverFetches());

		await Assert.That(path).Count().IsEqualTo(2);
	}
}
```

`TestAttributeFactory.Named` does not exist. Create it in the same test file (or a small helper alongside) building a minimal `SharpAttribute` with the given `LongName` and no flags — read `SharpMUSH.Library/Models/SharpAttribute.cs` for its required members first; it is a positional record, so a parameterless initialiser will not compile.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeAncestryTests/*"`
Expected: compile failure — `AttributeAncestry` is not defined.

- [ ] **Step 3: Implement**

Create `SharpMUSH.Library/Services/AttributeAncestry.cs`. Split `leaf.LongName` on `` ` ``; for each prefix from shortest to longest, take the attribute from `known` if present, otherwise call `fetch` with the split prefix; skip any prefix that resolves to null; return the assembled array root-first with the leaf last.

Use `StringComparer.OrdinalIgnoreCase` for `known` lookups — attribute names are case-insensitive throughout this codebase.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeAncestryTests/*"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/AttributeAncestry.cs SharpMUSH.Tests/Services/AttributeAncestryTests.cs
git commit -m "Add ancestor path assembly for attribute permission gating"
```

---

### Task 2: Close the pattern-path disclosure leak

**Files:**
- Modify: `SharpMUSH.Library/Services/AttributeService.cs:528-568` (`GetAttributePatternAsync`), `:598-617` (`FilterLazyAttributes`)
- Test: `SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs`

**Interfaces:**
- Consumes: `AttributeAncestry.PathAsync` (Task 1).

**This is the security fix.** Land it before the cosmetic parity work.

- [ ] **Step 1: Write the failing test**

Fixture shape copied from `SharpMUSH.Tests/Commands/AttributeTreePermissionTests.cs`. The critical difference from the existing tests: the pattern must match **only the leaf**, and for the visual case the viewer must **not** own the target.

```csharp
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A mortal_dark branch must hide its leaves even when the pattern names only
/// the leaf. The pre-existing MortalDark_HidesFromLattrForMortal passes only
/// because lattr(me/**) happens to pull the ancestor into the result set, which
/// is what populated darkPrefixes; a leaf-only pattern left it empty.
/// </summary>
public class AttributeTreePatternVisibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask MortalDarkBranch_HidesLeaf_WhenPatternNamesOnlyTheLeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatDarkOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatDarkViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PD{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PD{uid}`LEAF me=leafvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PD{uid}=mortal_dark"));

		// The leaf-only pattern: the branch is NOT in the result set.
		var result = await Parser.FunctionParse(MModule.single($"lattr({ownerDbRef}/PD{uid}`LEAF)"));

		await Assert.That(result!.Message!.ToPlainText()).DoesNotContain($"PD{uid}`LEAF");
	}

	[Test]
	public async ValueTask MortalDarkBranch_HidesLeaf_FromGetThroughLeafOnlyPattern()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatDarkGet");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PG{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PG{uid}`LEAF me=leafvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PG{uid}=mortal_dark"));

		var result = await Parser.FunctionParse(MModule.single($"xattr({ownerDbRef}/PG{uid}`LEAF,0,10)"));

		await Assert.That(result!.Message!.ToPlainText()).DoesNotContain($"PG{uid}`LEAF");
	}

	[Test]
	public async ValueTask VisualLeaf_UnderNonVisualBranch_IsNotReadable()
	{
		// Penn requires AF_VISUAL on EVERY level. The viewer must not own or
		// control the target, or CanExamine short-circuits and the All(IsVisual)
		// grant branch never executes — which is why every pre-existing test in
		// AttributeTreePermissionTests fails to exercise this at all.
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatVisOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatVisViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PV{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PV{uid}`LEAF me=leafvalue"));
		// Leaf is visual; branch deliberately is not.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PV{uid}`LEAF=visual"));

		var result = await Parser.FunctionParse(MModule.single($"get({ownerDbRef}/PV{uid}`LEAF)"));

		await Assert.That(result!.Message!.ToPlainText()).DoesNotContain("leafvalue");
	}
}
```

Two things to verify while writing these rather than assuming:
- `FunctionParse` runs as whichever executor the parser is bound to. The visual test needs the **viewer**, not the owner, to be the executor — check how `AttributeTreeParentPermissionTests` switches executor and mirror it. If `FunctionParse` cannot take an executor, drive it through `CommandParse` with `think` on the viewer's handle instead.
- Confirm `xattr`'s argument order against `AttributeFunctions.cs` before relying on it; substitute `lattr` if simpler.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeTreePatternVisibilityTests/*"`
Expected: FAIL — the leaf is currently visible.

- [ ] **Step 3: Implement**

In both `GetAttributePatternAsync` and `FilterLazyAttributes`, replace the `darkPrefixes` filter and the single-attribute `CanViewAttribute` call with:

1. Build `var known = results.ToDictionary(x => x.LongName!, StringComparer.OrdinalIgnoreCase);`
2. For each result, `var path = await AttributeAncestry.PathAsync(attr, known, parts => FetchAncestor(obj, parts));`
3. `if (await ps.CanViewAttribute(executor, obj, path)) permitted.Add(attr);`

`FetchAncestor` issues `mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, parts))` and returns the last element, or null when the stream is empty. `GetAttributeQuery` is `ICacheable` keyed on the path, so repeated ancestor lookups across a wide result set are cache hits.

**Delete `darkPrefixes` entirely** — it is now redundant and keeping both would leave two mechanisms disagreeing.

**Keep the `isPrivileged` early-out.** God and wizards bypass the walk, as today.

With `checkParents: true` the handler merges attributes from ancestor *objects*; ancestor lookups must target the object the attribute actually came from, not `obj`. If `SharpAttribute` does not carry its source object, note that in your report and use `obj` — but say so explicitly rather than silently.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeTreePatternVisibilityTests/*"`
Expected: PASS.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeTree*/*"`
Expected: PASS — no existing tree test regresses.

- [ ] **Step 5: Strengthen the test that passed by luck**

`AttributeTreePermissionTests.MortalDark_HidesFromLattrForMortal` uses `lattr(me/**)`. Add a sibling case using a leaf-only pattern so the file itself pins the real behaviour.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Library/Services/AttributeService.cs \
        SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs \
        SharpMUSH.Tests/Commands/AttributeTreePermissionTests.cs
git commit -m "Walk the real ancestor path when filtering pattern matches"
```

---

### Task 3: Split `visual` from `public`, implement `nearby`

**Files:**
- Modify: `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs:29-33`
- Modify: `SharpMUSH.Library/Services/PermissionService.cs:67-93`
- Test: `SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs` (extend)

**Interfaces:**
- Produces: `IsVisual` returns `visual` only; new `IsPublic` returns `public`.

Penn's read gate tests `AF_VISUAL` alone (`src/attrib.c:306`). `AF_PUBLIC` is a different flag overriding `SAFER_UFUN`. Conflating them makes the every-level rule mean the wrong thing.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async ValueTask PublicAlone_DoesNotGrantRead()
{
	// An attribute flagged public but not visual is not readable by a mortal
	// who cannot examine the object. Penn's read gate tests AF_VISUAL only.
}

[Test]
public async ValueTask NearbyVisualAttribute_IsHiddenFromRemoteViewer()
{
	// AF_NEARBY overrides AF_VISUAL when the viewer is not in the same location.
}
```

- [ ] **Step 2: Run to verify failure**

Expected: FAIL — `public` currently grants, and `nearby` is ignored.

- [ ] **Step 3: Implement**

Change `IsVisual` to test `visual` only. Add `IsPublic` testing `public`. Find every existing `IsVisual` caller (`grep -rn "IsVisual()" --include="*.cs" .`) and decide per call site whether it wanted `visual`, `public`, or both — **do not blanket-replace**; report each decision.

In `CanViewAttribute`, gate the visual grant on `nearby`: when any level carries `nearby` and the viewer is not in the same location as the target, the grant does not apply. Mirror how `IsNearby` would be used — it currently has zero callers, so you are its first.

- [ ] **Step 4: Run tests**

Run the new tests plus `--treenode-filter "/*/*/*Attribute*/*"` and `--treenode-filter "/*/*/*Permission*/*"`.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs \
        SharpMUSH.Library/Services/PermissionService.cs \
        SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs
git commit -m "Test visual alone for the read grant, and honour nearby"
```

---

### Task 4: Seed and enforce `internal`

**Files:**
- Create: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddInternalAttributeFlag.cs`
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs` (attrFlags array)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs` (attrFlags array)
- Modify: `SharpMUSH.Library/Services/PermissionService.cs`
- Test: `SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs` (extend)

Penn denies both reads and writes on `AF_INTERNAL` along the path. SharpMUSH has `IsInternal` with zero callers and no provider seeds the flag.

- [ ] **Step 1: Seed the flag in all three providers**

ArangoDB migration `Id => 20260824_001` (verified free across all worktrees; re-verify with `grep -rho "Id => [0-9_]*" SharpMUSH.Database.ArangoDB/Migrations/` before committing). Follow `Migration_AddSyntaxFlags.cs` for the UPSERT-keyed-on-name pattern; attribute flags carry `Name`/`Symbol`/`System`/`Inheritable` only — do **not** copy permission fields from object-flag migrations.

Name `internal`, symbol — pick a free letter, checking the seeded set first. `Inheritable = true`.

Append matching tuples to the SurrealDB and Memgraph seed arrays, matching each file's own tuple shape.

- [ ] **Step 2: Write the failing tests**

```csharp
[Test]
public async ValueTask InternalBranch_HidesLeafFromEveryone()
{
	// Penn denies on AF_INTERNAL for any viewer, including wizards —
	// verify against src/attrib.c:305 before asserting the wizard case.
}

[Test]
public async ValueTask InternalBranch_BlocksWritingALeaf() { }
```

**Check Penn first:** `can_read_attr_internal` takes an easy-out on `See_All` before the internal test. Determine whether a wizard is genuinely denied and write the assertion to match Penn, not to match intuition.

- [ ] **Step 3: Implement**

Add `internal` to the read denial in `CanViewAttribute` alongside `mortal_dark`, and to the write denial in `CanSet`.

- [ ] **Step 4: Run tests, then build all three providers**

Run: `dotnet build SharpMUSH.Database.ArangoDB SharpMUSH.Database.SurrealDB SharpMUSH.Database.Memgraph SharpMUSH.Library`

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Database.ArangoDB/Migrations/Migration_AddInternalAttributeFlag.cs \
        SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs \
        SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs \
        SharpMUSH.Library/Services/PermissionService.cs \
        SharpMUSH.Tests/Commands/AttributeTreePatternVisibilityTests.cs
git commit -m "Seed the internal attribute flag and deny on it"
```

---

### Task 5: Complete the write gate — `safe` and `nodump`

**Files:**
- Modify: `SharpMUSH.Library/Services/PermissionService.cs:19-38` (`CanSet`)
- Test: `SharpMUSH.Tests/Commands/AttributeTreeWriteGateTests.cs`

`PermissionService.cs:31` carries `// TODO: Internal and SAFE attribute flag checks not yet implemented.` Task 4 handled `internal`; this handles `safe` and create-time `nodump`.

**Also fix the axis conflation.** `CanSet` currently folds ancestor flags filtered by `Inheritable` (`:25-29`), which is the object-parent axis and works only by coincidence. Replace with explicit per-flag ancestor tests mirroring Penn's `Cannot_Write_This_Attr` (`src/attrib.c:364-368`): deny if **any** level has `internal`, `safe`, or `wizard`, or has `locked` and the writer is not the owner.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async ValueTask SafeBranch_BlocksWritingALeaf() { }

[Test]
public async ValueTask SafeAttribute_BlocksWritingItself() { }

[Test]
public async ValueTask NodumpAttribute_IsGodOnlyToCreateUnder() { }
```

- [ ] **Step 2-4:** Run to verify failure, implement, run to verify pass. Remove the TODO comment.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/PermissionService.cs \
        SharpMUSH.Tests/Commands/AttributeTreeWriteGateTests.cs
git commit -m "Enforce safe and nodump on the write gate, per flag not per Inheritable"
```

---

### Task 6: Close the two write paths that bypass the walk

**Files:**
- Modify: `SharpMUSH.Library/Services/AttributeService.cs:619-717` (`SetAttributeFlagAsync`, `UnsetAttributeFlagAsync`), `:828-870` (`ClearAttributeAsync`)
- Test: `SharpMUSH.Tests/Commands/AttributeTreeWriteGateTests.cs` (extend)

Two paths sidestep the ancestor walk entirely:

- `SetAttributeFlagAsync`/`UnsetAttributeFlagAsync` gate on `AttributeMode.Execute` → `CanExecuteAttribute`, which tests only object privilege and `public` — never `wizard`/`locked`/`safe`. **A mortal owner can strip `wizard` off their own attribute.**
- `ClearAttributeAsync:852` calls `CanSet` with each matched attribute alone, so `@wipe` under a wizard branch is ungated.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public async ValueTask MortalOwner_CannotStripWizardFromOwnAttribute() { }

[Test]
public async ValueTask WipeUnderWizardBranch_IsRefused() { }
```

- [ ] **Step 2: Run to verify failure**

Both should currently **succeed** where they must be refused — this is the sharpest fail-first signal in the plan; confirm you see the wrong behaviour before fixing.

- [ ] **Step 3: Implement**

Switch the flag set/unset paths from `AttributeMode.Execute` to a path-aware `CanSet`. In `ClearAttributeAsync`, assemble each match's path via `GetAttributeQuery(obj.Object().DBRef, attrItem.LongName!.Split('\`'))` and pass the whole path to `CanSet`.

- [ ] **Step 4: Run tests**

Run the new tests plus `--treenode-filter "/*/*/*Wipe*/*"` and `--treenode-filter "/*/*/Attribute*/*"`. `@wipe` and `@set obj/attr=` are widely exercised; a regression here is loud.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/AttributeService.cs \
        SharpMUSH.Tests/Commands/AttributeTreeWriteGateTests.cs
git commit -m "Gate flag changes and wipes on the full ancestor path"
```

---

### Task 7: `no_inherit` down-tree outside the command scan

**Files:**
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Attributes.cs:974-977` and the SurrealDB/Memgraph equivalents
- Test: `SharpMUSH.Tests/Commands/AttributeTreeInheritTests.cs`

Penn blocks an entire subtree when any branch carries `AF_PRIVATE` while resolving through a parent object (`src/attrib.c:325`). SharpMUSH tests only the leaf outside the command scan, so `get()`/`lattrp` through a parent leak.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async ValueTask NoInheritBranch_BlocksLeafThroughObjectParent()
{
	// Parent object has BRANCH (no_inherit) and BRANCH`LEAF.
	// Child object @parented to it must not see BRANCH`LEAF via get() or lattrp().
}
```

- [ ] **Step 2-4:** Verify failure, implement in all three providers, verify pass.

The check currently sits at the leaf (`lastAttr.Flags.Any(f => f.Name == "no_inherit")`); it must test every level of the returned path array.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Database.ArangoDB/ArangoDatabase.Attributes.cs \
        SharpMUSH.Database.SurrealDB/SurrealDatabase.Attributes.cs \
        SharpMUSH.Database.Memgraph/MemgraphDatabase.Attributes.cs \
        SharpMUSH.Tests/Commands/AttributeTreeInheritTests.cs
git commit -m "Block a subtree when any branch is no_inherit"
```

---

### Task 8: Pin the non-propagating flags, document, verify

**Files:**
- Test: `SharpMUSH.Tests/Commands/AttributeTreeNonPropagationTests.cs`
- Modify: `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpattr.md`

- [ ] **Step 1: Write negative tests**

These pin the **deliberate** divergence from Penn's help. Without them a future contributor will read the help, see the gap, and "fix" it.

```csharp
[Test]
public async ValueTask NoCloneBranch_DoesNotPreventCloningALeaf()
{
	// Penn's help says no_clone is inherited; atr_cpy tests AF_NOCOPY
	// per-attribute only (src/attrib.c:1701-1709). We match the code.
}

[Test]
public async ValueTask VeiledBranch_DoesNotVeilALeaf() { }

[Test]
public async ValueTask WizardBranch_DoesNotBlockReadingALeaf()
{
	// AF_WIZARD is absent from can_read_attr_internal — it gates writes only.
}
```

- [ ] **Step 2: Update the help file**

Document which flags propagate and which do not, in the same section as the existing flag docs. State plainly that `no_clone`, `veiled`, and `wizard`-on-read do not propagate **and that this matches PennMUSH's implementation rather than PennMUSH's own documentation** — otherwise the next reader will file it as a bug.

- [ ] **Step 3: Format and build clean**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
```

Run each twice. Then `dotnet build` — clean, no `FORMAT001`.

- [ ] **Step 4: Full suite**

Run: `dotnet run --project SharpMUSH.Tests`

Baseline on this branch's base is **5814 total, 0 failed, 5626 succeeded, 188 skipped**. Treat any new failure as yours until proven otherwise. Expect some existing tests to *legitimately* change behaviour — attributes under a `mortal_dark` branch become hidden. Any such change must be reviewed individually and explained in your report, never silently updated.

- [ ] **Step 4b: Provider parity check**

The default provider is ArangoDB; **production runs SurrealDB**. Tasks 4 and 7 touch all three providers, and the pattern-path fix depends on `GetAttributeQuery` behaving identically across them.

Re-run the attribute suites against SurrealDB:

```bash
SHARPMUSH_DATABASE_PROVIDER=surrealdb dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/Attribute*/*"
```

Report the result. If the harness does not honour that variable, say so plainly rather than reporting a pass you did not obtain — a silent single-provider run is exactly how a production-only divergence ships.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Tests/Commands/AttributeTreeNonPropagationTests.cs \
        SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpattr.md
git commit -m "Pin the non-propagating flags and document the divergence"
```

---

## Deferred

- `LazilyGetAttributePatternAsync` has zero callers and its three provider implementations diverge badly (Arango single-hop from an untyped start vertex, Memgraph single-hop, Surreal fully recursive). `GetLazyAttributesQueryHandler` also silently ignores `CheckParents`. Fix or delete in its own change.
- Penn's creator-owner read grant (`Owner(AL_CREATOR(atr)) == Owner(player)`, `src/attrib.c:307-308`) has no SharpMUSH equivalent on either path.
- `CanViewAttribute` denies `mortal_dark` to a mortal holding the `see_all` power; Penn bypasses via `See_All` (`src/attrib.c:305`).
