# LMDB Storage Provider (Lightning) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `SHARPMUSH_DATABASE_PROVIDER=lightning`, an embedded LMDB-backed provider implementing every interface the other providers implement, with the full test suite green under it and benchmarks for all four providers.

**Architecture:** A small store layer (environment, table catalogue, big-endian keys, JSON codec, one dedicated writer thread, synchronous read scopes, chunked streaming) beneath a `LightningDatabase` partial class ported method-for-method from the SurrealDB provider's semantics. Every relation is a forward/reverse pair of sorted-duplicate tables; attributes are a flat `(dbref, LONGNAME)` keyspace split into a metadata table and a value table. The shared `SharpMUSH.Database` project loses its `Core.Arango` reference.

**Tech Stack:** .NET 10, C#, LightningDB (Lightning.NET 0.23.x bundling LMDB 1.0.1), System.Text.Json source generation, TUnit, BenchmarkDotNet 0.15.

**Spec:** `docs/superpowers/specs/2026-09-06-lightning-provider-design.md` — read it first. The semantic source for every provider method is the SurrealDB partial with the same name under `SharpMUSH.Database.SurrealDB/`; the contract and its doc comments are `SharpMUSH.Library/ISharpDatabase.cs` and `SharpMUSH.Library/Services/Interfaces/*.cs`.

## Global Constraints

- Work in this worktree only: `/home/grave/RiderProjects/SharpMUSH/.claude/worktrees/lightning-provider`, branch `feat/lightning-provider`. Use absolute paths in every command. **Never `git add -A`**; stage your own paths. Never push.
- C# style: **tabs, indent size 2**. On a `FORMAT001` build error run `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"` **twice**. Build with `-p:SkipFormatVerification=true` only for speed while iterating; the final build of every task runs without it.
- `TreatWarningsAsErrors` is on in every project you touch. `var` throughout, no `this.`, `OneOf<T1,T2>` returns as the interface declares.
- TUnit, not xUnit. `HasCount().EqualTo(n)` is obsolete and fails the build; use `Count().IsEqualTo(n)`. Run a class with `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/<Class>/*"`. **Always write full test output to a file and grep it**; `tail` truncates failing test names.
- **Fail-first is mandatory.** Every new test must be shown red before its implementation. Porting tasks use the existing suite as the red test: the class fails with `NotImplementedException` under `lightning` before the port and passes after.
- The LightningDB package's handle type is also named `LightningDatabase`. In every store file add `using LmdbDb = LightningDB.LightningDatabase;` and never import both names unaliased.
- **Read transactions never span an `await`.** All LMDB work inside `Read(...)`/`WriteAsync(...)` delegates is synchronous. Decode inside the scope; never return an `MDBValue` or a span that points into the map.
- **Scan-shaped members stream.** Any member returning `IAsyncEnumerable<T>` is wrapped in `new FreshAsyncEnumerable<T>(ct => ...)` (`SharpMUSH.Database/FreshAsyncEnumerable.cs`; read its remarks) and iterates `store.RangeAsync(...)`; never `ToList()` a table.
- Keys are big-endian bytes from `Keys`; attribute long names are stored upper-case invariant, name-index keys lower-case invariant.
- Durability is LMDB default flags. Do not pass `NoSync`, `NoMetaSync`, `WriteMap` or `MapAsync` anywhere.
- Env vars: `SHARPMUSH_DATABASE_PROVIDER=lightning`, `SHARPMUSH_LIGHTNING_PATH` (directory), `SHARPMUSH_LIGHTNING_MAPSIZE` (bytes, default `68719476736`).
- Commit after every task with a message in the repo's style (imperative, no `feat:` prefix — see `git log --oneline -10`), ending with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

### Task 0: Move the Core.Arango reference out of the shared project

**Files:**
- Modify: `SharpMUSH.Database/SharpMUSH.Database.csproj:13-14`
- Modify: `SharpMUSH.Database.ArangoDB/SharpMUSH.Database.ArangoDB.csproj`

**Interfaces:**
- Produces: `SharpMUSH.Database` with no `Core.Arango*` package reference; every provider that needs the driver references it directly.

- [ ] **Step 1: Record the failing condition**

Run: `dotnet list SharpMUSH.Database/SharpMUSH.Database.csproj package | grep -i arango`
Expected: two lines, `Core.Arango` and `Core.Arango.Migration`. This is the state the task removes.

- [ ] **Step 2: Remove the references from the shared project**

Delete these two lines from `SharpMUSH.Database/SharpMUSH.Database.csproj`:

```xml
    <PackageReference Include="Core.Arango" Version="3.12.3" />
    <PackageReference Include="Core.Arango.Migration" Version="3.12.3" />
```

- [ ] **Step 3: Add them to the ArangoDB provider**

In `SharpMUSH.Database.ArangoDB/SharpMUSH.Database.ArangoDB.csproj`, add inside an `<ItemGroup>`:

```xml
    <PackageReference Include="Core.Arango" Version="3.12.3" />
    <PackageReference Include="Core.Arango.Migration" Version="3.12.3" />
```

- [ ] **Step 4: Build the whole solution and fix any project that compiled only through the transitive reference**

Run: `dotnet build SharpMUSH.sln -p:SkipFormatVerification=true -nologo -v:q 2>&1 | tee /tmp/claude-1000/task0-build.log | grep -E "error|Build succeeded" | head`
Expected: `Build succeeded`. If a project fails with a missing `Core.Arango` type, add `<PackageReference Include="Core.Arango" Version="3.12.3" />` to that project's csproj (candidates: `SharpMUSH.Plugins.Scene`, `SharpMUSH.Tests`, `SharpMUSH.Tests.Infrastructure`, `SharpMUSH.Benchmarks`). Do not add it back to `SharpMUSH.Database`.

- [ ] **Step 5: Verify the shared project is clean**

Run: `dotnet list SharpMUSH.Database/SharpMUSH.Database.csproj package --include-transitive | grep -i arango`
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database/SharpMUSH.Database.csproj SharpMUSH.Database.ArangoDB/SharpMUSH.Database.ArangoDB.csproj
# plus any csproj touched in step 4
git commit -m "Reference Core.Arango from the ArangoDB provider, not the shared database project

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 1: Project skeleton, options and key encoding

**Files:**
- Create: `SharpMUSH.Database.Lightning/SharpMUSH.Database.Lightning.csproj`
- Create: `SharpMUSH.Database.Lightning/GlobalUsings.cs`
- Create: `SharpMUSH.Database.Lightning/Store/LightningStoreOptions.cs`
- Create: `SharpMUSH.Library/Plugins/Storage/Lightning/Keys.cs` (namespace `SharpMUSH.Library.Plugins.Storage.Lightning`; the Library has no LightningDB dependency, and the scene accessor in Task 6 needs these types)
- Create: `SharpMUSH.Tests/Database/Lightning/KeysTests.cs`
- Modify: `SharpMUSH.sln` (add the project), `SharpMUSH.Tests/SharpMUSH.Tests.csproj` (project reference)

**Interfaces:**
- Produces:
  - `sealed record LightningStoreOptions { required string Path; long MapSize = 64L << 30; int MaxReaders = 256; int PageSize = 16384; int MaxDatabases = 64; }`
  - `static class Keys`: `byte[] Dbref(long)`, `long ReadDbref(ReadOnlySpan<byte>)`, `byte[] Str(string)`, `byte[] Lower(string)`, `byte[] Upper(string)`, `byte[] Concat(params byte[][])`, `byte[] Sep = [0x00]`, `byte[] Attr(long dbref, string longName)`, `byte[] AttrPrefix(long dbref)`, `byte[] AttrPrefix(long dbref, string literalPrefix)`, `(long Dbref, string LongName) ParseAttr(ReadOnlySpan<byte>)`, `bool StartsWith(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix)`, `byte[] Composite(long dbref, string second)`, `byte[] Composite(string first, long dbref)`, `byte[] Composite(string first, string second)`, `byte[] Composite(string first, string second, uint number)`, `string ReadStr(ReadOnlySpan<byte>)`.

- [ ] **Step 1: Create the project**

`SharpMUSH.Database.Lightning/SharpMUSH.Database.Lightning.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>default</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="SharpMUSH.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="LightningDB" Version="0.23.0" />
    <PackageReference Include="System.Linq.Async" Version="6.0.1" ExcludeAssets="compile" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SharpMUSH.Database\SharpMUSH.Database.csproj" />
    <ProjectReference Include="..\SharpMUSH.Contracts\SharpMUSH.Contracts.csproj" />
  </ItemGroup>

</Project>
```

`SharpMUSH.Database.Lightning/GlobalUsings.cs` (copy of the SurrealDB one):

```csharp
global using MModule = global::MarkupString.MarkupStringModule;
global using MString = global::MarkupString.MarkupString;
```

Add the project to the solution: `dotnet sln SharpMUSH.sln add SharpMUSH.Database.Lightning/SharpMUSH.Database.Lightning.csproj`. Add `<ProjectReference Include="..\SharpMUSH.Database.Lightning\SharpMUSH.Database.Lightning.csproj" />` to `SharpMUSH.Tests/SharpMUSH.Tests.csproj` next to the other provider references.

- [ ] **Step 2: Write the options record**

`SharpMUSH.Database.Lightning/Store/LightningStoreOptions.cs`:

```csharp
namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Environment settings. <see cref="PageSize"/> applies only when the environment is created; LMDB
/// reads it back from an existing file. <see cref="MapSize"/> is the file-size ceiling, not RAM: it is
/// sparse on Linux and macOS and grows incrementally on Windows with LMDB 1.0.
/// </summary>
public sealed record LightningStoreOptions
{
	public required string Path { get; init; }
	public long MapSize { get; init; } = 64L << 30;
	public int MaxReaders { get; init; } = 256;
	public int PageSize { get; init; } = 16384;
	public int MaxDatabases { get; init; } = 64;
}
```

- [ ] **Step 3: Write the failing key tests**

`SharpMUSH.Tests/Database/Lightning/KeysTests.cs`:

```csharp
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class KeysTests
{
	[Test]
	public async Task DbrefKeysSortNumerically()
	{
		var k9 = Keys.Dbref(9);
		var k10 = Keys.Dbref(10);
		var k256 = Keys.Dbref(256);
		await Assert.That(k9.AsSpan().SequenceCompareTo(k10)).IsLessThan(0);
		await Assert.That(k10.AsSpan().SequenceCompareTo(k256)).IsLessThan(0);
		await Assert.That(Keys.ReadDbref(k256)).IsEqualTo(256);
	}

	[Test]
	public async Task AttributeKeysGroupByDbrefAndSortByLongName()
	{
		var a = Keys.Attr(5, "FOO");
		var b = Keys.Attr(5, "FOO`BAR");
		var c = Keys.Attr(6, "AAA");
		await Assert.That(a.AsSpan().SequenceCompareTo(b)).IsLessThan(0);
		await Assert.That(b.AsSpan().SequenceCompareTo(c)).IsLessThan(0);
		await Assert.That(Keys.StartsWith(b, Keys.AttrPrefix(5))).IsTrue();
		await Assert.That(Keys.StartsWith(c, Keys.AttrPrefix(5))).IsFalse();
		await Assert.That(Keys.StartsWith(b, Keys.AttrPrefix(5, "FOO`"))).IsTrue();
	}

	[Test]
	public async Task AttributeKeysAreUpperCasedAndRoundTrip()
	{
		var key = Keys.Attr(42, "desc`Format");
		var (dbref, longName) = Keys.ParseAttr(key);
		await Assert.That(dbref).IsEqualTo(42);
		await Assert.That(longName).IsEqualTo("DESC`FORMAT");
	}

	[Test]
	public async Task CompositeKeysSeparateWithZeroByte()
	{
		var key = Keys.Composite("public", 7);
		await Assert.That(key[6]).IsEqualTo((byte)0);
		await Assert.That(Keys.ReadDbref(key.AsSpan(7))).IsEqualTo(7);
	}
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/KeysTests/*" > /tmp/claude-1000/task1-red.log 2>&1; grep -E "error CS|failed|passed" /tmp/claude-1000/task1-red.log | head`
Expected: compile error `The type or namespace name 'Keys' does not exist`.

- [ ] **Step 5: Implement Keys**

`SharpMUSH.Library/Plugins/Storage/Lightning/Keys.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;

namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>
/// Key encoders. Every key is plain bytes compared lexicographically by LMDB, so fixed-width
/// big-endian integers sort numerically and a shorter key is a prefix of every key that extends it.
/// Variable-length parts are separated by a single 0x00, which no stored name contains.
/// </summary>
public static class Keys
{
	public static readonly byte[] Sep = [0x00];

	public static byte[] Dbref(long dbref)
	{
		var buffer = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(buffer, unchecked((ulong)dbref));
		return buffer;
	}

	public static long ReadDbref(ReadOnlySpan<byte> bytes)
		=> unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]));

	public static byte[] Str(string value) => Encoding.UTF8.GetBytes(value);
	public static byte[] Lower(string value) => Str(value.ToLowerInvariant());
	public static byte[] Upper(string value) => Str(value.ToUpperInvariant());
	public static string ReadStr(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

	public static byte[] Concat(params byte[][] parts)
	{
		var total = 0;
		foreach (var part in parts) total += part.Length;
		var buffer = new byte[total];
		var offset = 0;
		foreach (var part in parts)
		{
			part.CopyTo(buffer, offset);
			offset += part.Length;
		}
		return buffer;
	}

	public static bool StartsWith(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix)
		=> key.Length >= prefix.Length && key[..prefix.Length].SequenceEqual(prefix);

	public static byte[] Attr(long dbref, string longName) => Concat(Dbref(dbref), Sep, Upper(longName));
	public static byte[] AttrPrefix(long dbref) => Concat(Dbref(dbref), Sep);
	public static byte[] AttrPrefix(long dbref, string literalPrefix) => Concat(Dbref(dbref), Sep, Upper(literalPrefix));

	public static (long Dbref, string LongName) ParseAttr(ReadOnlySpan<byte> key)
		=> (ReadDbref(key), ReadStr(key[9..]));

	public static byte[] Composite(long dbref, string second) => Concat(Dbref(dbref), Sep, Str(second));
	public static byte[] Composite(string first, long dbref) => Concat(Str(first), Sep, Dbref(dbref));
	public static byte[] Composite(string first, string second) => Concat(Str(first), Sep, Str(second));

	public static byte[] Composite(string first, string second, uint number)
	{
		var tail = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(tail, number);
		return Concat(Str(first), Sep, Str(second), Sep, tail);
	}
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/KeysTests/*" > /tmp/claude-1000/task1-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task1-green.log | tail -3`
Expected: 4 passed, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.sln SharpMUSH.Database.Lightning SharpMUSH.Library/Plugins/Storage/Lightning SharpMUSH.Tests/SharpMUSH.Tests.csproj SharpMUSH.Tests/Database/Lightning/KeysTests.cs
git commit -m "Add the Lightning provider project with its options and key encoding

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Table catalogue, environment and read scopes

**Files:**
- Create: `SharpMUSH.Library/Plugins/Storage/Lightning/TableDef.cs` (`TableKind`, `TableDef`) and `SharpMUSH.Library/Plugins/Storage/Lightning/ITx.cs` — both in namespace `SharpMUSH.Library.Plugins.Storage.Lightning`, no LightningDB types
- Create: `SharpMUSH.Database.Lightning/Store/Tables.cs`
- Create: `SharpMUSH.Database.Lightning/Store/LightningStoreException.cs`
- Create: `SharpMUSH.Database.Lightning/Store/LightningStore.cs` (environment, tables, `Read`, `Count`, `CopyTo`, close/reopen; writes come in Task 3)
- Test: `SharpMUSH.Tests/Database/Lightning/LightningStoreTests.cs`

**Interfaces:**
- Consumes: `Keys`, `LightningStoreOptions` (Task 1).
- Produces:
  - `enum TableKind { Node, ForwardEdge, ReverseEdge, Index }`
  - `sealed record TableDef(string Name, bool Duplicates, bool FixedDuplicates, TableKind Kind)` with `TableDef? Pair { get; internal set; }` — `Pair` links a forward table to its reverse and vice versa. The store maps `Duplicates` → `DatabaseOpenFlags.DuplicatesSort` and `FixedDuplicates` → `| DuplicatesFixed`.
  - `static class Tables` with one static field per table in spec §6 and `IReadOnlyList<TableDef> All`, plus `IEnumerable<(TableDef Forward, TableDef Reverse)> EdgePairs`.
  - `interface ITx { bool TryGet(TableDef, ReadOnlySpan<byte> key, out byte[] value); void Put(TableDef, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value); bool Delete(TableDef, ReadOnlySpan<byte> key); bool Delete(TableDef, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value); long Count(TableDef); IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef, byte[] prefix); IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef, byte[] prefix, byte[] afterKey, byte[]? afterValue); IEnumerable<byte[]> Dups(TableDef, byte[] key); int DeletePrefix(TableDef, byte[] prefix); }`
  - `sealed class LightningStore : IDisposable { LightningStore(LightningStoreOptions); string Path; T Read<T>(Func<ITx, T>); long Count(TableDef); void CopyTo(string path, bool compact = true); internal void Close(); internal void Reopen(); }`
  - `sealed class LightningStoreException(MDBResultCode code, string message) : Exception`.

- [ ] **Step 1: Write the failing store tests**

`SharpMUSH.Tests/Database/Lightning/LightningStoreTests.cs`:

```csharp
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class LightningStoreTests
{
	private static LightningStore Open() => new(new LightningStoreOptions
	{
		Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
		MapSize = 256L << 20
	});

	[Test]
	public async Task OpensEveryCatalogueTable()
	{
		using var store = Open();
		var counts = store.Read(tx => Tables.All.Select(t => tx.Count(t)).ToArray());
		await Assert.That(counts.All(c => c == 0)).IsTrue();
		await Assert.That(counts.Length).IsEqualTo(Tables.All.Count);
	}

	[Test]
	public async Task RangeReturnsOnlyKeysUnderThePrefixInOrder()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "B"), "b"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "A`X"), "ax"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(5, "A"), "a"u8);
			tx.Put(Tables.AttrMeta, Keys.Attr(6, "A"), "other"u8);
		});
		var names = store.Read(tx => tx.Range(Tables.AttrMeta, Keys.AttrPrefix(5))
			.Select(e => Keys.ParseAttr(e.Key).LongName).ToArray());
		await Assert.That(names).IsEquivalentTo(new[] { "A", "A`X", "B" });
	}

	[Test]
	public async Task DuplicateValuesAreSortedAndIndividuallyDeletable()
	{
		using var store = Open();
		await store.WriteAsync(tx =>
		{
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(30));
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20));
			tx.Put(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20));
		});
		var before = store.Read(tx => tx.Dups(Tables.RevLocation, Keys.Dbref(1)).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(before).IsEquivalentTo(new long[] { 20, 30 });
		await store.WriteAsync(tx => tx.Delete(Tables.RevLocation, Keys.Dbref(1), Keys.Dbref(20)));
		var after = store.Read(tx => tx.Dups(Tables.RevLocation, Keys.Dbref(1)).Select(v => Keys.ReadDbref(v)).ToArray());
		await Assert.That(after).IsEquivalentTo(new long[] { 30 });
	}
}
```

Note: `WriteAsync` is delivered by Task 3; this task implements `Read`, and the test file will not compile until Task 3. To keep fail-first honest for this task, implement `Read` and a temporary `internal T Write<T>(Func<ITx,T>)` that begins a write transaction on the calling thread; Task 3 replaces it with the writer thread and adds `WriteAsync`. Use `store.Write(tx => { ... ; return 0; })` in the tests above for now and switch them to `WriteAsync` in Task 3.

- [ ] **Step 2: Run to verify the tests fail to compile**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LightningStoreTests/*" > /tmp/claude-1000/task2-red.log 2>&1; grep -E "error CS" /tmp/claude-1000/task2-red.log | head -3`
Expected: `Tables`/`LightningStore` not found.

- [ ] **Step 3: Write the table catalogue**

`SharpMUSH.Library/Plugins/Storage/Lightning/TableDef.cs`:

```csharp
namespace SharpMUSH.Library.Plugins.Storage.Lightning;

public enum TableKind { Node, ForwardEdge, ReverseEdge, Index }

/// <summary>One named LMDB sub-database. <see cref="Pair"/> links each edge direction to the other.</summary>
public sealed record TableDef(string Name, bool Duplicates, bool FixedDuplicates, TableKind Kind)
{
	public TableDef? Pair { get; internal set; }
	public override string ToString() => Name;

	public static TableDef Node(string name) => new(name, false, false, TableKind.Node);
	public static TableDef Index(string name, bool duplicates = false) => new(name, duplicates, false, TableKind.Index);

	public static (TableDef Forward, TableDef Reverse) Edge(string name, bool fixedDuplicates)
	{
		var forward = new TableDef("e." + name, true, fixedDuplicates, TableKind.ForwardEdge);
		var reverse = new TableDef("r." + name, true, fixedDuplicates, TableKind.ReverseEdge);
		forward.Pair = reverse;
		reverse.Pair = forward;
		return (forward, reverse);
	}
}
```

`SharpMUSH.Database.Lightning/Store/Tables.cs`:

```csharp
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The schema. Every table the provider touches is declared here so the store can open them all at
/// startup and the delete cascade can enumerate the edge pairs. Names are stable on disk; renaming one
/// is a migration. Plugin-owned tables are opened through the accessor and appended to <see cref="All"/>.
/// </summary>
public static class Tables
{
	public static readonly TableDef Meta = TableDef.Node("meta");
	public static readonly TableDef Obj = TableDef.Node("obj");
	public static readonly TableDef ObjName = TableDef.Index("obj.name", duplicates: true);
	public static readonly TableDef AttrMeta = TableDef.Node("attr.meta");
	public static readonly TableDef AttrVal = TableDef.Node("attr.val");
	public static readonly TableDef AttrFlag = TableDef.Node("attr.flag");
	public static readonly TableDef AttrEntry = TableDef.Node("attr.entry");
	public static readonly TableDef Flag = TableDef.Node("flag");
	public static readonly TableDef Power = TableDef.Node("power");

	public static readonly (TableDef Forward, TableDef Reverse) Location = TableDef.Edge("loc", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Home = TableDef.Edge("home", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Owner = TableDef.Edge("owner", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Parent = TableDef.Edge("parent", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Zone = TableDef.Edge("zone", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) Exit = TableDef.Edge("exit", fixedDuplicates: true);
	public static readonly (TableDef Forward, TableDef Reverse) ObjFlag = TableDef.Edge("flag", fixedDuplicates: false);
	public static readonly (TableDef Forward, TableDef Reverse) ObjPower = TableDef.Edge("power", fixedDuplicates: false);
	public static readonly (TableDef Forward, TableDef Reverse) AccountChar = TableDef.Edge("acct.char", fixedDuplicates: false);

	public static readonly TableDef RevLocation = Location.Reverse;

	public static readonly TableDef Chan = TableDef.Node("chan");
	public static readonly TableDef ChanMember = TableDef.Node("chan.member");
	public static readonly TableDef RevChanMember = TableDef.Index("r.chan.member", duplicates: true);
	public static readonly TableDef Mail = TableDef.Node("mail");
	public static readonly TableDef MailBox = TableDef.Index("mail.box");
	public static readonly TableDef MailSent = TableDef.Index("mail.sent");
	public static readonly TableDef Account = TableDef.Node("account");
	public static readonly TableDef AccountEmail = TableDef.Index("account.email");
	public static readonly TableDef AccountUser = TableDef.Index("account.user");
	public static readonly TableDef AccountRole = TableDef.Index("e.acct.role", duplicates: true);
	public static readonly TableDef Session = TableDef.Node("session");
	public static readonly TableDef SessionAccount = TableDef.Index("session.acct", duplicates: true);
	public static readonly TableDef SessionIp = TableDef.Index("session.ip", duplicates: true);
	public static readonly TableDef State = TableDef.Node("state");
	public static readonly TableDef ExpandedObj = TableDef.Node("x.obj");
	public static readonly TableDef ExpandedSrv = TableDef.Node("x.srv");
	public static readonly TableDef WikiPage = TableDef.Node("wiki.page");
	public static readonly TableDef WikiSlug = TableDef.Index("wiki.slug");
	public static readonly TableDef WikiRev = TableDef.Node("wiki.rev");
	public static readonly TableDef WikiTr = TableDef.Node("wiki.tr");
	public static readonly TableDef Layout = TableDef.Node("layout");
	public static readonly TableDef App = TableDef.Node("app");
	public static readonly TableDef Role = TableDef.Node("role");
	public static readonly TableDef Pkg = TableDef.Node("pkg");
	public static readonly TableDef PkgObj = TableDef.Node("pkg.obj");
	public static readonly TableDef PkgAttr = TableDef.Node("pkg.attr");
	public static readonly TableDef PkgStruct = TableDef.Node("pkg.struct");
	public static readonly TableDef PkgRemote = TableDef.Node("pkg.remote");
	public static readonly TableDef PkgRev = TableDef.Node("pkg.rev");
	public static readonly TableDef PkgDep = TableDef.Index("pkg.dep", duplicates: true);

	public static readonly IReadOnlyList<TableDef> All = typeof(Tables)
		.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
		.SelectMany(f => f.GetValue(null) switch
		{
			TableDef t => [t],
			ValueTuple<TableDef, TableDef> pair => new[] { pair.Item1, pair.Item2 },
			_ => Array.Empty<TableDef>()
		})
		.Distinct()
		.ToArray();

	/// <summary>Every forward/reverse pair; the object-delete cascade walks all of them.</summary>
	public static IEnumerable<(TableDef Forward, TableDef Reverse)> EdgePairs =>
		All.Where(t => t.Kind == TableKind.ForwardEdge).Select(t => (t, t.Pair!));
}
```

- [ ] **Step 4: Write the transaction abstraction and exception**

`SharpMUSH.Library/Plugins/Storage/Lightning/ITx.cs`:

```csharp
namespace SharpMUSH.Library.Plugins.Storage.Lightning;

/// <summary>
/// One LMDB transaction, read-only or read-write, valid only inside the delegate that received it.
/// Every result is a copy; nothing returned points into the memory map.
/// </summary>
public interface ITx
{
	bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value);
	void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
	bool Delete(TableDef table, ReadOnlySpan<byte> key);
	bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
	long Count(TableDef table);
	/// <summary>All entries whose key starts with <paramref name="prefix"/>, in key order (duplicates in value order).</summary>
	IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix);
	/// <summary>As <see cref="Range"/>, resuming strictly after (<paramref name="afterKey"/>, <paramref name="afterValue"/>).</summary>
	IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue);
	/// <summary>All duplicate values stored under <paramref name="key"/>, in order.</summary>
	IEnumerable<byte[]> Dups(TableDef table, byte[] key);
	/// <summary>Deletes every entry under the prefix; returns how many.</summary>
	int DeletePrefix(TableDef table, byte[] prefix);
}
```

`SharpMUSH.Database.Lightning/Store/LightningStoreException.cs`:

```csharp
using LightningDB;

namespace SharpMUSH.Database.Lightning.Store;

public sealed class LightningStoreException(MDBResultCode code, string message) : Exception(message)
{
	public MDBResultCode Code { get; } = code;

	public static LightningStoreException From(MDBResultCode code, string operation) => code switch
	{
		MDBResultCode.MapFull => new(code, $"{operation}: the database reached its size ceiling. Raise SHARPMUSH_LIGHTNING_MAPSIZE (bytes) and restart."),
		MDBResultCode.TxnFull => new(code, $"{operation}: a single write touched too many pages. Split the operation into smaller batches."),
		MDBResultCode.ReadersFull => new(code, $"{operation}: too many concurrent readers. Raise LightningStoreOptions.MaxReaders."),
		_ => new(code, $"{operation}: LMDB returned {code}.")
	};
}
```

- [ ] **Step 5: Write the store with the transaction implementation**

`SharpMUSH.Database.Lightning/Store/LightningStore.cs`:

```csharp
using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using LmdbDb = LightningDB.LightningDatabase;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Owns the LMDB environment and the open table handles. Reads run on the calling thread inside
/// <see cref="Read{T}"/>; writes are serialized through <see cref="LightningWriter"/> (Task 3). The
/// environment can be closed and reopened under <see cref="Gate"/> so staging promotion can swap the
/// directory beneath a live singleton.
/// </summary>
public sealed partial class LightningStore : IDisposable
{
	private readonly LightningStoreOptions _options;
	private readonly ReaderWriterLockSlim _gate = new(LockRecursionPolicy.NoRecursion);
	private LightningEnvironment _env = null!;
	private Dictionary<TableDef, LmdbDb> _tables = new();

	public string Path => _options.Path;
	internal ReaderWriterLockSlim Gate => _gate;

	public LightningStore(LightningStoreOptions options)
	{
		_options = options;
		Open();
	}

	private void Open()
	{
		Directory.CreateDirectory(_options.Path);
		_env = new LightningEnvironment(_options.Path, new EnvironmentConfiguration
		{
			MapSize = _options.MapSize,
			MaxDatabases = _options.MaxDatabases,
			MaxReaders = _options.MaxReaders,
			PageSize = _options.PageSize
		});
		_env.Open(EnvironmentOpenFlags.NoThreadLocalStorage);

		using var tx = _env.BeginTransaction();
		var tables = new Dictionary<TableDef, LmdbDb>();
		foreach (var def in Tables.All)
		{
			tables[def] = tx.OpenDatabase(def.Name, new DatabaseConfiguration { Flags = FlagsFor(def) | DatabaseOpenFlags.Create });
		}
		tx.Commit().ThrowOnError();
		_tables = tables;
	}

	internal static DatabaseOpenFlags FlagsFor(TableDef def) => def.Duplicates
		? DatabaseOpenFlags.DuplicatesSort | (def.FixedDuplicates ? DatabaseOpenFlags.DuplicatesFixed : DatabaseOpenFlags.None)
		: DatabaseOpenFlags.None;

	internal void Close()
	{
		foreach (var db in _tables.Values) db.Dispose();
		_tables = new();
		_env.Dispose();
	}

	internal void Reopen() => Open();

	public T Read<T>(Func<ITx, T> read)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction(TransactionBeginFlags.ReadOnly);
			return read(new Tx(tx, _tables));
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	/// <summary>Direct write on the calling thread. Only the writer thread (Task 3) and tests may call this.</summary>
	internal T Write<T>(Func<ITx, T> write)
	{
		_gate.EnterReadLock();
		try
		{
			using var tx = _env.BeginTransaction();
			var result = write(new Tx(tx, _tables));
			var code = tx.Commit();
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, "commit");
			return result;
		}
		finally
		{
			_gate.ExitReadLock();
		}
	}

	public long Count(TableDef table) => Read(tx => tx.Count(table));

	public void CopyTo(string path, bool compact = true)
	{
		Directory.CreateDirectory(path);
		_env.CopyTo(path, compact);
	}

	public void Dispose()
	{
		Close();
		_gate.Dispose();
	}

	private sealed class Tx(LightningTransaction tx, Dictionary<TableDef, LmdbDb> tables) : ITx
	{
		private LmdbDb Db(TableDef t) => tables[t];

		public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value)
		{
			var (code, _, v) = tx.Get(Db(table), key);
			if (code == MDBResultCode.NotFound) { value = []; return false; }
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"get {table}");
			value = v.CopyToNewArray();
			return true;
		}

		public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		{
			var code = tx.Put(Db(table), key, value);
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"put {table}");
		}

		public bool Delete(TableDef table, ReadOnlySpan<byte> key)
		{
			var code = tx.Delete(Db(table), key);
			if (code == MDBResultCode.NotFound) return false;
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"delete {table}");
			return true;
		}

		public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
		{
			var code = tx.Delete(Db(table), key, value);
			if (code == MDBResultCode.NotFound) return false;
			if (code != MDBResultCode.Success) throw LightningStoreException.From(code, $"delete {table}");
			return true;
		}

		public long Count(TableDef table) => tx.GetEntriesCount(Db(table));

		public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix)
		{
			using var cursor = tx.CreateCursor(Db(table));
			if (cursor.SetRange(prefix) != MDBResultCode.Success) yield break;
			do
			{
				var (code, k, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				var key = k.CopyToNewArray();
				if (!Keys.StartsWith(key, prefix)) yield break;
				yield return (key, v.CopyToNewArray());
			} while (cursor.Next() == MDBResultCode.Success);
		}

		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue)
		{
			foreach (var entry in Range(table, prefix))
			{
				var keyCompare = entry.Key.AsSpan().SequenceCompareTo(afterKey);
				if (keyCompare < 0) continue;
				if (keyCompare == 0 && (afterValue is null || entry.Value.AsSpan().SequenceCompareTo(afterValue) <= 0)) continue;
				yield return entry;
			}
		}

		public IEnumerable<byte[]> Dups(TableDef table, byte[] key)
		{
			using var cursor = tx.CreateCursor(Db(table));
			if (cursor.Set(key) != MDBResultCode.Success) yield break;
			do
			{
				var (code, _, v) = cursor.GetCurrent();
				if (code != MDBResultCode.Success) yield break;
				yield return v.CopyToNewArray();
			} while (cursor.NextDuplicate() == MDBResultCode.Success);
		}

		public int DeletePrefix(TableDef table, byte[] prefix)
		{
			var keys = Range(table, prefix).Select(e => (e.Key, e.Value)).ToList();
			foreach (var (k, v) in keys)
			{
				if (table.Duplicates) tx.Delete(Db(table), k, v);
				else tx.Delete(Db(table), k);
			}
			return keys.Count;
		}
	}
}
```

Implementation notes for the engineer: `RangeFrom` above is written for clarity; replace the linear skip with `cursor.SetRange(afterKey)` plus one `Next()`/`NextDuplicate()` step once the tests pass, keeping the same semantics. Check the exact Lightning.NET names (`CopyToNewArray`, `GetEntriesCount`, `SetRange`, `Set`, `NextDuplicate`, `PageSize`) against the package's public API with `dotnet build`; if a name differs, use the package's name and keep the behaviour.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LightningStoreTests/*" > /tmp/claude-1000/task2-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task2-green.log | tail -3`
Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Database.Lightning/Store SharpMUSH.Library/Plugins/Storage/Lightning SharpMUSH.Tests/Database/Lightning/LightningStoreTests.cs
git commit -m "Open the Lightning table catalogue and read through synchronous transaction scopes

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The single writer thread

**Files:**
- Create: `SharpMUSH.Database.Lightning/Store/LightningWriter.cs`
- Modify: `SharpMUSH.Database.Lightning/Store/LightningStore.cs` (own a writer; add `WriteAsync`; `Write` becomes private to the writer)
- Test: `SharpMUSH.Tests/Database/Lightning/LightningWriterTests.cs`; update `LightningStoreTests` to use `WriteAsync`.

**Interfaces:**
- Produces on `LightningStore`: `ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default)`, `ValueTask WriteAsync(Action<ITx> job, CancellationToken ct = default)`, `internal Task DrainAsync()` (completes when every queued job has finished), `internal void PauseWriter()` / `internal void ResumeWriter()` (used by staging promotion).

- [ ] **Step 1: Write the failing writer tests**

`SharpMUSH.Tests/Database/Lightning/LightningWriterTests.cs`:

```csharp
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class LightningWriterTests
{
	private static LightningStore Open() => new(new LightningStoreOptions
	{
		Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
		MapSize = 256L << 20
	});

	[Test]
	public async Task JobsRunInSubmissionOrderOnOneThread()
	{
		using var store = Open();
		var threads = new List<int>();
		var tasks = Enumerable.Range(0, 50).Select(i => store.WriteAsync(tx =>
		{
			lock (threads) threads.Add(Environment.CurrentManagedThreadId);
			tx.Put(Tables.Meta, Keys.Str("k" + i), Keys.Str(i.ToString()));
			return i;
		}).AsTask()).ToArray();
		var results = await Task.WhenAll(tasks);
		await Assert.That(results).IsEquivalentTo(Enumerable.Range(0, 50));
		await Assert.That(threads.Distinct().Count()).IsEqualTo(1);
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(50);
	}

	[Test]
	public async Task AThrowingJobFaultsOnlyItsOwnTaskAndRollsBack()
	{
		using var store = Open();
		var bad = store.WriteAsync<int>(tx =>
		{
			tx.Put(Tables.Meta, Keys.Str("ghost"), Keys.Str("x"));
			throw new InvalidOperationException("boom");
		}).AsTask();
		var good = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("real"), Keys.Str("y")); return 1; }).AsTask();
		await Assert.That(async () => await bad).Throws<InvalidOperationException>();
		await Assert.That(await good).IsEqualTo(1);
		var hasGhost = store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("ghost"), out _));
		await Assert.That(hasGhost).IsFalse();
	}

	[Test]
	public async Task CancellationBeforeDequeueSkipsTheJob()
	{
		using var store = Open();
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var task = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("never"), Keys.Str("z")); return 0; }, cts.Token).AsTask();
		await Assert.That(async () => await task).Throws<OperationCanceledException>();
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("never"), out _))).IsFalse();
	}
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LightningWriterTests/*" > /tmp/claude-1000/task3-red.log 2>&1; grep -E "error CS" /tmp/claude-1000/task3-red.log | head -3`
Expected: `WriteAsync` not found.

- [ ] **Step 3: Implement the writer**

`SharpMUSH.Database.Lightning/Store/LightningWriter.cs`:

```csharp
using System.Threading.Channels;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The one thread allowed to begin LMDB write transactions. Jobs run to completion in submission
/// order; each job is its own transaction and its own fsynced commit. A job that throws is aborted and
/// its exception is delivered to its caller only.
/// </summary>
internal sealed class LightningWriter : IDisposable
{
	private sealed record Job(Func<ITx, object?> Work, TaskCompletionSource<object?> Completion, CancellationToken Token);

	private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(10_000)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait
	});
	private readonly Func<Func<ITx, object?>, object?> _execute;
	private readonly Thread _thread;
	private readonly ManualResetEventSlim _resume = new(true);

	public LightningWriter(Func<Func<ITx, object?>, object?> execute)
	{
		_execute = execute;
		_thread = new Thread(Run) { IsBackground = true, Name = "lightning-writer" };
		_thread.Start();
	}

	public async ValueTask<T> EnqueueAsync<T>(Func<ITx, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new Job(tx => job(tx), completion, ct), ct).ConfigureAwait(false);
		return (T)(await completion.Task.ConfigureAwait(false))!;
	}

	/// <summary>Completes once every job queued before the call has finished.</summary>
	public Task DrainAsync() => EnqueueAsync(_ => 0, CancellationToken.None).AsTask();

	public void Pause() => _resume.Reset();
	public void Resume() => _resume.Set();

	private void Run()
	{
		var reader = _queue.Reader;
		while (true)
		{
			Job job;
			try
			{
				if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) return;
				if (!reader.TryRead(out job!)) continue;
			}
			catch (ChannelClosedException) { return; }

			if (job.Token.IsCancellationRequested)
			{
				job.Completion.TrySetCanceled(job.Token);
				continue;
			}

			_resume.Wait();
			try
			{
				job.Completion.TrySetResult(_execute(job.Work));
			}
			catch (Exception ex)
			{
				job.Completion.TrySetException(ex);
			}
		}
	}

	public void Dispose()
	{
		_queue.Writer.TryComplete();
		_resume.Set();
		if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(10));
		_resume.Dispose();
	}
}
```

Modify `LightningStore`: add `private readonly LightningWriter _writer;` created in the constructor as `new LightningWriter(work => Write(work))`, make `Write<T>` private, and add:

```csharp
	public ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default) => _writer.EnqueueAsync(job, ct);

	public async ValueTask WriteAsync(Action<ITx> job, CancellationToken ct = default)
		=> await _writer.EnqueueAsync<object?>(tx => { job(tx); return null; }, ct).ConfigureAwait(false);

	internal Task DrainAsync() => _writer.DrainAsync();
	internal void PauseWriter() => _writer.Pause();
	internal void ResumeWriter() => _writer.Resume();
```

and dispose the writer first in `Dispose()`. Note the private `Write` runs on the writer thread because `_execute` is invoked from `Run`; that satisfies LMDB's thread rule.

- [ ] **Step 4: Switch `LightningStoreTests` to `WriteAsync` and run both classes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/Lightning*Tests/*" > /tmp/claude-1000/task3-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task3-green.log | tail -3`
Expected: all Lightning store and writer tests pass.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Database.Lightning/Store SharpMUSH.Tests/Database/Lightning
git commit -m "Serialize Lightning writes through one dedicated writer thread

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Chunked streaming and the JSON codec

**Files:**
- Create: `SharpMUSH.Database.Lightning/Store/LightningStore.Streaming.cs`
- Create: `SharpMUSH.Database.Lightning/Store/Codec.cs`
- Create: `SharpMUSH.Database.Lightning/Records/*.cs` (one file per record listed below)
- Test: `SharpMUSH.Tests/Database/Lightning/StreamingTests.cs`, `CodecTests.cs`

**Interfaces:**
- Produces on `LightningStore`: `IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default)` — reads `pageSize` entries per read transaction and resumes after the last (key, value).
- Produces `static class Codec { byte[] Serialize<T>(T value); T Deserialize<T>(ReadOnlySpan<byte> bytes); }` backed by `[JsonSerializable]` `LightningJsonContext` with `PropertyNamingPolicy = null`, `DefaultIgnoreCondition = WhenWritingNull`.
- Produces records (all `sealed record`, mutable `init` properties, in `SharpMUSH.Database.Lightning.Records`):
  - `ObjectRecord { string Name; string Type; string[] Aliases; long CreationTime; long ModifiedTime; string? PasswordHash; string? PasswordSalt; long Quota; string? Warnings; Dictionary<string, LockRecord> Locks; }` and `LockRecord { string LockString; string Flags; }` — mirror `SharpMUSH.Database/Models/SharpObject.cs` and `SharpLockData.cs`.
  - `AttrMetaRecord { long? Owner; string[] Flags; string? Entry; }`
  - `FlagRecord { string Name; string Symbol; string[] Aliases; string[] SetPermissions; string[] UnsetPermissions; string[] TypeRestrictions; bool System; bool Disabled; }`, `PowerRecord { string Name; string Alias; string[] SetPermissions; string[] UnsetPermissions; string[] TypeRestrictions; bool System; bool Disabled; }`, `AttributeFlagRecord { string Name; string Symbol; bool Inheritable; bool System; }`, `AttributeEntryRecord { string Name; string[] DefaultFlags; string? Limit; string[]? Enum; }` — align property names with `SharpMUSH.Library/Models/SharpObjectFlag.cs`, `SharpPower.cs`, `SharpAttributeFlag.cs`, `SharpAttributeEntry.cs`.
  - `ChannelRecord`, `ChannelMemberRecord { bool Gagged; bool Mute; bool Hide; bool Combine; string Title; }`, `MailRecord`, `AccountRecord`, `SessionRecord`, `ServerStateRecord`, `WikiPageRecord`, `WikiRevisionRecord`, `WikiTranslationRecord`, `ApplicationRecord`, `RoleRecord`, package records — each mirrors the fields the corresponding SurrealDB partial reads and writes (`SurrealDatabase.Channels.cs`, `.Mail.cs`, `.Accounts.cs`, `.Sessions.cs`, `.ServerState.cs`, `.Wiki.cs`, `.Applications.cs`, `.Roles.cs`, `.Packages.cs`). Markup-string fields are stored as `string` produced by `MModule.serialize`.

- [ ] **Step 1: Write the failing streaming test**

`SharpMUSH.Tests/Database/Lightning/StreamingTests.cs`:

```csharp
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class StreamingTests
{
	[Test]
	public async Task RangeAsyncResumesAcrossPagesWhileWritesLand()
	{
		using var store = new LightningStore(new LightningStoreOptions
		{
			Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
			MapSize = 256L << 20
		});
		await store.WriteAsync(tx =>
		{
			for (var i = 0; i < 1000; i++) tx.Put(Tables.AttrMeta, Keys.Attr(1, $"A{i:D4}"), Keys.Str("v"));
		});
		var seen = new List<string>();
		var i = 0;
		await foreach (var entry in store.RangeAsync(Tables.AttrMeta, Keys.AttrPrefix(1), pageSize: 100))
		{
			seen.Add(Keys.ParseAttr(entry.Key).LongName);
			if (i++ == 150) await store.WriteAsync(tx => tx.Put(Tables.AttrMeta, Keys.Attr(1, "A0500X"), Keys.Str("late")));
		}
		await Assert.That(seen.Count).IsEqualTo(1001);
		await Assert.That(seen).IsInOrder();
		await Assert.That(seen.Distinct().Count()).IsEqualTo(1001);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/StreamingTests/*" > /tmp/claude-1000/task4-red.log 2>&1; grep -E "error CS" /tmp/claude-1000/task4-red.log | head -3`
Expected: `RangeAsync` not found.

- [ ] **Step 3: Implement chunked streaming**

`SharpMUSH.Database.Lightning/Store/LightningStore.Streaming.cs`:

```csharp
using System.Runtime.CompilerServices;

using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	/// <summary>
	/// Streams a prefix range without holding a read transaction across the consumer's awaits: each
	/// page is read in its own transaction and the next page resumes strictly after the last entry.
	/// Entries written between pages at keys not yet passed are included; entries deleted are skipped.
	/// </summary>
	public async IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix,
		int pageSize = 256, [EnumeratorCancellation] CancellationToken ct = default)
	{
		byte[]? lastKey = null;
		byte[]? lastValue = null;
		var dup = table.Duplicates;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var page = Read(tx =>
				(lastKey is null ? tx.Range(table, prefix) : tx.RangeFrom(table, prefix, lastKey, dup ? lastValue : null))
					.Take(pageSize).ToList());
			foreach (var entry in page) yield return entry;
			if (page.Count < pageSize) yield break;
			(lastKey, lastValue) = page[^1];
		}
	}
}
```

- [ ] **Step 4: Write the codec and records, with a round-trip test**

`SharpMUSH.Database.Lightning/Store/Codec.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpMUSH.Database.Lightning.Records;

namespace SharpMUSH.Database.Lightning.Store;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(ObjectRecord))]
[JsonSerializable(typeof(AttrMetaRecord))]
[JsonSerializable(typeof(FlagRecord))]
[JsonSerializable(typeof(PowerRecord))]
[JsonSerializable(typeof(AttributeFlagRecord))]
[JsonSerializable(typeof(AttributeEntryRecord))]
[JsonSerializable(typeof(ChannelRecord))]
[JsonSerializable(typeof(ChannelMemberRecord))]
[JsonSerializable(typeof(MailRecord))]
[JsonSerializable(typeof(AccountRecord))]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(ServerStateRecord))]
[JsonSerializable(typeof(WikiPageRecord))]
[JsonSerializable(typeof(WikiRevisionRecord))]
[JsonSerializable(typeof(WikiTranslationRecord))]
[JsonSerializable(typeof(ApplicationRecord))]
[JsonSerializable(typeof(RoleRecord))]
[JsonSerializable(typeof(MigrationRecord))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal partial class LightningJsonContext : JsonSerializerContext;

public static class Codec
{
	public static byte[] Serialize<T>(T value)
		=> JsonSerializer.SerializeToUtf8Bytes(value, (JsonTypeInfo<T>)LightningJsonContext.Default.GetTypeInfo(typeof(T))!);

	public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
		=> JsonSerializer.Deserialize(bytes, (JsonTypeInfo<T>)LightningJsonContext.Default.GetTypeInfo(typeof(T))!)!;
}
```

Add every package record type to the context as you create it. `MigrationRecord { string Id; long AppliedUnixMs; }`.

`SharpMUSH.Tests/Database/Lightning/CodecTests.cs`:

```csharp
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Tests.Database.Lightning;

public class CodecTests
{
	[Test]
	public async Task ObjectRecordRoundTripsIncludingLocks()
	{
		var record = new ObjectRecord
		{
			Name = "God", Type = "PLAYER", Aliases = ["#1"], CreationTime = 1, ModifiedTime = 2, Quota = 999999,
			Locks = new() { ["Basic"] = new LockRecord { LockString = "#TRUE", Flags = "" } }
		};
		var back = Codec.Deserialize<ObjectRecord>(Codec.Serialize(record));
		await Assert.That(back).IsEqualTo(record with { Locks = back.Locks });
		await Assert.That(back.Locks["Basic"].LockString).IsEqualTo("#TRUE");
		await Assert.That(back.PasswordHash).IsNull();
	}
}
```

- [ ] **Step 5: Run all Lightning tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Tests/*" --treenode-filter "/*/SharpMUSH.Tests.Database.Lightning/*/*" > /tmp/claude-1000/task4-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task4-green.log | tail -3`
Expected: streaming and codec tests pass with the earlier ones.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database.Lightning SharpMUSH.Tests/Database/Lightning
git commit -m "Stream Lightning ranges in bounded pages and encode records with a generated JSON context

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Hoist the seed data into the shared project

**Files:**
- Create: `SharpMUSH.Database/Seed/FlagSeed.cs`, `AttributeFlagSeed.cs`, `PowerSeed.cs`, `AttributeEntrySeed.cs`, `InitialObjectSeed.cs`
- Test: `SharpMUSH.Tests/Database/Lightning/SeedDataTests.cs`
- Source of truth to copy from: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs` — flags `405-498`, attribute flags `551-597`, powers `598-657`, attribute entries `658-834`, objects and edges `276-404`.

**Interfaces:**
- Produces, all in namespace `SharpMUSH.Database.Seed`:
  - `static class FlagSeed { static readonly (string Name, string Symbol, string[]? Aliases, string[] SetPerms, string[] UnsetPerms, string[] TypeRestrictions)[] Flags; }`
  - `static class AttributeFlagSeed { static readonly (string Name, string Symbol, bool Inheritable)[] Flags; }`
  - `static class PowerSeed { static readonly (string Name, string Alias, string[] SetPerms, string[] UnsetPerms)[] Powers; }`
  - `static class AttributeEntrySeed { static readonly (string Name, string[] DefaultFlags)[] Entries; }`
  - `static class InitialObjectSeed { sealed record SeedObject(long Dbref, string Name, string Type, long? Location, long? Home, long Owner, string[] Flags, long Quota); static readonly SeedObject[] Objects; }` with the ten objects #0–#9 exactly as the SurrealDB migration seeds them (names, types, locations, homes, owners, flags such as `WIZARD` on #8 and #9, quota 999999 on #1).

- [ ] **Step 1: Write the failing test**

`SharpMUSH.Tests/Database/Lightning/SeedDataTests.cs`:

```csharp
using SharpMUSH.Database.Seed;

namespace SharpMUSH.Tests.Database.Lightning;

public class SeedDataTests
{
	[Test]
	public async Task SeedCountsMatchTheProviders()
	{
		await Assert.That(FlagSeed.Flags.Length).IsEqualTo(61);
		await Assert.That(AttributeFlagSeed.Flags.Length).IsEqualTo(26);
		await Assert.That(PowerSeed.Powers.Length).IsEqualTo(36);
		await Assert.That(AttributeEntrySeed.Entries.Length).IsEqualTo(153);
		await Assert.That(InitialObjectSeed.Objects.Select(o => o.Dbref)).IsEquivalentTo(Enumerable.Range(0, 10).Select(i => (long)i));
	}

	[Test]
	public async Task SeedNamesAreUnique()
	{
		await Assert.That(FlagSeed.Flags.Select(f => f.Name).Distinct().Count()).IsEqualTo(FlagSeed.Flags.Length);
		await Assert.That(AttributeEntrySeed.Entries.Select(e => e.Name).Distinct().Count()).IsEqualTo(AttributeEntrySeed.Entries.Length);
	}
}
```

If a count differs from the SurrealDB source after copying, the source is right and the test number is wrong: fix the test to the counted value and note it in the commit message.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SeedDataTests/*" > /tmp/claude-1000/task5-red.log 2>&1; grep -E "error CS" /tmp/claude-1000/task5-red.log | head -3`
Expected: `FlagSeed` not found.

- [ ] **Step 3: Copy the arrays verbatim**

Create each file with the array copied byte-for-byte from the SurrealDB line ranges above, wrapped as a `public static readonly` field with the tuple element names shown in the Interfaces block. For `InitialObjectSeed`, read `SurrealDatabase.Migration.cs:276-404` and encode each object as a `SeedObject`; the `at_location`, `has_home` and `has_owner` edge lists become the `Location`, `Home` and `Owner` members. Rooms have `Location = null` and `Home = null`. #7 (Package Manager) owns itself.

- [ ] **Step 4: Run to verify it passes, then commit**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SeedDataTests/*" > /tmp/claude-1000/task5-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task5-green.log | tail -2`

```bash
git add SharpMUSH.Database/Seed SharpMUSH.Tests/Database/Lightning/SeedDataTests.cs
git commit -m "Share the initial seed data from the database project

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Provider skeleton, migration, and host wiring

**Files:**
- Create: `SharpMUSH.Database.Lightning/LightningDatabase.cs` and one partial per interface area listed in spec §4, each member throwing `NotImplementedException` for now.
- Create: `SharpMUSH.Database.Lightning/Migration/LightningMigration.cs`
- Create: `SharpMUSH.Library/Plugins/Storage/ILightningStorageAccessor.cs`
- Modify: `SharpMUSH.Library/Plugins/IMigrationSource.cs` (add `LightningSteps`), `SharpMUSH.Library/Definitions/DatabaseProvider.cs` (add `Lightning`), `SharpMUSH.Server/Program.cs:27-32`, `SharpMUSH.Server/Startup.cs` (new branch after the SurrealDB one at `167-192`; registration of the accessor), `SharpMUSH.Server/SharpMUSH.Server.csproj`, `SharpMUSH.Tests.Infrastructure/ServerWebAppFactory.cs:208-230`, `SharpMUSH.Tests.Infrastructure/SharpMUSH.Tests.Infrastructure.csproj`, `SharpMUSH.Plugins.Scene/Storage/SceneSystemServiceCollectionExtensions.cs` (add `LightningKey`; the storage class itself comes in Task 19, register a placeholder that throws a clear message until then), `CLAUDE.md:50`.
- Test: `SharpMUSH.Tests/Database/Lightning/MigrationTests.cs`

**Interfaces:**
- Consumes: store layer (Tasks 2–4), seeds (Task 5).
- Produces:
  - `public sealed partial class LightningDatabase(ILogger<LightningDatabase> logger, LightningStoreOptions options, IPasswordService passwordService, IReadOnlyList<IMigrationSource>? migrationSources = null, IReadOnlyList<PluginFlag>? pluginFlags = null) : ISharpDatabase, IWikiService, IPackageRegistryService, IRoleRegistryService, ILayoutRegistryService, IApplicationRegistryService, ILightningStorageAccessor` with `internal LightningStore Store { get; }`.
  - `interface ILightningStorageAccessor { T Read<T>(Func<ITx, T> read); ValueTask<T> WriteAsync<T>(Func<ITx, T> job, CancellationToken ct = default); IAsyncEnumerable<(byte[] Key, byte[] Value)> RangeAsync(TableDef table, byte[] prefix, int pageSize = 256, CancellationToken ct = default); TableDef OpenTable(string name, bool duplicates); }` in `SharpMUSH.Library/Plugins/Storage/`, using the `ITx`, `TableDef` and `Keys` types that Tasks 1–2 placed in `SharpMUSH.Library.Plugins.Storage.Lightning`. `OpenTable` opens (creating if needed) a plugin-owned table in a write job and appends it to the store's handle map; scene tables are opened this way in Task 19, so `Tables` holds no `scene.*` entries.
  - `IMigrationSource.LightningSteps => Enumerable.Empty<LightningMigrationStep>()` with `sealed record LightningMigrationStep(string Id, Func<ILightningStorageAccessor, ValueTask> Apply)`.
  - Migration id keys in `meta`: `Keys.Str("mig:" + id)` → `MigrationRecord`; counter key `Keys.Str("next_dbref")` → 8-byte big-endian.

- [ ] **Step 1: Write the failing migration test**

`SharpMUSH.Tests/Database/Lightning/MigrationTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database.Lightning;

public class MigrationTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>());

	[Test]
	public async Task MigrateSeedsTheWorldOnceAndIsIdempotent()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);
		await db.Migrate();
		await db.Migrate();
		await Assert.That(await db.GetObjectCountAsync()).IsEqualTo(10);
		var god = await db.GetObjectNodeAsync(new DBRef(1));
		await Assert.That(god.IsPlayer).IsTrue();
		await Assert.That(god.Known.Object().Name).IsEqualTo("God");
		await Assert.That(db.Store.Count(Tables.Flag)).IsEqualTo(61);
		await Assert.That(db.Store.Count(Tables.AttrEntry)).IsEqualTo(153);
		var next = db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : -1);
		await Assert.That(next).IsEqualTo(10);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MigrationTests/*" > /tmp/claude-1000/task6-red.log 2>&1; grep -E "error CS" /tmp/claude-1000/task6-red.log | head -3`
Expected: `LightningDatabase` not found.

- [ ] **Step 3: Write the class and the migration**

`SharpMUSH.Database.Lightning/LightningDatabase.cs` holds the constructor, `Store`, the static `MigrateLock = new SemaphoreSlim(1, 1)`, `Migrate()`, `WipeDatabaseAsync()`, `CreateStagingAsync()` (throws until Task 18), and shared helpers:

```csharp
internal static long ParseDbref(string id) => long.Parse(id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id.TrimStart('#'));
internal long AllocateDbref(ITx tx)
{
	var current = tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : 0;
	tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(current + 1));
	return current;
}
internal static void PutEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long to)
{
	tx.Put(edge.Forward, Keys.Dbref(from), Keys.Dbref(to));
	tx.Put(edge.Reverse, Keys.Dbref(to), Keys.Dbref(from));
}
internal static void DeleteEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long to)
{
	tx.Delete(edge.Forward, Keys.Dbref(from), Keys.Dbref(to));
	tx.Delete(edge.Reverse, Keys.Dbref(to), Keys.Dbref(from));
}
/// <summary>Single-valued relations (location, home, owner, parent, zone): replace whatever is there.</summary>
internal static void SetSingleEdge(ITx tx, (TableDef Forward, TableDef Reverse) edge, long from, long? to)
{
	foreach (var old in tx.Dups(edge.Forward, Keys.Dbref(from)).ToList())
		DeleteEdge(tx, edge, from, Keys.ReadDbref(old));
	if (to is { } t) PutEdge(tx, edge, from, t);
}
internal static long? GetSingleEdge(ITx tx, TableDef forward, long from)
	=> tx.Dups(forward, Keys.Dbref(from)).Select(v => (long?)Keys.ReadDbref(v)).FirstOrDefault();
```

`Migrate()` in `Migration/LightningMigration.cs`, in this order under `MigrateLock`:
1. Upsert every `FlagSeed.Flags` entry into `Tables.Flag` keyed `Keys.Upper(name)` as `FlagRecord { System = true }`, preserving an existing record's `Disabled`; same for `AttributeFlagSeed` → `Tables.AttrFlag`, `PowerSeed` → `Tables.Power`, `AttributeEntrySeed` → `Tables.AttrEntry`, and `pluginFlags` → `Tables.Flag`.
2. If `meta["mig:0001_initial_seed"]` is absent: for each `InitialObjectSeed.Objects` put `Tables.Obj` (`Locks = {}`, timestamps now, `PasswordHash/Salt = null`), `Tables.ObjName` for lower name, edges via `SetSingleEdge` for location/home/owner, and `Tables.ObjFlag` pairs for each flag name; set `next_dbref` to 10; write the migration record.
3. For each `IMigrationSource` step whose `mig:<Id>` is absent, `await step.Apply(this)` then record it.
4. The ancestor-format seed (`AncestorSeed.SeedAncestorPlayerFormatsAsync`) needs `SetAttributeAsync`, which Task 9 delivers; Task 9 adds it under its own migration id `0002_ancestor_formats`. This task does not call it.
5. Recompute `next_dbref` as (last key in `Tables.Obj`) + 1 when larger than the stored value.
6. Ensure `Tables.State["state"]` exists as a default `ServerStateRecord`.

Every step runs inside `Store.WriteAsync`. `WipeDatabaseAsync`: `Store.Close()`, delete the directory, `Store.Reopen()`, `Migrate()`.

- [ ] **Step 4: Wire the host**

`DatabaseProvider.cs`: add `Lightning` with a doc comment "LMDB embedded in-process through Lightning.NET; one directory per world."

`Program.cs:27-32`: extend the chain with `"lightning"` → `DatabaseProvider.Lightning`.

`Startup.cs`, after the SurrealDB branch:

```csharp
else if (databaseProvider == DatabaseProvider.Lightning)
{
	var lightningPath = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH")
		?? configuration["Lightning:Path"]
		?? "lightning-data";
	var mapSize = long.TryParse(Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_MAPSIZE"), out var parsed)
		? parsed
		: 64L << 30;
	services.AddSingleton<ISharpDatabase, LightningDatabase>(x =>
	{
		var dbLogger = x.GetRequiredService<ILogger<LightningDatabase>>();
		var password = x.GetRequiredService<IPasswordService>();
		var db = new LightningDatabase(dbLogger,
			new LightningStoreOptions { Path = lightningPath, MapSize = mapSize },
			password, pluginMigrationSources, pluginFlags);
		db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
		return db;
	});
	services.AddSingleton<ILightningStorageAccessor>(sp => (ILightningStorageAccessor)sp.GetRequiredService<ISharpDatabase>());
}
```

`ServerWebAppFactory.cs`: add `useLightning` and, when set, `SHARPMUSH_LIGHTNING_PATH` = a fresh `Path.Combine(Path.GetTempPath(), "sharpmush-lightning-tests-" + Guid)` unless already set; delete it in `DisposeAsync`. `SceneSystemServiceCollectionExtensions`: `LightningKey = "lightning"`, a switch arm, and a keyed registration whose factory throws `NotSupportedException("Lightning scene storage arrives in Task 19")` for now. Add project references to `SharpMUSH.Server.csproj` and `SharpMUSH.Tests.Infrastructure.csproj`. `CLAUDE.md:50`: list `lightning` and the two env vars.

- [ ] **Step 5: Build the solution, run the migration test, and boot the server under lightning**

Run: `dotnet build SharpMUSH.sln -p:SkipFormatVerification=true -nologo -v:q 2>&1 | grep -E " error |Build succeeded" | head`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MigrationTests/*" > /tmp/claude-1000/task6-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task6-green.log | tail -2`
Run a smoke test that the factory boots: `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ObjectSearchFilterPushdownTests/*" > /tmp/claude-1000/task6-boot.log 2>&1; grep -E "NotImplementedException|failed|passed|Migrate" /tmp/claude-1000/task6-boot.log | head`
Expected: the factory constructs and migrates; tests fail with `NotImplementedException` from unported members, not from startup.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database.Lightning SharpMUSH.Library SharpMUSH.Server SharpMUSH.Tests.Infrastructure SharpMUSH.Plugins.Scene/Storage/SceneSystemServiceCollectionExtensions.cs CLAUDE.md SharpMUSH.Tests/Database/Lightning/MigrationTests.cs
git commit -m "Boot SharpMUSH on the Lightning provider with its seeded world

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Objects

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Objects.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Objects.cs` (all members except `GetFilteredObjectsAsync`, which is Task 12) and the hydration helpers in `SurrealDatabase.cs:600-1075` that build `SharpObject`/`SharpPlayer`/`SharpRoom`/`SharpThing`/`SharpExit` with their lazy edges.
- Contract: `ISharpDatabase.cs:40-179, 285-291, 424-554, 601-635`.
- Test: `SharpMUSH.Tests/Database/Lightning/ObjectsTests.cs` plus the existing suite classes `CreateObjectTests`, `DestroyCommandTests`, `SetObjectTests` (find them with `grep -rl "CreateThingAsync\|DeleteObjectAsync" SharpMUSH.Tests --include='*.cs'`).

**Interfaces:**
- Consumes: store helpers from Task 6 (`AllocateDbref`, `PutEdge`, `SetSingleEdge`, `GetSingleEdge`), `ObjectRecord`, `Codec`.
- Produces: `internal AnySharpObject Hydrate(ITx tx, long dbref, ObjectRecord record)` — builds the Library model with lazy loaders for owner, location, home, parent, zone, flags and powers; every lazy loader opens its own `Store.Read` at call time. Also `internal (long Dbref, ObjectRecord Record)? ReadObject(ITx tx, long dbref)`.

- [ ] **Step 1: Write the failing provider test**

`SharpMUSH.Tests/Database/Lightning/ObjectsTests.cs` (same `Create(path)` helper as `MigrationTests`, migrated in a `[Before(Test)]`):

```csharp
[Test]
public async Task CreateThingWiresNameOwnerLocationAndHome()
{
	var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
	var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
	var dbref = await _db.CreateThingAsync("Widget", room, room, god);
	var thing = (await _db.GetObjectNodeAsync(dbref)).Known;
	await Assert.That(thing.Object().Name).IsEqualTo("Widget");
	await Assert.That((await thing.AsContent.Location()).Object().DBRef.Number).IsEqualTo(2);
	await Assert.That((await thing.Object().Owner.WithCancellation(CancellationToken.None)).Object().DBRef.Number).IsEqualTo(1);
	await Assert.That(await _db.GetOwnedObjectCountAsync(new DBRef(1))).IsGreaterThanOrEqualTo(9);
}

[Test]
public async Task DeleteObjectRemovesEveryEdgeInBothDirections()
{
	var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
	var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
	var dbref = await _db.CreateThingAsync("Doomed", room, room, god);
	await _db.SetAttributeAsync(dbref, ["DESC"], MModule.single("gone"), god);
	await _db.DeleteObjectAsync(dbref);
	await Assert.That((await _db.GetObjectNodeAsync(dbref)).IsNone).IsTrue();
	var n = dbref.Number;
	var leftovers = _db.Store.Read(tx => Tables.EdgePairs.Sum(p =>
		tx.Dups(p.Forward, Keys.Dbref(n)).Count() + tx.Dups(p.Reverse, Keys.Dbref(n)).Count())
		+ tx.Range(Tables.AttrMeta, Keys.AttrPrefix(n)).Count()
		+ tx.Range(Tables.RevLocation, Keys.Dbref(n)).Count());
	await Assert.That(leftovers).IsEqualTo(0);
}
```

(The attribute line depends on Task 9; keep it and expect `NotImplementedException` until then, or order Task 9 before running this test's second case.)

- [ ] **Step 2: Run to verify red**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ObjectsTests/*" > /tmp/claude-1000/task7-red.log 2>&1; grep -E "NotImplemented|failed|passed" /tmp/claude-1000/task7-red.log | head`
Expected: failures with `NotImplementedException`.

- [ ] **Step 3: Port the members**

Rules for this partial:
- `Create{Player,Room,Thing,Exit}Async`: one `WriteAsync` job: `AllocateDbref`, put `Obj` record (`Type` from `DatabaseConstants.Type*`, `Locks = {}`, timestamps `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`), put `ObjName` for lower name and every alias, `SetSingleEdge` for owner, location, home as the SurrealDB version does (players: location and home; rooms: none; things: location and home; exits: location = source room, plus `Tables.Exit` from room to exit, and destination through the same edge the SurrealDB provider uses — read `SurrealDatabase.Objects.cs` `CreateExitAsync` and `GetEntrancesAsync` and mirror exactly). Password hashing through `passwordService` exactly as `SurrealDatabase.Objects.cs` does for players.
- `GetObjectNodeAsync(DBRef)`: `Read` → `ReadObject` → `Hydrate`; honour the creation-timestamp check in the contract (`ISharpDatabase.cs:285-291`) the same way SurrealDB does.
- `GetBaseObjectNodeAsync`, `GetAllObjectsAsync`, `GetAllTypedObjectsAsync`, `GetAllPlayersAsync`: `RangeAsync(Tables.Obj, [])` wrapped in `FreshAsyncEnumerable`, hydrating per entry inside a `Read`.
- `GetPlayerByNameOrAliasAsync`: `Dups(Tables.ObjName, Keys.Lower(name))` → filter `Type == PLAYER`.
- `GetObjectCountAsync`: `Store.Count(Tables.Obj)`. `GetOwnedObjectCountAsync`: `Dups(Tables.Owner.Reverse, Keys.Dbref(owner)).Count()`.
- `SetObjectNameAsync`: update record and `ObjName` (delete old name keys, add new). `SetObjectParentAsync`/`SetObjectZoneAsync`/`SetObjectOwnerAsync`: `SetSingleEdge`. `SetObjectWarningsAsync`, `SetLockAsync`, `UnsetLockAsync`: record update.
- `DeleteObjectAsync`: one job, in this order: read record; delete `ObjName` keys for name and aliases; `DeletePrefix(AttrMeta)` and `DeletePrefix(AttrVal)` under `AttrPrefix(n)`; `DeletePrefix(ExpandedObj, Keys.Composite(n, ""))`; for each mail id in `Range(MailBox, Keys.Dbref(n))` delete `Mail`, `MailSent` entry, and the box entry; for each `(f, r)` in `Tables.EdgePairs`: for each `t` in `Dups(f, key)` delete `r[t]` value `key`; `DeletePrefix(f, key)`; for each `s` in `Dups(r, key)` delete `f[s]` value `key`; `DeletePrefix(r, key)`; then `Delete(Obj, key)`. Channel membership: `Range(RevChanMember, key)` → delete `ChanMember` composite and the reverse entry.
- `Hydrate` builds the same model objects the SurrealDB provider builds in `SurrealDatabase.cs` (search for `new SharpObject`, `new SharpPlayer`, etc.), with `AsyncLazy`/`Lazy` loaders that call `Store.Read` when invoked.

- [ ] **Step 4: Run the provider test and the existing object-related suites under lightning**

Run: `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ObjectsTests/*" > /tmp/claude-1000/task7-green.log 2>&1; grep -E "failed|passed" /tmp/claude-1000/task7-green.log | tail -2`
Expected: pass (the attribute-dependent case may stay red until Task 9; say so in the commit).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Database.Lightning SharpMUSH.Tests/Database/Lightning/ObjectsTests.cs
git commit -m "Store objects and their edges in Lightning

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Flags and powers

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.FlagsAndPowers.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.FlagsAndPowers.cs`
- Contract: `ISharpDatabase.cs:293-422, 496-524, 756`
- Test: existing `SharpMUSH.Tests/Database/FlagUnsetPermissionTests.cs` (skip its Arango-only parts), `FlagSeedIntegrityTests.cs` (Arango-gated; ensure it self-skips), and the command suites `FlagCommandTests`/`PowerCommandTests` (`grep -rl "@set\|@power" SharpMUSH.Tests/Commands`).

**Interfaces:**
- Consumes: `FlagRecord`, `PowerRecord`, `Tables.Flag`, `Tables.Power`, `Tables.ObjFlag`, `Tables.ObjPower`.
- Produces: `internal IEnumerable<SharpObjectFlag> ReadObjectFlags(ITx tx, long dbref)` used by `Hydrate` and by Task 12's flag predicate.

- [ ] **Step 1: Red** — run `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/FlagUnsetPermissionTests/*" > /tmp/claude-1000/task8-red.log 2>&1` and confirm `NotImplementedException`.
- [ ] **Step 2: Port** — definitions by upper-cased name (`GetObjectFlagAsync` matches name or any alias case-insensitively, as SurrealDB does); `GetObjectFlagsAsync(id, type)` reads `Dups(ObjFlag.Forward, key)` and joins to `Tables.Flag`; set/unset write the `ObjFlag`/`ObjPower` pairs where the reverse key is `Keys.Upper(name)` and the reverse value the dbref (both tables `DuplicatesSort`, not fixed); `SetObjectFlagDisabledAsync`/`SetPowerDisabledAsync` refuse system entries exactly as the SurrealDB version does; `CreateObjectFlagAsync`/`DeleteObjectFlagAsync`/powers likewise.
- [ ] **Step 3: Green** — rerun the classes above; all pass under lightning.
- [ ] **Step 4: Commit** — `git add SharpMUSH.Database.Lightning` and commit "Store flags and powers in Lightning".

---

### Task 9: Attributes

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Attributes.cs`, `Migration/LightningMigration.cs` (add the `0002_ancestor_formats` gated `AncestorSeed` call)
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Attributes.cs` (everything except the inheritance members, which are Task 10) and the glob-to-regex conversion in `SharpMUSH.Database.ArangoDB/ArangoDatabase.Attributes.cs:297-344` (backtick-aware: `**` → `.*`, `*` → `[^`]*`, `?` → `.`, trailing backtick → direct children only).
- Contract: `ISharpDatabase.cs:181-282, 637-727`. Read the ordering contract at `196-216`: regex results must be parent-before-child by long name.
- Test: `SharpMUSH.Tests/Database/Lightning/AttributesTests.cs` plus the existing `AttributeTests`, `LattrTests`, `WipeAttributeTests`, `AttributeFlagTests` classes (`grep -rl "SetAttributeAsync\|lattr" SharpMUSH.Tests --include='*.cs' | head`).

**Interfaces:**
- Consumes: `AttrMetaRecord`, `Tables.AttrMeta`, `Tables.AttrVal`, `Tables.AttrFlag`, `Tables.AttrEntry`, `Keys.Attr*`.
- Produces: `internal SharpAttribute HydrateAttribute(ITx tx, long dbref, string longName, AttrMetaRecord meta, byte[]? value)`; `internal static Regex GlobToRegex(string pattern)`; `internal IReadOnlyList<(string LongName, AttrMetaRecord Meta)> ReadPathPrefixes(ITx tx, long dbref, string[] path)` (returns fewer entries than `path.Length` when the walk stops early).

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task SetCreatesAncestorsAndMarksBranch()
{
	var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
	await _db.SetAttributeAsync(new DBRef(1), ["FOO", "BAR", "BAZ"], MModule.single("deep"), god);
	var path = await _db.GetAttributeAsync(new DBRef(1), ["FOO", "BAR", "BAZ"]);
	await Assert.That(path.Count()).IsEqualTo(3);
	var foo = await _db.GetAttributeAsync(new DBRef(1), ["FOO"]);
	await Assert.That((await foo.First().Flags.ToListAsync()).Select(f => f.Name)).Contains("branch");
	var partial = await _db.GetAttributeAsync(new DBRef(1), ["FOO", "NOPE"]);
	await Assert.That(partial).IsEmpty();
}

[Test]
public async Task WildcardListingStaysWithinOneLevelUnlessDoubleStar()
{
	var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
	await _db.SetAttributeAsync(new DBRef(1), ["FOO", "A"], MModule.single("1"), god);
	await _db.SetAttributeAsync(new DBRef(1), ["FOO", "A", "B"], MModule.single("2"), god);
	await _db.SetAttributeAsync(new DBRef(1), ["FOX"], MModule.single("3"), god);
	var one = (await _db.GetAttributesAsync(new DBRef(1), "FO*")).Select(a => a.LongName).ToList();
	await Assert.That(one).IsEquivalentTo(new[] { "FOO", "FOX" });
	var all = (await _db.GetAttributesAsync(new DBRef(1), "FOO`**")).Select(a => a.LongName).ToList();
	await Assert.That(all).IsEquivalentTo(new[] { "FOO`A", "FOO`A`B" });
	var regex = (await _db.GetAttributesByRegexAsync(new DBRef(1), "^FOO.*")).Select(a => a.LongName).ToList();
	await Assert.That(regex).IsEquivalentTo(new[] { "FOO", "FOO`A", "FOO`A`B" });
}
```

Adjust the exact return shapes to the interface (`GetAttributeAsync` returns the path; `GetAttributesAsync` returns the matches) after reading the contract; the assertions above express the semantics.

- [ ] **Step 2: Red** — run `AttributesTests` under lightning; expect `NotImplementedException`.
- [ ] **Step 3: Port**
  - `SetAttributeAsync`: one job. Walk the path prefixes; for each missing prefix put `AttrMeta` with `Owner = owner dbref`, `Flags` = default flags from `Tables.AttrEntry` when the leaf name has an entry (mirror `SurrealDatabase.Attributes.cs:250-330`), `AttrVal` = `MModule.serialize(MModule.empty())` for created ancestors and the given value for the leaf; when a child was created, add `branch` to the parent's `Flags` if absent. Preserve an existing leaf's flags and owner exactly as SurrealDB does (check its behaviour on overwrite and match it).
  - `GetAttributeAsync`: `ReadPathPrefixes`; return empty when shorter than the path.
  - `GetAttributesAsync(pattern)`: compute the literal prefix of the glob (characters before the first `*`, `?`), range `AttrPrefix(dbref, literalPrefix)`, filter `GlobToRegex(pattern).IsMatch(longName)`, stream via `FreshAsyncEnumerable` over `RangeAsync`, reading `AttrVal` per match. `GetAttributesByRegexAsync`: range the whole `AttrPrefix(dbref)` with `new Regex(pattern, IgnoreCase | CultureInvariant)`; the range order is the long-name order the contract needs.
  - Lazy variants return `LazySharpAttribute` whose value loader does a `Store.Read` point read of `AttrVal`.
  - `ClearAttributeAsync`: if children exist (`Range(AttrMeta, Attr(dbref, longName + "`"))` non-empty) set value to empty, else delete meta and value and remove `branch` from the parent when it has no other children. `WipeAttributeAsync`: `DeletePrefix` on `Attr(dbref, longName)` and `Attr(dbref, longName + "`")` for both tables, then the parent branch check.
  - `ReassignAttributeOwnerAsync`: range every `AttrMeta` (whole table) and rewrite `Owner` where it matches; this is the one full-table write, document it as such.
  - Attribute flags set/unset/get; attribute entries CRUD keyed `Keys.Upper(name)`.
  - Migration: add the gated ancestor seed under `mig:0002_ancestor_formats`.
- [ ] **Step 4: Green** — run `AttributesTests`, `MigrationTests`, `ObjectsTests` and the existing attribute suites under lightning.
- [ ] **Step 5: Commit** — "Store the attribute tree as a flat ordered keyspace in Lightning".

---

### Task 10: Attribute inheritance

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Attributes.cs`
- Semantic source: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Attributes.cs:886-1096` (the single-query version and the C# evaluation `EvaluateInheritanceCandidateAsync`), `SharpMUSH.Database.SurrealDB/SurrealDatabase.Attributes.cs` inheritance members.
- Contract: `ISharpDatabase.cs:228-247`.
- Test: existing `AttributeInheritanceTests`/`ParentTests`/`ZoneTests` (`grep -rl "GetAttributeWithInheritance\|@parent" SharpMUSH.Tests --include='*.cs' | head`).

- [ ] **Step 1: Red** — run those classes under lightning; expect `NotImplementedException`.
- [ ] **Step 2: Port** — in one `Read`: build `chain = [self] + parents` by following `Tables.Parent.Forward` up to 100 hops with a visited set; `zonesByChainMember` by following `Tables.Zone.Forward` per member; for each candidate object call `ReadPathPrefixes(tx, dbref, path)`; return the same three candidate groups the Arango C# post-processing consumes and reuse that evaluation logic (copy `EvaluateInheritanceCandidateAsync` into the provider if it is private in Arango; do not reference the Arango project). Lazy variant defers value reads.
- [ ] **Step 3: Green** — rerun the classes.
- [ ] **Step 4: Commit** — "Resolve inherited attributes through parent and zone chains in Lightning".

---

### Task 11: Navigation

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Navigation.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Navigation.cs`, and for exits/entrances/homed-at `SurrealDatabase.Objects.cs` (search `GetEntrancesAsync`, `GetHomedAtAsync`, `MoveObjectAsync`, `IsReachableViaParentOrZoneAsync`).
- Contract: `ISharpDatabase.cs:504-532, 611-635, 728-777, 872-881`.
- Test: `SharpMUSH.Tests/Database/Lightning/NavigationTests.cs` plus existing `LookCommandTests`, `MoveTests`, `TeleportTests`, `ParentCycleTests` (`grep -rl "IsReachableViaParentOrZone\|MoveObjectAsync\|GetContentsAsync" SharpMUSH.Tests --include='*.cs' | head`).

- [ ] **Step 1: Write the failing cycle test**

```csharp
[Test]
public async Task ReachabilityFollowsParentsAndZonesWithoutLooping()
{
	var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
	var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
	var a = await _db.CreateThingAsync("A", room, room, god);
	var b = await _db.CreateThingAsync("B", room, room, god);
	var c = await _db.CreateThingAsync("C", room, room, god);
	await _db.SetObjectParentAsync(a, b);
	await _db.SetObjectZoneAsync(b, c);
	await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, c, 100)).IsTrue();
	await Assert.That(await _db.IsReachableViaParentOrZoneAsync(c, a, 100)).IsFalse();
	await _db.SetObjectParentAsync(c, a);
	await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, a, 100)).IsTrue();
}
```

- [ ] **Step 2: Red**, **Step 3: Port** — `GetLocationAsync(obj, depth)` loops `GetSingleEdge(Location.Forward)`; `depth == -1` means until none. Contents/exits/entrances/homed-at/objects-by-zone are `Dups` over the reverse tables hydrated inside the same `Read`, streamed via `FreshAsyncEnumerable` when the contract returns `IAsyncEnumerable`. `GetHomedAtAsync` excludes rooms as the contract says. `GetParentsAsync` walks with a visited set. `IsReachableViaParentOrZoneAsync` is BFS with a `HashSet<long>` visited set over `Parent.Forward` and `Zone.Forward`, stopping at `maxDepth`. `MoveObjectAsync` = `SetSingleEdge(Location, obj, destination)`.
- [ ] **Step 4: Green** — run `NavigationTests` and the existing classes under lightning.
- [ ] **Step 5: Commit** — "Walk locations, exits, parents and zones over Lightning edge tables".

---

### Task 12: Filtered object search

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Objects.cs`
- Semantic source: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Objects.cs:776-902` (the predicate list) and `SharpMUSH.Library/Models/ObjectSearchFilter.cs`.
- Contract: `ISharpDatabase.cs:561-601` — every populated predicate must be honoured; the doc comment explains why.
- Test: existing `SharpMUSH.Tests/Database/ObjectSearchFilterPushdownTests.cs` — this is the gate.

- [ ] **Step 1: Red** — `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ObjectSearchFilterPushdownTests/*" > /tmp/claude-1000/task12-red.log 2>&1; grep -cE "NotImplemented" /tmp/claude-1000/task12-red.log` — expect non-zero.
- [ ] **Step 2: Port** — `FreshAsyncEnumerable` over `RangeAsync(Tables.Obj, [])` bounded to `[Keys.Dbref(MinDbRef), Keys.Dbref(MaxDbRef)]` when given (start the range at the min key and stop when the key exceeds max). For each entry decode `ObjectRecord` and evaluate in a `Read`: `Types` contains `Type`; `NamePattern` with `UseRegex` → `Regex(pattern, IgnoreCase)`, else `Name.Contains(pattern, OrdinalIgnoreCase)`; `Owner` → `GetSingleEdge(Owner.Forward)` equals the filter's dbref (the contract's two-hop note collapses to one read here because `obj` stores the player itself); `Zone`/`Parent` → single-edge equality; `HasFlag` → `ReadObjectFlags` name or alias case-insensitive OR the object's own `Type` equals the flag text; `HasPower` → name or alias; then `Skip`/`Limit` applied while streaming. Hydrate matches through `Hydrate`.
- [ ] **Step 3: Green** — the pushdown suite passes under lightning; record the pass count in the commit message.
- [ ] **Step 4: Commit** — "Push every object search predicate down into the Lightning scan".

---

### Task 13: Channels

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Channels.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Channels.cs`
- Contract: `ISharpDatabase.cs:832-870`; `CreateChannelAsync` must be atomic on the name and return the name-taken result.
- Test: existing `SharpMUSH.Tests/Database/ChannelUniquenessTests.cs` and the `@channel`/`@cemit` command suites (`grep -rl "CreateChannelAsync\|@channel" SharpMUSH.Tests --include='*.cs' | head`).

- [ ] **Step 1: Red** — run `ChannelUniquenessTests` under lightning; expect `NotImplementedException`.
- [ ] **Step 2: Port** — `ChannelRecord` keyed `Keys.Upper(name)` storing the markup fields via `MModule.serialize`; owner as a dbref field on the record (no edge table needed); `CreateChannelAsync` is one job: `TryGet` then `Put`, returning the taken result when present. Membership: `ChanMember` keyed `Keys.Composite(NAME, dbref)` → `ChannelMemberRecord`; `RevChanMember` keyed dbref → NAME. `GetChannelListAsync` streams `RangeAsync(Chan, [])`; `GetChannelAsync` point read; member listing decodes `Range(ChanMember, Keys.Str(NAME) + Sep)` and hydrates each member's object inside the same `Read`. Status updates rewrite the member record. Delete removes the record, every member entry and every reverse entry.
- [ ] **Step 3: Green** — rerun the classes above.
- [ ] **Step 4: Commit** — "Store channels and membership status in Lightning".

---

### Task 14: Mail

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Mail.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Mail.cs`
- Contract: `ISharpDatabase.cs:759-795`. Positional access (`@mail N`) is by ordinal within the recipient's folder.
- Test: existing mail suites (`grep -rl "SendMailAsync\|@mail" SharpMUSH.Tests --include='*.cs' | head`).

- [ ] **Step 1: Red** — run the mail classes under lightning.
- [ ] **Step 2: Port** — mail id from a second counter `meta["next_mail"]` allocated in the job; `Mail` keyed `Keys.Dbref(mailId)` (reuse the encoder) → `MailRecord` with `Sender`, `Recipient`, folder, flags and serialized subject/content; `MailBox` keyed `Keys.Concat(Keys.Dbref(recipient), Keys.Dbref(mailId))` → empty; `MailSent` keyed `Keys.Concat(Keys.Dbref(sender), Keys.Dbref(mailId))`. Listing a folder ranges `MailBox` under the recipient and filters by folder; positional access counts within that filtered sequence; folder listing is `Distinct()` over the same range; rename folder rewrites records; sent-mail members range `MailSent`; `GetAllSystemMailAsync` streams `Mail`. Mirror every flag transition (`Fresh`, `Read`, `Cleared`, `Tagged`, `Urgent`, `Forwarded`) exactly as the SurrealDB partial does.
- [ ] **Step 3: Green**, **Step 4: Commit** — "Store mail with per-recipient ordered indexes in Lightning".

---

### Task 15: Accounts, sessions, server state, expanded data

**Files:**
- Modify: `LightningDatabase.Accounts.cs`, `LightningDatabase.Sessions.cs`, `LightningDatabase.ServerState.cs`, `LightningDatabase.ExpandedData.cs`
- Semantic sources: the four SurrealDB partials of the same names.
- Contract: `ISharpDatabase.cs:797-830, 883-985`. `TouchSessionExpiryAsync` must be a no-op when the token is absent (see the contract's explanation at `966`).
- Test: existing account/session tests (`grep -rl "IAccountSessionStore\|CreateAccountAsync\|TouchSessionExpiry" SharpMUSH.Tests --include='*.cs' | head`) and `ExpandedData` tests.

- [ ] **Step 1: Red** — run those classes under lightning.
- [ ] **Step 2: Port** — Accounts: `Account` keyed by id; `AccountEmail` keyed `Keys.Lower(email)` only when email is present (sparse unique); `AccountUser` keyed `Keys.Lower(username)`; uniqueness checked with `TryGet` inside the job; `LinkCharacterToAccountAsync` writes `Tables.AccountChar` pairs (forward key `Keys.Str(accountId)`, value dbref; reverse key dbref, value account id bytes); accounts are never deleted, only status-changed. Sessions: `Session` keyed `Keys.Str(token)`; `SessionAccount` and `SessionIp` secondaries; delete-by-account and delete-by-ip iterate the secondaries inside the job; `GetSessionOriginIpsAsync` is `Distinct()` over `SessionIp` keys. Server state: single key. Expanded data: `ExpandedObj` keyed `Keys.Composite(dbref, type)`, `ExpandedSrv` keyed `Keys.Str(type)`; write = read existing JSON, merge non-null properties of the new value over it (`Dictionary<string, JsonElement>` merge as `SurrealDatabase.ExpandedData.cs:47-58`), put.
- [ ] **Step 3: Green**, **Step 4: Commit** — "Store accounts, sessions, server state and expanded data in Lightning".

---

### Task 16: Layouts, applications, roles, packages

**Files:**
- Modify: `LightningDatabase.Layouts.cs`, `LightningDatabase.Applications.cs`, `LightningDatabase.Roles.cs`, `LightningDatabase.Packages.cs`
- Semantic sources: the SurrealDB partials of the same names; contracts in `SharpMUSH.Library/Services/Interfaces/ILayoutRegistryService.cs`, `IApplicationRegistryService.cs`, `IRoleRegistryService.cs`, `IPackageRegistryService.cs`.
- Test: existing `RoleRegistryTests`, `LayoutRegistryTests`, `ApplicationRegistryTests`, `PackageRegistryTests` (`grep -rl "IRoleRegistryService\|IPackageRegistryService" SharpMUSH.Tests --include='*.cs' | head`) and the boot path: `RoleSeedService`, `DefaultPackagesBootstrapService`, `DefaultApplicationsBootstrapService` run at startup, so `ServerWebAppFactory` under lightning is itself the test.

- [ ] **Step 1: Red** — run those classes under lightning.
- [ ] **Step 2: Port** — all keyed CRUD: layouts by scope (whole `LayoutConfiguration` as JSON), applications by slug, roles by slug with permissions as a JSON map and `AccountRole` for membership, packages by the composite keys the SurrealDB partial uses for its seven record types (`PkgDep` is `DuplicatesSort`). List members stream via `FreshAsyncEnumerable` when the contract returns `IAsyncEnumerable`, otherwise materialise inside one `Read`.
- [ ] **Step 3: Green**, **Step 4: Commit** — "Store the portal registries in Lightning".

---

### Task 17: Wiki

**Files:**
- Modify: `SharpMUSH.Database.Lightning/LightningDatabase.Wiki.cs`
- Semantic source: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs` (865 lines) and `SharpMUSH.Library/Services/Interfaces/IWikiService.cs` (22 members, including revisions, locale streams and `WikiWriteConflict`).
- Test: `SharpMUSH.Tests/Wiki/*` and `SharpMUSH.Tests.Integration/Wiki/WikiServiceIntegrationTests.cs`, `WikiTranslationIntegrationTests.cs`.

- [ ] **Step 1: Red** — run the wiki classes under lightning in both test projects.
- [ ] **Step 2: Port** — `WikiPage` keyed by page id; `WikiSlug` keyed `Keys.Lower(namespace + "/" + category + "/" + slug)` (match the canonical identity in `IWikiService` docs); `WikiRev` keyed `Keys.Composite(pageId, locale, revisionNumber)` so a locale's revisions range in order and the latest is the last entry of the range; `WikiTr` keyed `Keys.Composite(pageId, locale)`. Write conflict: the job reads the current revision number for the locale and returns `WikiWriteConflict` when the caller's expected revision differs, else appends `expected + 1`. Draft visibility and author-sees-own-draft rules exactly as the SurrealDB partial.
- [ ] **Step 3: Green**, **Step 4: Commit** — "Store wiki pages, revisions and translations in Lightning".

---

### Task 18: Staging, wipe, backup, recovery and cascade coverage

**Files:**
- Create: `SharpMUSH.Database.Lightning/LightningStagingDatabase.cs`
- Modify: `LightningDatabase.cs` (`CreateStagingAsync`, `WipeDatabaseAsync`, `CopyToAsync`), `LightningStore.cs` (`SwapDirectory`)
- Test: `SharpMUSH.Tests/Database/Lightning/StagingTests.cs`, `RecoveryTests.cs`, `CascadeCoverageTests.cs`; existing `SharpMUSH.Tests/Database/StagingDatabaseTests.cs` (marked `[Explicit]`; run it explicitly under lightning).

**Interfaces:**
- Produces: `LightningStagingDatabase : LightningDatabase, IStagingDatabase` with `StagingId`, `PromoteToLiveAsync`, `AbortAsync`, `IsPromoted`, `DisposeAsync`; `LightningStore.SwapDirectory(string incomingPath, string previousPath)`; `LightningDatabase.CopyToAsync(string path, bool compact = true)`.

- [ ] **Step 1: Write the failing tests**

`StagingTests`: create a live db and migrate; create staging via `CreateStagingAsync`; create a thing named "Staged" in the staging db; `PromoteToLiveAsync`; assert the live db now finds "Staged" by `GetPlayerByNameOrAliasAsync`-style lookup or by scanning `GetAllObjectsAsync`, and that `next_dbref` on live equals the staging counter; a second test aborts and asserts live is unchanged and the staging directory is gone.

`RecoveryTests`:

```csharp
[Test]
public async Task AnAbandonedWriteIsAbsentAfterReopen()
{
	var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
	var options = new LightningStoreOptions { Path = path, MapSize = 256L << 20 };
	using (var store = new LightningStore(options))
	{
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("kept"), Keys.Str("1")));
		// Simulate a crash mid-transaction: a job that throws after writing is rolled back.
		try { await store.WriteAsync<int>(tx => { tx.Put(Tables.Meta, Keys.Str("lost"), Keys.Str("1")); throw new Exception("crash"); }); }
		catch { }
	}
	using var reopened = new LightningStore(options);
	await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("kept"), out _))).IsTrue();
	await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("lost"), out _))).IsFalse();
}
```

`CascadeCoverageTests`: reflection over `Tables.All` asserts every `ForwardEdge` has a `Pair` of kind `ReverseEdge` and vice versa; opens a store and asserts the set of database names LMDB reports (enumerate the unnamed root database's keys with a cursor, which lists sub-database names) equals `Tables.All.Select(t => t.Name)` plus any plugin-opened tables, all of which must start with `scene.` or `scene`.

- [ ] **Step 2: Red**, **Step 3: Implement** — `SwapDirectory`: `PauseWriter`, `DrainAsync`, `Gate.EnterWriteLock`, `Close`, move live dir to `previousPath` (delete an older one first), move `incomingPath` to live, `Reopen`, exit lock, `ResumeWriter`. `LightningStagingDatabase` owns its own `LightningStore` on `<path>.staging-<id>` and delegates all members to the base class with that store; `PromoteToLiveAsync` disposes the staging store, calls `live.Store.SwapDirectory(stagingPath, live.Store.Path + ".previous")`, then recomputes the live counter; `AbortAsync` disposes and deletes. `CreateStagingAsync` migrates the staging db before returning it.
- [ ] **Step 4: Green** — run the three new classes plus `StagingDatabaseTests` explicitly under lightning.
- [ ] **Step 5: Commit** — "Stage, promote, back up and recover a Lightning world".

---

### Task 19: Scene storage

**Files:**
- Create: `SharpMUSH.Plugins.Scene/Storage/LightningSceneStorage.cs`
- Modify: `SharpMUSH.Plugins.Scene/Storage/SceneSystemServiceCollectionExtensions.cs` (replace the placeholder with the real registration), `SharpMUSH.Plugins.Scene/SharpMUSH.Plugins.Scene.csproj` (reference nothing new: the accessor and key helpers live in `SharpMUSH.Library`), `SharpMUSH.Tests.ScenePlugin` registration test if it enumerates providers.
- Semantic source: `SharpMUSH.Plugins.Scene/Storage/SurrealSceneStorage.cs` (1,644 lines) and the contract `ISceneService.cs`.
- Test: `SharpMUSH.Tests.Integration/Scene/SceneServiceIntegrationTests.cs` under lightning, and `SharpMUSH.Tests.ScenePlugin/SceneSystemRegistrationTests.cs`.

- [ ] **Step 1: Red** — run the scene integration suite under lightning; expect the placeholder's `NotSupportedException`.
- [ ] **Step 2: Port** — tables `scene` (id → scene JSON), `scene.idx` (status/owner/room/scheduled keys → scene id, duplicates), `scene.part` (`Composite(sceneId, dbref)`), `scene.pose` (`Composite(sceneId, "", seq)`), `scene.log`. The plugin opens its tables through `accessor.OpenTable(name, duplicates)` once at construction. Every `ISceneService` member maps to `accessor.Read`/`WriteAsync`/`RangeAsync`. Name snapshots and UTC-millis timestamps as the contract remarks require.
- [ ] **Step 3: Green** — both suites pass under lightning.
- [ ] **Step 4: Commit** — "Store scenes in Lightning through the storage accessor".

---

### Task 20: Full suite under lightning, CI, docs

**Files:**
- Modify: `.github/workflows/_dotnet-build-test.yml:65` and `:154` (add `lightning` to both matrices), `CLAUDE.md` (project map row for the new project; env vars), `docs/design/adr-storage-engine.md` (mark action items done).

- [ ] **Step 1: Run everything** — `SHARPMUSH_DATABASE_PROVIDER=lightning dotnet run --project SharpMUSH.Tests -- --output Detailed > /tmp/claude-1000/task20-unit.log 2>&1` and the same for `SharpMUSH.Tests.Integration`. Grep for `failed` and fix every failure in the provider (not in the tests) unless a test is provider-gated by design. Record the pass/fail counts.
- [ ] **Step 2: Formatting gate** — `dotnet build SharpMUSH.sln -nologo -v:q` without `SkipFormatVerification`; fix `FORMAT001` with the two-pass format command on each affected project.
- [ ] **Step 3: CI and docs** — add the matrix entries; update `CLAUDE.md`; tick the ADR action items this branch completes.
- [ ] **Step 4: Commit** — "Run the suite against the Lightning provider in CI".

---

### Task 21: Lightning benchmarks mirroring the existing seven operations

**Files:**
- Create: `SharpMUSH.Benchmarks/LightningBaseBenchmark.cs` (copy of `SurrealBaseBenchmark.cs`: no DB container, NATS only, temp directory set through `SHARPMUSH_LIGHTNING_PATH`), `SharpMUSH.Benchmarks/LightningDatabaseBenchmarks.cs` (copy of `SurrealDatabaseBenchmarks.cs` with categories `"Database Read", "Lightning"` and `"Database Write", "Lightning"`)
- Modify: `SharpMUSH.Benchmarks/TestWebApplicationBuilderFactory.cs:40-62` (lightning arm), `SharpMUSH.Benchmarks/SharpMUSH.Benchmarks.csproj` (project reference)

- [ ] **Step 1: Build and run a short pass** — `SHARPMUSH_CI_BENCHMARK=true dotnet run --project SharpMUSH.Benchmarks -c Release -- --filter '*Lightning*' > /tmp/claude-1000/task21-bench.log 2>&1; grep -E "Mean|Error|Lightning" /tmp/claude-1000/task21-bench.log | head -20`. Expected: seven rows with means.
- [ ] **Step 2: Commit** — "Benchmark the Lightning provider on the shared read and write operations".

---

### Task 22: Extended benchmarks for all four providers

**Files:**
- Create: `SharpMUSH.Benchmarks/Extended/ExtendedDatabaseBenchmarks.cs` (abstract, `[Config(typeof(AdaptiveBenchmarkConfig))]`), `ArangoExtendedBenchmarks.cs`, `MemgraphExtendedBenchmarks.cs`, `SurrealExtendedBenchmarks.cs`, `LightningExtendedBenchmarks.cs` (each deriving from its provider's base benchmark and the abstract class's setup).

**Benchmarks** (seeded once in `[GlobalSetup]` from the migrated world):
- `InheritanceWalk`: object D with parent C with parent B with zone Z; `DESC` set on Z only; measure `GetAttributeWithInheritanceAsync(D, ["DESC"])`.
- `WildcardLattr`: object with 200 attributes named `ATTR{i:D3}` and 50 under `TREE`{i}`; measure `GetAttributesAsync(obj, "ATTR1*")` consumed to completion.
- `RegexLattr`: same object; `GetAttributesByRegexAsync(obj, "^TREE.*")`.
- `FilteredSearch`: 1 000 things owned alternately by God and by a second player, with flag `WIZARD` on every tenth; measure `GetFilteredObjectsAsync(new ObjectSearchFilter { Types = [THING], HasFlag = "WIZARD", Owner = god })` consumed to completion.
- `WipeSubtree`: create a 50-attribute subtree per iteration in `[IterationSetup]`, measure `WipeAttributeAsync`.
- `DeleteObject`: create a thing with 20 attributes per iteration, measure `DeleteObjectAsync`.
- `Reachability`: chain of 30 parents; measure `IsReachableViaParentOrZoneAsync(head, tail, 100)`.
- `ConcurrentAttributeWrites`: eight `Task.Run` loops each doing `SetAttributeAsync` on its own object 25 times; measure the `WhenAll`.

- [ ] **Step 1: Implement**, **Step 2: Short run for lightning and surrealdb** — `SHARPMUSH_CI_BENCHMARK=true dotnet run --project SharpMUSH.Benchmarks -c Release -- --filter '*Extended*' --anyCategories Lightning SurrealDB > /tmp/claude-1000/task22-bench.log 2>&1`.
- [ ] **Step 3: Commit** — "Benchmark the uncached storage shapes across every provider".

---

### Task 23: Run the comparison and write the report

**Files:**
- Create: `docs/benchmarks/2026-09-storage-providers.md`

- [ ] **Step 1: Check Docker/Podman** — `docker info >/dev/null 2>&1 && echo docker || (systemctl --user is-active podman.socket && echo podman)`. If a container runtime is available, export `DOCKER_HOST` and `TESTCONTAINERS_RYUK_DISABLED=true` as the repo's Podman memory describes, and include ArangoDB and Memgraph; otherwise run lightning and SurrealDB only and say so in the report.
- [ ] **Step 2: Full run** — `dotnet run --project SharpMUSH.Benchmarks -c Release -- --filter '*DatabaseBenchmarks*' '*Extended*' > /tmp/claude-1000/task23-bench.log 2>&1` (nightly job, no `SHARPMUSH_CI_BENCHMARK`).
- [ ] **Step 3: Report** — one table per benchmark class with Mean, Error, Allocated per provider, copied from BenchmarkDotNet's markdown exporter output under `BenchmarkDotNet.Artifacts/results/`; a paragraph per surprising row; the environment (CPU, OS, .NET, LMDB page size, map size, SurrealDB engine and endpoint, container versions).
- [ ] **Step 4: Commit** — "Record the storage provider benchmark comparison".
