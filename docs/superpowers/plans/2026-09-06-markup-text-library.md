# MarkupText Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild `SharpMUSH.MarkupString` as three AOT-safe packages (`MarkupString` core, `MarkupString.Ansi`, `MarkupString.Html`), fix the audited defects, and migrate every consumer off `MModule`.

**Architecture:** Immutable `MarkupText` = plain text + styled-span runs (gaps are plain). Formats are core values; kind packages register emitters and codecs into an immutable `MarkupRegistry` (no reflection). A temporary `MModule` compat shim inside the core project keeps the solution green until each consumer is migrated, then it is deleted.

**Tech Stack:** .NET 10, C# 14, TUnit 1.19.16, Verify.TUnit, CsCheck, BenchmarkDotNet 0.15.8, System.Text.Json (`Utf8JsonWriter`/`JsonDocument` only), `System.Buffers`.

**Spec:** `docs/superpowers/specs/2026-09-06-markup-text-library-design.md` (read it first; every task argues from it).

## Global Constraints

- C# files: tabs, indent size 2. Every build runs `dotnet format whitespace` (`FORMAT001` on failure). Fix with `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"` run twice.
- `TreatWarningsAsErrors` true in every project touched. No `[Obsolete]` on the shim (it would error).
- No reflection, no `JsonSerializer` on markup types, no `[ModuleInitializer]`. All three packages set `<IsAotCompatible>true</IsAotCompatible>`.
- `Length` is UTF-16 code units. Cuts never split a grapheme cluster. Pad/align measure display cells.
- `MarkupText.Equals` is text-only. `ToString()` is plain text.
- Test framework is TUnit: `[Test]`, `await Assert.That(x).IsEqualTo(y)`. Run one class with `dotnet run --project <TestProject> -- --treenode-filter "/*/*/<Class>/*"`. Always redirect full test output to a file and grep it.
- Commit after every task with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Never `cd` out of the worktree `/home/grave/RiderProjects/SharpMUSH/.claude/worktrees/markup-text-library`.

---

## Phase 1: the packages

### Task 1: Core project skeleton, `MarkupText`, `Run`, `MarkupSet`, factories

**Files:**
- Modify: `SharpMUSH.MarkupString/SharpMUSH.MarkupString.csproj`
- Create: `SharpMUSH.MarkupString/MarkupText.cs`, `Run.cs`, `MarkupSet.cs`, `IMarkup.cs`, `NeutralMarkup.cs`
- Create: `SharpMUSH.MarkupString.Tests/SharpMUSH.MarkupString.Tests.csproj`, `GlobalUsings.cs`, `MarkupTextTests.cs`
- Delete: `SharpMUSH.MarkupString/MarkupStringModule.cs`, `MarkupStringSerializer.cs`, `ColumnModule.cs`, `TextAlignerModule.cs`, `Markup/Markup.cs`, `Markup/AnsiCodeParser.cs`, `Markup/ANSILibrary/ANSI.cs` (their replacements arrive in Tasks 1–9; the old code is in git history and in the audit report).
- Modify: `SharpMUSH.sln` (add the test project: `dotnet sln add SharpMUSH.MarkupString.Tests`)

**Interfaces produced:**

```csharp
namespace MarkupString;
public interface IMarkup { }                       // implementations are records with value equality
public sealed class NeutralMarkup : IMarkup { public static readonly NeutralMarkup Instance = new(); }
public readonly record struct Run(int Start, int Length, MarkupSet Markups) { public int End => Start + Length; }
public sealed class MarkupSet : IEquatable<MarkupSet>, IReadOnlyList<IMarkup>
{
	public static MarkupSet Of(IMarkup markup);
	public static MarkupSet Of(ReadOnlySpan<IMarkup> markups);       // innermost first
	public static MarkupSet Of(IEnumerable<IMarkup> markups);
	public MarkupSet Append(IMarkup outer);                           // new outermost layer
	public int Count { get; } public IMarkup this[int i] { get; }
}
public sealed class MarkupText : IEquatable<MarkupText>
{
	public static readonly MarkupText Empty, Space, NewLine;
	public string Text { get; } public ImmutableArray<Run> Runs { get; } public int Length { get; }
	public static MarkupText Plain(string text);
	public static MarkupText Wrap(IMarkup markup, string text);
	public static MarkupText Wrap(MarkupSet markups, string text);
	public static MarkupText Wrap(IMarkup markup, MarkupText inner);
	public static MarkupText Concat(MarkupText a, MarkupText b);
	public static MarkupText Concat(ReadOnlySpan<MarkupText> parts);
	public static MarkupText Concat(IEnumerable<MarkupText> parts);
	public static MarkupText Join(MarkupText separator, IEnumerable<MarkupText> parts);
	public static MarkupText Join(Func<int, MarkupText> separator, IEnumerable<MarkupText> parts);
	internal MarkupText(string text, ImmutableArray<Run> runs);        // normalises: drop empty, sort, coalesce
	public string ToPlainText(); public override string ToString();
	public bool Equals(MarkupText? other); public bool TextEquals(string? s); public override int GetHashCode();
}
```

- [ ] **Step 1: csproj.** Replace the core csproj body with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MarkupString</AssemblyName>
    <RootNamespace>MarkupString</RootNamespace>
    <PackageId>MarkupString</PackageId>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <IsAotCompatible>true</IsAotCompatible>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

Remove the `Microsoft.Extensions.ObjectPool` reference (nothing will use it).

- [ ] **Step 2: Test project.** Create `SharpMUSH.MarkupString.Tests/SharpMUSH.MarkupString.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTestProject>true</IsTestProject>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>TUnit0038;TUnitAssertions0002</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="1.19.16" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SharpMUSH.MarkupString\SharpMUSH.MarkupString.csproj" />
  </ItemGroup>
</Project>
```

`GlobalUsings.cs`: `global using MarkupString;` and `global using TUnit.Core;`. Run `dotnet sln add SharpMUSH.MarkupString.Tests/SharpMUSH.MarkupString.Tests.csproj`.

- [ ] **Step 3: Failing tests** in `MarkupTextTests.cs`:

```csharp
public class MarkupTextTests
{
	private sealed record Tag(string Name) : IMarkup;

	[Test] public async Task Plain_HasNoRuns()
	{
		var t = MarkupText.Plain("abc");
		await Assert.That(t.Runs.Length).IsEqualTo(0);
		await Assert.That(t.Text).IsEqualTo("abc");
		await Assert.That(t.Length).IsEqualTo(3);
	}
	[Test] public async Task Wrap_CreatesOneRunCoveringText()
	{
		var t = MarkupText.Wrap(new Tag("b"), "abc");
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0]).IsEqualTo(new Run(0, 3, MarkupSet.Of(new Tag("b"))));
	}
	[Test] public async Task Wrap_EmptyText_IsEmpty()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "")).IsSameReferenceAs(MarkupText.Empty);
	[Test] public async Task Concat_PlainThenStyled_KeepsGapAsPlain()
	{
		var t = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
		await Assert.That(t.Text).IsEqualTo("abc");
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0].Start).IsEqualTo(2);
	}
	[Test] public async Task Concat_AdjacentEqualSets_Coalesce()
	{
		var t = MarkupText.Concat(MarkupText.Wrap(new Tag("b"), "a"), MarkupText.Wrap(new Tag("b"), "b"));
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0].Length).IsEqualTo(2);
	}
	[Test] public async Task Wrap_Inner_AddsOuterLayerToEveryRunAndGap()
	{
		var inner = MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Wrap(new Tag("i"), "b"));
		var t = MarkupText.Wrap(new Tag("o"), inner);
		await Assert.That(t.Runs.Length).IsEqualTo(2);
		await Assert.That(t.Runs[0].Markups).IsEqualTo(MarkupSet.Of(new Tag("o")));
		await Assert.That(t.Runs[1].Markups).IsEqualTo(MarkupSet.Of([new Tag("i"), new Tag("o")]));
	}
	[Test] public async Task Join_InsertsSeparatorBetweenParts()
	{
		var t = MarkupText.Join(MarkupText.Plain(", "), [MarkupText.Plain("a"), MarkupText.Plain("b")]);
		await Assert.That(t.Text).IsEqualTo("a, b");
	}
	[Test] public async Task Equality_IsTextOnly()
	{
		var plain = MarkupText.Plain("x"); var styled = MarkupText.Wrap(new Tag("b"), "x");
		await Assert.That(plain.Equals(styled)).IsTrue();
		await Assert.That(plain.GetHashCode()).IsEqualTo(styled.GetHashCode());
		await Assert.That(plain.TextEquals("x")).IsTrue();
		await Assert.That(plain.Equals((object)"x")).IsFalse();
	}
	[Test] public async Task MarkupSet_IsValueEqualAndInterned()
	{
		var a = MarkupSet.Of(new Tag("b")); var b = MarkupSet.Of(new Tag("b"));
		await Assert.That(a).IsEqualTo(b);
		await Assert.That(ReferenceEquals(a, b)).IsTrue();
	}
	[Test] public async Task ToString_IsPlainText()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").ToString()).IsEqualTo("x");
}
```

- [ ] **Step 4: Run** `dotnet run --project SharpMUSH.MarkupString.Tests -- --treenode-filter "/*/*/MarkupTextTests/*" > /tmp/t.log 2>&1; grep -E "failed|error" /tmp/t.log | head`. Expected: build errors (types missing).

- [ ] **Step 5: Implement.** `MarkupSet.cs`:

```csharp
using System.Collections;
using System.Collections.Concurrent;
namespace MarkupString;

public sealed class MarkupSet : IEquatable<MarkupSet>, IReadOnlyList<IMarkup>
{
	private const int InternCapacity = 4096;
	private static readonly ConcurrentDictionary<MarkupSet, MarkupSet> Intern = new();
	private readonly IMarkup[] _items;
	private readonly int _hash;

	private MarkupSet(IMarkup[] items)
	{
		_items = items;
		var h = new HashCode();
		foreach (var m in items) h.Add(m);
		_hash = h.ToHashCode();
	}

	public static MarkupSet Of(IMarkup markup) => Canonical(new MarkupSet([markup]));
	public static MarkupSet Of(ReadOnlySpan<IMarkup> markups) => Canonical(new MarkupSet(markups.ToArray()));
	public static MarkupSet Of(IEnumerable<IMarkup> markups) => Canonical(new MarkupSet(markups.ToArray()));
	public MarkupSet Append(IMarkup outer)
	{
		var items = new IMarkup[_items.Length + 1];
		_items.CopyTo(items, 0);
		items[^1] = outer;
		return Canonical(new MarkupSet(items));
	}

	private static MarkupSet Canonical(MarkupSet candidate)
	{
		if (candidate._items.Length == 0) throw new ArgumentException("A MarkupSet must contain at least one markup.");
		if (Intern.TryGetValue(candidate, out var existing)) return existing;
		if (Intern.Count >= InternCapacity) Intern.Clear();
		return Intern.GetOrAdd(candidate, candidate);
	}

	public int Count => _items.Length;
	public IMarkup this[int index] => _items[index];
	public IMarkup Innermost => _items[0];
	public IMarkup Outermost => _items[^1];
	public IEnumerator<IMarkup> GetEnumerator() => ((IEnumerable<IMarkup>)_items).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public bool Equals(MarkupSet? other)
	{
		if (other is null || other._items.Length != _items.Length || other._hash != _hash) return false;
		for (var i = 0; i < _items.Length; i++) if (!_items[i].Equals(other._items[i])) return false;
		return true;
	}
	public override bool Equals(object? obj) => obj is MarkupSet s && Equals(s);
	public override int GetHashCode() => _hash;
}
```

`MarkupText.cs` (construction, normalisation, equality, factories; operations arrive in Task 2):

```csharp
using System.Collections.Immutable;
namespace MarkupString;

public sealed partial class MarkupText : IEquatable<MarkupText>
{
	public static readonly MarkupText Empty = new(string.Empty, ImmutableArray<Run>.Empty);
	public static readonly MarkupText Space = new(" ", ImmutableArray<Run>.Empty);
	public static readonly MarkupText NewLine = new("\n", ImmutableArray<Run>.Empty);

	public string Text { get; }
	public ImmutableArray<Run> Runs { get; }
	public int Length => Text.Length;

	internal MarkupText(string text, ImmutableArray<Run> runs)
	{
		Text = text;
		Runs = Normalise(runs, text.Length);
	}

	public static MarkupText Plain(string text) => text.Length switch
	{
		0 => Empty,
		1 when text[0] == ' ' => Space,
		1 when text[0] == '\n' => NewLine,
		_ => new MarkupText(text, ImmutableArray<Run>.Empty),
	};

	public static MarkupText Wrap(IMarkup markup, string text) => Wrap(MarkupSet.Of(markup), text);
	public static MarkupText Wrap(MarkupSet markups, string text) =>
		text.Length == 0 ? Empty : new MarkupText(text, [new Run(0, text.Length, markups)]);

	public static MarkupText Wrap(IMarkup markup, MarkupText inner)
	{
		if (inner.Length == 0) return Empty;
		var outer = MarkupSet.Of(markup);
		var builder = ImmutableArray.CreateBuilder<Run>(inner.Runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in inner.Runs)
		{
			if (run.Start > position) builder.Add(new Run(position, run.Start - position, outer));
			builder.Add(new Run(run.Start, run.Length, run.Markups.Append(markup)));
			position = run.End;
		}
		if (position < inner.Length) builder.Add(new Run(position, inner.Length - position, outer));
		return new MarkupText(inner.Text, builder.ToImmutable());
	}

	public static MarkupText Concat(MarkupText a, MarkupText b)
	{
		if (a.Length == 0) return b;
		if (b.Length == 0) return a;
		var runs = ImmutableArray.CreateBuilder<Run>(a.Runs.Length + b.Runs.Length);
		runs.AddRange(a.Runs);
		foreach (var run in b.Runs) runs.Add(run with { Start = run.Start + a.Length });
		return new MarkupText(a.Text + b.Text, runs.ToImmutable());
	}

	public static MarkupText Concat(ReadOnlySpan<MarkupText> parts)
	{
		var totalLength = 0; var totalRuns = 0; var nonEmpty = 0; MarkupText? last = null;
		foreach (var p in parts) { if (p.Length == 0) continue; totalLength += p.Length; totalRuns += p.Runs.Length; nonEmpty++; last = p; }
		if (nonEmpty == 0) return Empty;
		if (nonEmpty == 1) return last!;
		var runs = ImmutableArray.CreateBuilder<Run>(totalRuns);
		var text = string.Create(totalLength, (parts: parts.ToArray(), runs), static (span, state) =>
		{
			var offset = 0;
			foreach (var p in state.parts)
			{
				if (p.Length == 0) continue;
				p.Text.AsSpan().CopyTo(span[offset..]);
				foreach (var run in p.Runs) state.runs.Add(run with { Start = run.Start + offset });
				offset += p.Length;
			}
		});
		return new MarkupText(text, runs.ToImmutable());
	}

	public static MarkupText Concat(IEnumerable<MarkupText> parts) =>
		parts is MarkupText[] array ? Concat(array.AsSpan()) : Concat(parts.ToArray().AsSpan());

	public static MarkupText Join(MarkupText separator, IEnumerable<MarkupText> parts) =>
		Join(_ => separator, parts);

	public static MarkupText Join(Func<int, MarkupText> separator, IEnumerable<MarkupText> parts)
	{
		var list = new List<MarkupText>();
		var i = 0;
		foreach (var part in parts)
		{
			if (i > 0) list.Add(separator(i));
			list.Add(part);
			i++;
		}
		return Concat(list.ToArray().AsSpan());
	}

	public string ToPlainText() => Text;
	public override string ToString() => Text;
	public bool Equals(MarkupText? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);
	public bool TextEquals(string? text) => string.Equals(Text, text, StringComparison.Ordinal);
	public override bool Equals(object? obj) => obj is MarkupText other && Equals(other);
	public override int GetHashCode() => string.GetHashCode(Text, StringComparison.Ordinal);
	public static bool operator ==(MarkupText? a, MarkupText? b) => a is null ? b is null : a.Equals(b);
	public static bool operator !=(MarkupText? a, MarkupText? b) => !(a == b);

	/// Drops empty runs, sorts by start, merges adjacent runs with equal sets, clips to the text.
	private static ImmutableArray<Run> Normalise(ImmutableArray<Run> runs, int length)
	{
		if (runs.IsDefaultOrEmpty) return ImmutableArray<Run>.Empty;
		var sorted = false;
		for (var i = 1; i < runs.Length; i++) if (runs[i].Start < runs[i - 1].Start) { sorted = true; break; }
		var source = sorted ? runs.Sort((a, b) => a.Start.CompareTo(b.Start)) : runs;
		var builder = ImmutableArray.CreateBuilder<Run>(source.Length);
		foreach (var raw in source)
		{
			var start = Math.Max(0, raw.Start);
			var end = Math.Min(length, raw.End);
			if (end <= start) continue;
			var run = new Run(start, end - start, raw.Markups);
			if (builder.Count > 0)
			{
				var prev = builder[^1];
				if (run.Start < prev.End) throw new ArgumentException("Runs must not overlap.");
				if (prev.End == run.Start && prev.Markups.Equals(run.Markups))
				{
					builder[^1] = prev with { Length = prev.Length + run.Length };
					continue;
				}
			}
			builder.Add(run);
		}
		return builder.Count == source.Length && !sorted ? runs : builder.ToImmutable();
	}
}
```

(`Run.cs`, `IMarkup.cs`, `NeutralMarkup.cs` are the one-liners from the interface block above.)

- [ ] **Step 6: Run tests** (same command). Expected: all `MarkupTextTests` pass.
- [ ] **Step 7: Format and commit.** `dotnet format whitespace --folder SharpMUSH.MarkupString --exclude "**/bin/**" --exclude "**/obj/**"` twice, same for the test project; `git add -A SharpMUSH.MarkupString SharpMUSH.MarkupString.Tests SharpMUSH.sln && git commit -m "MarkupText core: text + styled-span runs, interned MarkupSet"`.

### Task 2: Text operations on `MarkupText` (grapheme-safe)

**Files:**
- Create: `SharpMUSH.MarkupString/MarkupText.Operations.cs`, `Graphemes.cs`, `DisplayWidth.cs`, `DisplayWidth.Table.cs`, `Enums.cs`
- Test: `SharpMUSH.MarkupString.Tests/MarkupTextOperationsTests.cs`, `GraphemeTests.cs`, `DisplayWidthTests.cs`

**Interfaces produced:**

```csharp
public enum TrimType { TrimStart, TrimEnd, TrimBoth }
public enum PadType { Left, Right, Center, Full }
public enum TruncationType { Truncate, Overflow }
public readonly record struct Edit(int Start, int Length, MarkupText Replacement);
public static class Graphemes
{
	public static int SnapStart(ReadOnlySpan<char> text, int index);   // <= index, cluster boundary
	public static int SnapEnd(ReadOnlySpan<char> text, int index);     // >= index, cluster boundary
	public static bool IsBoundary(ReadOnlySpan<char> text, int index);
}
public static class DisplayWidth
{
	public static int Of(ReadOnlySpan<char> text);
	public static int OfRune(Rune rune);            // 0, 1 or 2
	public static int IndexAtWidth(ReadOnlySpan<char> text, int cells);  // largest boundary whose width <= cells
}
partial class MarkupText
{
	public int DisplayWidth { get; }
	public MarkupText Substring(int start); public MarkupText Substring(int start, int length);
	public MarkupText[] Split(string delimiter); public MarkupText[] Split(MarkupText delimiter);
	public MarkupText Trim(TrimType type, string chars = " "); public MarkupText Trim(TrimType type, MarkupText chars);
	public MarkupText Pad(MarkupText fill, int width, PadType type, TruncationType truncation);
	public MarkupText Center(MarkupText fillLeft, MarkupText fillRight, int width, TruncationType truncation);
	public MarkupText Repeat(int count);
	public MarkupText Remove(int index, int length);
	public MarkupText Replace(int index, int length, MarkupText replacement);
	public MarkupText Insert(int index, MarkupText insert);
	public MarkupText Splice(ReadOnlySpan<Edit> edits);
	public MarkupText ReplaceAll(string search, MarkupText replacement);
	public int IndexOf(string search); public int LastIndexOf(string search); public IEnumerable<int> IndexesOf(string search);
	public MarkupText Apply(Func<string, string> transform);
	public MarkupText Map(Func<MarkupText, MarkupText> transform);
	public MarkupText AttachTail(MarkupText tail);
}
```

- [ ] **Step 1: Failing tests.** `MarkupTextOperationsTests.cs` (port every operation test from `SharpMUSH.Tests/Markup/AttributedMarkupStringTests.cs` that exercises substring/split/trim/pad/repeat/remove/replace/insert to the new API; keep names) plus these new ones:

```csharp
[Test] public async Task Substring_NeverSplitsSurrogatePair()
{
	var t = MarkupText.Plain("a😀b");
	await Assert.That(t.Substring(0, 2).Text).IsEqualTo("a");     // snaps end down to boundary
	await Assert.That(t.Substring(2, 2).Text).IsEqualTo("😀b");   // snaps start down to 1
}
[Test] public async Task Substring_KeepsCombiningMarkWithBase()
{
	var t = MarkupText.Plain("éx");
	await Assert.That(t.Substring(0, 1).Text).IsEqualTo("");      // cannot end inside cluster: snaps to 0
	await Assert.That(t.Substring(0, 2).Text).IsEqualTo("é");
}
[Test] public async Task Pad_UsesDisplayWidth()
{
	var t = MarkupText.Plain("日本").Pad(MarkupText.Plain("."), 6, PadType.Right, TruncationType.Truncate);
	await Assert.That(t.Text).IsEqualTo("日本..");
}
[Test] public async Task Pad_TruncatesByDisplayWidthWithoutSplittingWideChar()
{
	var t = MarkupText.Plain("日本語").Pad(MarkupText.Plain(" "), 5, PadType.Right, TruncationType.Truncate);
	await Assert.That(t.Text).IsEqualTo("日本 ");
}
[Test] public async Task Splice_AppliesEditsInOnePass()
{
	var t = MarkupText.Plain("a  b   c");
	var r = t.ReplaceAll("  ", MarkupText.Plain(" "));
	await Assert.That(r.Text).IsEqualTo("a b  c");   // non-overlapping left-to-right matches
}
[Test] public async Task ReplaceAll_PreservesMarkupOutsideMatches()
{
	var t = MarkupText.Concat(MarkupText.Wrap(new Tag("b"), "ab"), MarkupText.Plain("  "), MarkupText.Wrap(new Tag("b"), "cd"));
	var r = t.ReplaceAll("  ", MarkupText.Plain(" "));
	await Assert.That(r.Text).IsEqualTo("ab cd");
	await Assert.That(r.Runs.Length).IsEqualTo(2);
}
[Test] public async Task Insert_InheritsEnclosingRunMarkup()
{
	var t = MarkupText.Wrap(new Tag("b"), "abcd").Insert(2, MarkupText.Plain("X"));
	await Assert.That(t.Runs.Length).IsEqualTo(1);
	await Assert.That(t.Text).IsEqualTo("abXcd");
}
```

`DisplayWidthTests.cs`: `Of("abc")==3`, `Of("日本")==4`, `Of("é")==1`, `Of("👨‍👩‍👧")==2`, `Of("")==0`, `Of("ｱ")==1` (halfwidth katakana), `Of("Ａ")==2` (fullwidth). `GraphemeTests.cs`: `SnapStart("a😀b",2)==1`, `SnapEnd("a😀b",2)==3`, `IsBoundary("é",1)==false`, ASCII indices unchanged.

- [ ] **Step 2: Run, expect compile failures.**
- [ ] **Step 3: Implement `Graphemes`.** Fast path: if `index<=0` or `index>=text.Length` return clamped index. If `char.IsSurrogate(text[index])` or `Rune.DecodeFromUtf16(text[index..], out var r, out _) == OperationStatus.Done` and `r` is category `NonSpacingMark`, `SpacingCombiningMark`, `EnclosingMark`, or `Format` (ZWJ U+200D, VS U+FE00–FE0F), or `text[index-1]` is U+200D, then walk clusters from the previous known boundary using `System.Globalization.StringInfo.GetNextTextElementLength(text[start..])`, starting from `max(0, index-32)` snapped back to a non-surrogate, non-mark char (loop back while the char is a surrogate low / mark / ZWJ). `SnapStart` returns the last boundary `<= index`, `SnapEnd` the first boundary `>= index`.
- [ ] **Step 4: Implement `DisplayWidth`.** `OfRune`: control (`< 0x20`, `0x7F–0x9F`) → 0; `Rune.GetUnicodeCategory` in `NonSpacingMark`, `EnclosingMark`, `Format` → 0; U+1160–U+11FF (Hangul jungseong/jongseong) → 0; else 2 if the code point falls in a range of `DisplayWidth.Table.WideRanges`; else 1. `Of(text)`: iterate `text.EnumerateRunes()`; when a ZWJ joins emoji the whole sequence counts as the first emoji's width (skip runes after a U+200D until a non-emoji). `IndexAtWidth`: accumulate cluster widths (`StringInfo` when non-ASCII) until adding the next cluster would exceed `cells`; return that boundary.
  `DisplayWidth.Table.cs`: `internal static readonly (int Start, int End)[] WideRanges` = the East Asian Width `W` and `F` ranges from Unicode 16.0 `EastAsianWidth.txt`, merged. Generate it with this one-off script (commit the generated C# file, not the script's output as data):

```bash
curl -s https://www.unicode.org/Public/16.0.0/ucd/EastAsianWidth.txt \
 | grep -E '^[0-9A-F]+(\.\.[0-9A-F]+)?\s*;\s*[WF]\b' \
 | sed -E 's/^([0-9A-F]+)(\.\.([0-9A-F]+))?\s*;.*/\1 \3/' \
 | awk '{ s=strtonum("0x"$1); e=($2==""?s:strtonum("0x"$2)); if (NR>1 && s<=pe+1) { pe=(e>pe?e:pe) } else { if (NR>1) printf("\t\t(0x%X, 0x%X),\n", ps, pe); ps=s; pe=e } } END { printf("\t\t(0x%X, 0x%X),\n", ps, pe) }'
```

  Wrap the output in `internal static partial class DisplayWidthTable { internal static readonly (int Start, int End)[] WideRanges = [ … ]; }` and binary-search it in `OfRune`. Add a header comment naming the Unicode version and the command.
- [ ] **Step 5: Implement the operations** in `MarkupText.Operations.cs`. Rules: `Substring(start,length)`: clamp, then `start = Graphemes.SnapStart(Text, start)`, `end = Graphemes.SnapStart(Text, end)` (an end inside a cluster moves down, never up, so a substring never grows), clip runs with a binary search on `Runs` by `Start` (port the old `FindFirstOverlappingRunIndex`). `Split(delimiter)`: find ordinal matches, then `Substring` between them (empty delimiter → single element). `Trim`: count from each side with `chars.Contains(text[i])`, then `Substring`. `Pad`: `var width0 = DisplayWidth;` if `width0 >= width` → truncate by `DisplayWidth.IndexAtWidth(Text, width)` when `Truncate`, else return `this`; padding count in cells = `width - width0`; fill built by repeating `fill` and cutting at `IndexAtWidth`; `Full` distributes cells across space-separated words as today. `Center` likewise. `Splice(edits)`: validate sorted/non-overlapping; one pass building text with `string.Create`-free `StringBuilder` and runs: for each gap between edits copy runs clipped (reuse the clipping helper into a `List<Run>`), for each edit append the replacement's runs offset; the character *before* an edit's run set is not inherited. `ReplaceAll(search, repl)`: gather ordinal non-overlapping matches into `Edit`s and `Splice`. `Remove`/`Replace`/`Insert` are `Splice` with one edit (`Insert` wraps the replacement in the enclosing run's set when index is strictly inside a run). `Repeat`: `Concat` of `count` copies via span. `Apply`: same-length → keep runs, else plain. `Map`: per-run + per-gap transform, `Concat`. `AttachTail(tail)`: if last run ends at `Length`, wrap `tail` in `Runs[^1].Markups.Outermost` then concat, else plain concat. `IndexOf`/`LastIndexOf`/`IndexesOf` ordinal on `Text`.
- [ ] **Step 6: Run tests, expect pass. Format. Commit** `"MarkupText operations: grapheme-safe cuts, display-width padding, batch splice"`.

### Task 3: Formats, registry, emitters, renderer

**Files:**
- Create: `SharpMUSH.MarkupString/MarkupFormat.cs`, `TextEncoding.cs`, `IMarkupEmitter.cs`, `IMarkupSetEmitter.cs`, `IFormatFramer.cs`, `IMarkupCodec.cs`, `EmitContext.cs`, `MarkupRegistry.cs`, `MarkupTextRenderer.cs`, `MarkupText.Render.cs`, `UnknownMarkup.cs`
- Test: `SharpMUSH.MarkupString.Tests/RendererTests.cs`

**Interfaces produced:**

```csharp
public enum TextEncoding { None, StripControls, Html }
public sealed class MarkupFormat : IEquatable<MarkupFormat>
{
	public string Name { get; } public TextEncoding Encoding { get; }
	public static readonly MarkupFormat Plain  = new("plain", TextEncoding.StripControls);
	public static readonly MarkupFormat Ansi   = new("ansi", TextEncoding.None);
	public static readonly MarkupFormat Html   = new("html", TextEncoding.Html);
	public static readonly MarkupFormat Pueblo = new("pueblo", TextEncoding.Html);
	public static readonly MarkupFormat Mxp    = new("mxp", TextEncoding.Html);
	public static readonly MarkupFormat BBCode = new("bbcode", TextEncoding.StripControls);
	public static MarkupFormat Custom(string name, TextEncoding encoding);
	public static MarkupFormat? TryParse(string name);      // the six built-ins by name, case-insensitive
	// equality by Name (ordinal, case-insensitive)
}
public readonly ref struct EmitContext
{
	public MarkupFormat Format { get; init; }
	public MarkupSet? Previous { get; init; }   // set of the previous run, null for a gap or start
	public MarkupSet? Next { get; init; }
	public bool IsFirstRun { get; init; } public bool IsLastRun { get; init; }
}
public interface IMarkupEmitter { Type MarkupType { get; } MarkupFormat Format { get; } void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output); }
public interface IMarkupSetEmitter { MarkupFormat Format { get; } bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output); }
public interface IFormatFramer { MarkupFormat Format { get; } void WritePreamble(IBufferWriter<char> output); void WriteEpilogue(bool anyRunEmitted, IBufferWriter<char> output); }
public interface IMarkupCodec { string Kind { get; } Type MarkupType { get; } void Write(Utf8JsonWriter writer, IMarkup markup); IMarkup Read(JsonElement element); }
public sealed record UnknownMarkup(string Kind, string RawJson) : IMarkup;
public sealed class MarkupRegistry
{
	public static readonly MarkupRegistry Empty;
	public static MarkupRegistry Default { get; set; }     // throws InvalidOperationException from the getter if never set
	public static bool IsConfigured { get; }
	public MarkupRegistry With(IMarkupEmitter e); With(IMarkupSetEmitter e); With(IFormatFramer f); With(IMarkupCodec c);
	public IMarkupEmitter? FindEmitter(Type markupType, MarkupFormat format);
	public IMarkupSetEmitter? FindSetEmitter(MarkupFormat format);
	public IFormatFramer? FindFramer(MarkupFormat format);
	public IMarkupCodec? FindCodec(string kind); public IMarkupCodec? FindCodec(Type markupType);
}
partial class MarkupText
{
	public string Render(MarkupFormat format, MarkupRegistry? registry = null);
	public void Render(MarkupFormat format, IBufferWriter<char> output, MarkupRegistry? registry = null);
	public bool Equals(MarkupText other, MarkupFormat format, MarkupRegistry? registry = null);
}
public static class MarkupTextRenderer
{
	public static void EncodeText(ReadOnlySpan<char> text, TextEncoding encoding, IBufferWriter<char> output);
	public static void Render(MarkupText text, MarkupFormat format, MarkupRegistry registry, IBufferWriter<char> output);
}
```

- [ ] **Step 1: Failing tests** `RendererTests.cs` using two local test markups and emitters:

```csharp
private sealed record Tag(string Name) : IMarkup;
private sealed class TagEmitter(MarkupFormat format) : IMarkupEmitter
{
	public Type MarkupType => typeof(Tag); public MarkupFormat Format => format;
	public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext c, IBufferWriter<char> o)
	{ var n = ((Tag)markup).Name; Write(o, "<" + n + ">"); Write(o, body); Write(o, "</" + n + ">"); }
}
[Test] public async Task Render_GapThenRun_EmitsAllText()
{
	var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
	var t = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
	await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("ab<b>c</b>");
}
[Test] public async Task Render_NestedSet_WrapsInnermostFirst()
{
	var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
	var t = MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new Tag("i"), "x"));
	await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("<o><i>x</i></o>");
}
[Test] public async Task Render_UnregisteredMarkup_FallsBackToBody()
	=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Html, MarkupRegistry.Empty)).IsEqualTo("x");
[Test] public async Task Render_HtmlEncodesText()
	=> await Assert.That(MarkupText.Plain("<&>").Render(MarkupFormat.Html, MarkupRegistry.Empty)).IsEqualTo("&lt;&amp;&gt;");
[Test] public async Task Render_PlainStripsControls()
	=> await Assert.That(MarkupText.Plain("a[31mb\tc").Render(MarkupFormat.Plain, MarkupRegistry.Empty)).IsEqualTo("a[31mb\tc");
[Test] public async Task Render_AnsiFormatKeepsControls()
	=> await Assert.That(MarkupText.Plain("ab").Render(MarkupFormat.Ansi, MarkupRegistry.Empty)).IsEqualTo("ab");
[Test] public async Task Render_CustomFormat_IsNotAnsi()
	=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Custom("bogus", TextEncoding.None), MarkupRegistry.Empty)).IsEqualTo("x");
[Test] public async Task Render_WithoutDefaultRegistry_Throws()
{
	// run in a fresh process-wide state: IsConfigured is false in this test assembly unless set
	await Assert.That(() => MarkupText.Plain("x").Render(MarkupFormat.Html)).Throws<InvalidOperationException>();
}
[Test] public async Task Equals_InFormat_ComparesRenderedOutput()
{
	var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
	var a = MarkupText.Wrap(new Tag("b"), "x"); var b = MarkupText.Plain("x");
	await Assert.That(a.Equals(b, MarkupFormat.Plain, reg)).IsTrue();
	await Assert.That(a.Equals(b, MarkupFormat.Html, reg)).IsFalse();
}
```

(`Write(IBufferWriter<char>, ReadOnlySpan<char>)` helper: `var s = o.GetSpan(text.Length); text.CopyTo(s); o.Advance(text.Length);` — put it in core as `public static class BufferWriterExtensions { public static void Write(this IBufferWriter<char> w, ReadOnlySpan<char> text) }` and use it from the emitters.)

- [ ] **Step 2: Run, expect failures.**
- [ ] **Step 3: Implement.** `MarkupRegistry` holds `FrozenDictionary<(Type, MarkupFormat), IMarkupEmitter>`, `FrozenDictionary<MarkupFormat, IMarkupSetEmitter>`, `FrozenDictionary<MarkupFormat, IFormatFramer>`, `FrozenDictionary<string, IMarkupCodec>` and `FrozenDictionary<Type, IMarkupCodec>`; `With` copies into a new instance. `Default` backed by a `static MarkupRegistry? _default` with getter `?? throw new InvalidOperationException("MarkupRegistry.Default has not been configured. Call MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml() at startup.")`.
  `MarkupTextRenderer.Render`: `EncodeText` implementations: `None` copies; `StripControls` uses `SearchValues<char>` for ` –` minus `\t\n\r` plus ``; `Html` strips the same then encodes `< > & " '` (`&lt; &gt; &amp; &quot; &#39;`). Walk: `position=0; foreach run: if run.Start>position encode gap; renderRun(run); position=run.End; then trailing gap`. `renderRun`: encode body into a pooled `ArrayBufferWriter<char>`-like scratch (use `ArrayPool<char>.Shared` with a small `PooledCharWriter : IBufferWriter<char>` internal type that grows); if `FindSetEmitter(format)?.TryEmit(set, body, ctx, output)` is true → done; else for `i` from 0 to `set.Count-1`: emitter = `FindEmitter(set[i].GetType(), format)`; if null → body stays; else emit into a second scratch and swap; finally copy the last scratch to `output`. Framer preamble before the first run, epilogue after the walk with `anyRunEmitted`.
  `MarkupText.Render(format, registry)` → `ArrayBufferWriter<char>` sized `Length + 16`, return `new string(writer.WrittenSpan)`. `Equals(other, format, registry)` → compare rendered strings (fast path: `Runs.IsEmpty && other.Runs.IsEmpty` → text equality).
- [ ] **Step 4: Run tests, pass. Format. Commit** `"Markup formats, registry, and renderer"`.

### Task 4: Serializer with codecs

**Files:**
- Create: `SharpMUSH.MarkupString/MarkupTextSerializer.cs`
- Test: `SharpMUSH.MarkupString.Tests/SerializerTests.cs`

**Interfaces produced:** `public static class MarkupTextSerializer { public static string Serialize(MarkupText, MarkupRegistry? = null); public static void Serialize(MarkupText, IBufferWriter<byte>, MarkupRegistry? = null); public static MarkupText Deserialize(string json, MarkupRegistry? = null); public static MarkupText Deserialize(ReadOnlySpan<byte> utf8, MarkupRegistry? = null); }`. Wire shape per spec: `{"t":…,"p":[null,[{"k":"kind",…}],…],"r":[len,idx,…]}`. `Neutral` codec lives in core (`"n"` kind `"neutral"`).

- [ ] **Step 1: Failing tests.** Round-trip through a local `Tag` codec; plain text serialises to `{"t":"hi"}`; empty to `{}`; `Deserialize("")` → `Empty`; legacy entry `{"h":"send","a":"href=\"x\""}` with no `k` and no html codec registered → `UnknownMarkup("html", raw)` whose re-serialisation writes the raw object back verbatim; legacy entry with no `k`, no `h`, no `n` → kind `"ansi"` (Unknown if the ansi codec is not registered); gaps use palette slot 0 (`{"t":"abc","p":[null,[{"k":"tag","n":"b"}]],"r":[2,0,1,1]}` for `Concat(Plain("ab"), Wrap(Tag("b"),"c"))`); truncated `r` pair stops reading; cover entries pointing past the text are clipped.
- [ ] **Step 2: Run, expect failure.**
- [ ] **Step 3: Implement**, porting the palette/cover logic from the old `MarkupStringSerializer` (git show `HEAD~3:SharpMUSH.MarkupString/MarkupStringSerializer.cs`) onto `MarkupSet` (palette dictionary keyed by `MarkupSet` directly), writing `"k"` first in every markup object, reading via `FindCodec(kind)` with the legacy inference. `UnknownMarkup.RawJson` is written with `writer.WriteRawValue(raw)`.
- [ ] **Step 4: Pass, format, commit** `"MarkupTextSerializer: kind-tagged palette format with pluggable codecs"`.

### Task 5: `MarkupString.Ansi` — colour model, style, code parser

**Files:**
- Create: `SharpMUSH.MarkupString.Ansi/SharpMUSH.MarkupString.Ansi.csproj` (same properties as core; `AssemblyName`/`PackageId`/`RootNamespace` `MarkupString.Ansi`; `ProjectReference` to core), `AnsiColor.cs`, `AnsiPalette.cs`, `AnsiStyle.cs`, `AnsiMarkup.cs`, `AnsiCodeParser.cs`, `LinkKind.cs`
- Test: `SharpMUSH.MarkupString.Tests/Ansi/AnsiColorTests.cs`, `AnsiCodeParserTests.cs` (test project gains a `ProjectReference` to the Ansi project); `dotnet sln add` the project.

**Interfaces produced:**

```csharp
namespace MarkupString.Ansi;
public enum LinkKind { Url = 0, Command = 1 }
public abstract record AnsiColor
{
	public sealed record Default : AnsiColor { public static readonly Default Instance = new(); }
	public sealed record Standard(byte Index, bool Bright) : AnsiColor;   // Index 0–7
	public sealed record Xterm(byte Index) : AnsiColor;
	public sealed record Rgb(byte R, byte G, byte B) : AnsiColor;
	public Rgb? ToRgb();                                    // null for Default
	public static Rgb NearestXterm(Rgb c) / public static Xterm NearestXtermIndex(Rgb c); public static Standard NearestStandard(Rgb c);
	public static bool TryParseHex(ReadOnlySpan<char> hex, out Rgb rgb);   // "#rgb", "#rrggbb"
	public string ToHex();                                  // "#rrggbb" via ToRgb, "" for Default
}
public static class AnsiPalette { public static AnsiColor.Rgb Standard(byte index, bool bright); public static AnsiColor.Rgb Xterm(byte index); }
public readonly record struct AnsiStyle
{
	public AnsiColor? Foreground { get; init; } public AnsiColor? Background { get; init; }
	public bool Bold, Faint, Italic, Underlined, Overlined, Blink, Inverted, StrikeThrough, Clear { get; init; }
	public string? LinkUrl { get; init; } public string? LinkText { get; init; } public LinkKind LinkKind { get; init; }
	public static readonly AnsiStyle None;
	public bool IsNone { get; }
	public AnsiStyle Combine(AnsiStyle inner);      // inner's non-null colours and true flags win; inner.Clear resets outer first
	public bool HasAnyAttribute { get; }
}
public sealed record AnsiMarkup(AnsiStyle Style) : IMarkup
{
	public static AnsiMarkup Create(AnsiColor? foreground = null, AnsiColor? background = null, string? linkText = null, string? linkUrl = null, LinkKind linkKind = LinkKind.Url, bool blink = false, bool bold = false, bool clear = false, bool faint = false, bool inverted = false, bool italic = false, bool overlined = false, bool underlined = false, bool strikeThrough = false);
}
public static class AnsiCodeParser { public static AnsiMarkup Parse(string codes); }
```

- [ ] **Step 1: Failing tests.** Port every `AnsiCodeParser_*` test from `SharpMUSH.Tests/Markup/MarkupStringHandlerTests.cs` to the new model (`Parse("r").Style.Foreground` is `Standard(1,false)`; `"hr"` → `Standard(1,true)`; `"200"` and `"+xterm200"` → `Xterm(200)`; `"/200"` → background; `"#ff0000"` → `Rgb(255,0,0)`; `"<255 0 0>"` → `Rgb`; `"d"` → `Default`; `"n"` → `Clear=true` and colours null; `"#zz"` → foreground null). `AnsiColorTests`: `Standard(1,false).ToRgb()==Rgb(170,0,0)`, `Standard(1,true).ToRgb()==Rgb(255,85,85)`, `Xterm(200).ToRgb()==Rgb(255,0,215)`, `Xterm(232).ToRgb()==Rgb(8,8,8)`, `Xterm(9).ToRgb()==Standard(1,true).ToRgb()`, `Default.ToRgb()==null`, `NearestStandard(Rgb(250,10,10))==Standard(1,true)`, `TryParseHex("#abc")==Rgb(0xaa,0xbb,0xcc)`.
- [ ] **Step 2: Run, expect failure.**
- [ ] **Step 3: Implement.** `AnsiPalette.Standard` = the VGA table from the old `ANSI.AnsiPalette` (indices 0–7 normal = 30–37, bright = 90–97). `AnsiPalette.Xterm`: 0–15 → standard (0–7 normal, 8–15 bright); 16–231 → 6×6×6 cube with levels `[0,95,135,175,215,255]`; 232–255 → grey `8 + 10*(i-232)`. `Nearest*` use redmean distance: `rmean=(r1+r2)/2; d = (2+rmean/256)*dr² + 4*dg² + (2+(255-rmean)/256)*db²`. `AnsiCodeParser.Parse` = the old `ParseCodes` (git history) with: letters → `Standard(index, curHighlight)` where `x,r,g,y,b,m,c,w` = 0–7, `d` → `Default`, uppercase → background, `D` → background `Default`; numbers/`+xtermN` → `Xterm`; `#hex` → `TryParseHex` (no exceptions); `<r g b>` → `Rgb`.
- [ ] **Step 4: Pass, format, commit** `"MarkupString.Ansi: semantic colour model, AnsiStyle, code parser"`.

### Task 6: `SgrWriter`, ANSI escape parser, ANSI emitters, codec, registration

**Files:**
- Create: `SharpMUSH.MarkupString.Ansi/SgrWriter.cs`, `AnsiEscapeParser.cs`, `Emitters/AnsiSetEmitter.cs` (format Ansi), `Emitters/AnsiHtmlEmitter.cs`, `Emitters/AnsiPuebloEmitter.cs`, `Emitters/AnsiMxpEmitter.cs`, `Emitters/AnsiBBCodeEmitter.cs`, `AnsiFormatFramer.cs`, `AnsiMarkupCodec.cs`, `AnsiRegistration.cs`, `UrlSafety.cs`
- Test: `SharpMUSH.MarkupString.Tests/Ansi/SgrWriterTests.cs`, `AnsiEscapeParserTests.cs` (port `SharpMUSH.Tests/Services/AnsiEscapeParserTests.cs`), `AnsiRenderTests.cs` (port `RenderFormatTests`, `RenderStrategyTests`, `LinkKindRenderTests`, `PuebloMxpRenderTests`, `AnsiHighlightFidelityTests`, `HtmlMarkupTests` ANSI-only cases, `MarkupStringOptimizationTests` expectations rewritten for the diffed stream)

**Interfaces produced:**

```csharp
public static class SgrWriter
{
	/// Writes the minimal SGR moving a terminal from `from` to `to`. Returns true if anything was written.
	public static bool Transition(in AnsiStyle from, in AnsiStyle to, IBufferWriter<char> output);
	public static void Reset(IBufferWriter<char> output);          // ESC[0m
	public static void WriteColorCodes(AnsiColor color, bool background, ref SgrBuilder codes);   // 30–37/90–97, 39, 38;5;n, 38;2;r;g;b
}
public static class AnsiEscapeParser { public static MarkupText Parse(string text); }
public static class AnsiRegistration { public static MarkupRegistry WithAnsi(this MarkupRegistry registry); }
public static class UrlSafety { public static bool IsSafeNavigableUrl(string url); }
```

- [ ] **Step 1: Failing tests.** `SgrWriterTests`: `Transition(None, {Bold,Fg=Standard(1,false)})` writes `"[1;31m"`; `Transition({Bold}, {Bold, Underlined})` writes `"[4m"` only; `Transition({Bold,Underlined},{Bold})` writes `"[0m[1m"`; `Transition({Fg=Xterm(200)}, {Fg=Xterm(200)})` writes nothing; `Transition(None, {Fg=Rgb(1,2,3), Bg=Default})` writes `"[38;2;1;2;3;49m"`; `Clear` in `to` writes `"[0m"` then the rest. `AnsiRenderTests` key expectations (format via a registry `MarkupRegistry.Empty.WithAnsi()`): `Wrap(red,"a")` → `"[31ma[0m"`; nested underline(red(bold("hello"))) → `"[1;4;31mhello[0m"`; `red "a" + plain "-" + red "b"` → `"[31ma[0m-[31mb[0m"`; `red "a" + bold "b"` (adjacent, different) → `"[31ma[0m[1mb[0m"`; `red "a" + red-bold "b"` → `"[31ma[1mb[0m"`; `Xterm(200)` → Html `<span style="color: #ff00d7">x</span>`; `Default` fg → Html `x` with no style; `Parse("hr")` → Html `#ff5555`; `Parse("hr")` → Mxp `"[1;31mx[0m"`; link Url kind → Ansi `"]8;;http://xa]8;;"`, Html `<a href="http://x" target="_blank" rel="noopener noreferrer">a</a>`, Mxp `<A HREF="http://x">a</A>`, Pueblo `<A HREF="http://x">a</A>`; link Command kind → Ansi plain, Html `<a class="ms-cmd-link" role="button" tabindex="0" xch_cmd="look">a</a>`, Mxp `<SEND HREF="look" HINT="h">a</SEND>`, Pueblo `<A XCH_CMD="look" XCH_HINT="h">a</A>`; `javascript:` links render as text everywhere; BBCode `[color=#aa0000][b]x[/b][/color]`; inverted swaps fg/bg in Html; 200 alternating red/bold single chars render to at most `200*8+4` chars. `AnsiEscapeParserTests`: port the existing file's cases and add `"[38;5;200mx"` → `Xterm(200)`, `"[1;34mx"` → `Standard(4,true)`... (bold + colour is Bold=true and `Standard(4,false)`; bright is only from 90–97), `"]8;;http://xa]8;;"` → link.
- [ ] **Step 2: Run, expect failure.**
- [ ] **Step 3: Implement.** `SgrWriter.Transition`: if `to.Clear` or any attribute/colour present in `from` is absent in `to` (attribute true→false, colour non-null→null) → `Reset` then write `to` fully (if not `IsNone`); else write only added attributes (1,2,3,4,53,5,7,9 in that order) and changed colours in one `ESC[…m`. `SgrBuilder` is a small `ref struct` accumulating `;`-separated numbers into a stackalloc span. `AnsiSetEmitter` (`IMarkupSetEmitter`, format Ansi): folds `AnsiMarkup` layers of the set outermost→innermost via `Combine` into `effective`; computes `previousEffective` from `context.Previous` the same way (None for gaps); `Transition(previousEffective, effective)`; if the effective style has a Url link that `UrlSafety` accepts, write OSC 8 open; body; OSC 8 close; if `context.Next` is null (a gap or end follows) and `effective` is not None → `Reset`. Non-`AnsiMarkup` layers in the set are handed to their `IMarkupEmitter` for the Ansi format wrapping the SGR output (render inner layers into scratch, then the outer non-ANSI emitter). `AnsiFormatFramer` (Ansi): no preamble; epilogue nothing (the set emitter resets). `AnsiHtmlEmitter` (`IMarkupSetEmitter` for Html so one `<span>` per run): fold layers → one style; port `WrapAsHtmlClass` from git history with `ColorToHex` = `ToRgb()?.ToHex()`. Pueblo/Mxp/BBCode emitters: `IMarkupSetEmitter` per format folding layers; colours/attributes via `SgrWriter.Transition(None, style)` + trailing `Reset`, links via the old `WrapAsPueblo`/`WrapAsMxp` tag forms (git history), BBCode as old `WrapAsBBCode`. `AnsiEscapeParser`: port `SharpMUSH.Library/Services/DatabaseConversion/AnsiEscapeParser.cs` onto `AnsiStyle` (delete the local 256-colour table; use `AnsiPalette`). `AnsiMarkupCodec` (`"ansi"`): keys `f`,`g` (colour: `"d"`, int 0–255 for Xterm, `"#rrggbb"` for Rgb; Standard written as Xterm index `i` or `i+8` when bright), `lt`,`lu`,`lk`,`bl`,`bo`,`cl`,`fa`,`in`,`it`,`ov`,`un`,`st` as before; read also accepts legacy byte arrays per the spec table. `AnsiRegistration.WithAnsi` registers all of the above.
- [ ] **Step 4: Pass, format, commit** `"MarkupString.Ansi: state-diffing SGR writer, emitters for six formats, codec"`.

### Task 7: `MarkupString.Html`

**Files:**
- Create: `SharpMUSH.MarkupString.Html/SharpMUSH.MarkupString.Html.csproj` (refs core + Ansi), `HtmlMarkup.cs`, `Emitters/HtmlTagEmitter.cs` (Html, Pueblo, Mxp: `<tag attrs>body</tag>`), `Emitters/HtmlToAnsiEmitter.cs` (Ansi: `b/strong`→Bold, `i/em`→Italic, `u`→Underlined, `s/strike/del`→StrikeThrough via `SgrWriter`, anything else → body), `HtmlMarkupCodec.cs` (`"html"`, keys `h`,`a`), `HtmlRegistration.cs` (`WithHtml`)
- Test: `SharpMUSH.MarkupString.Tests/Html/HtmlMarkupTests.cs` (port `SharpMUSH.Tests/Markup/HtmlMarkupTests.cs`), `dotnet sln add`, test project reference.

- [ ] **Step 1: Failing tests** — ported cases plus: `Wrap(HtmlMarkup("send","href=\"n\""), "north")` → Ansi `"north"`, Pueblo `<send href="n">north</send>`; `Wrap(red, Wrap(HtmlMarkup("b"), "x"))` → Ansi `"[1;31mx[0m"`; BBCode/Plain → `x`.
- [ ] **Step 2–4: Run, implement, pass, format, commit** `"MarkupString.Html: HtmlMarkup kind with emitters and codec"`.

### Task 8: Snapshot and property tests, benchmarks

**Files:**
- Modify: `SharpMUSH.MarkupString.Tests/SharpMUSH.MarkupString.Tests.csproj` (add `Verify.TUnit` latest 30.x and `CsCheck` latest 4.x)
- Create: `SharpMUSH.MarkupString.Tests/Snapshots/FormatSnapshotTests.cs` + `*.verified.txt`, `Properties/RoundTripProperties.cs`
- Modify: `SharpMUSH.Benchmarks/MStringBenchmarks.cs` (rewrite on the new API; add `RenderAnsiOneRun`, `RenderHtmlOneRun`, `SerializeOneRun`, `DeserializeOneRun`, `PlainAlloc`; `[GlobalSetup]` sets `MarkupRegistry.Default`; docs no longer mention F#)

- [ ] **Step 1:** Snapshot test: a fixture list of 20 `MarkupText` values (plain, each attribute, xterm, rgb, links of both kinds, nested, html send tag, mixed, unicode) rendered to each of the six formats; `await Verify(output).UseParameters(format.Name)`. Run once to create `.received`, review by eye against the spec's expectations, rename to `.verified`.
- [ ] **Step 2:** Properties with CsCheck: generator for `MarkupText` = random ASCII/CJK/emoji text + random non-overlapping runs of random `AnsiMarkup`/`HtmlMarkup` sets; assert `Deserialize(Serialize(x)).Render(f) == x.Render(f)` for all six formats; `x.Substring(a,b).Text` never contains a lone surrogate; `Concat(x.Substring(0,n), x.Substring(n)).Text == x.Text` for any `n` at a boundary; `DisplayWidth.Of(x.Pad(fill,w,Right,Truncate).Text) <= w`.
- [ ] **Step 3:** Benchmarks compile (the Benchmarks project still references the shim until Phase 2; write the new file against the new API now and keep `MModule` out of it). Commit `"MarkupText: snapshot, property, and benchmark coverage"`.

### Task 9: Compat shim `MModule`, `MushText`, `TextAligner`, solution build

**Files:**
- Create: `SharpMUSH.MarkupString/Compat/MModule.cs` (static class `MarkupString.MarkupStringModule` — the alias target of every `global using MModule`), `Compat/CompatEnums.cs` (none needed: `TrimType/PadType/TruncationType` live in core namespace already)
- Create: `SharpMUSH.Library/Markup/MushText.cs`, `ColumnSpec.cs`, `TextAligner.cs`
- Modify: every `GlobalUsings.cs` listed below: `MString` → `global::MarkupString.MarkupText`; add `global using MarkupString.Ansi; global using MarkupString.Html;`
- Modify: the 15 consumer csproj files (add `ProjectReference` to `..\SharpMUSH.MarkupString.Ansi\…` and `…Html\…`; in `SharpMUSH.Library.csproj` with `PrivateAssets="all"` like the core; in the three test fixture plugins and `examples/plugins/hello-ui` with the same `Private=false`/`ExcludeAssets` pattern the core uses there)
- Modify: `SharpMUSH.Client/SharpMUSH.Client.csproj` (`TrimmerRootAssembly` for `SharpMUSH.MarkupString` → `MarkupString`, `MarkupString.Ansi`, `MarkupString.Html`)
- Modify: registry bootstrap sites: `SharpMUSH.Server/Program.cs`, `SharpMUSH.ConnectionServer/Program.cs`, `SharpMUSH.Client/Program.cs`, `SharpMUSH.LanguageServer/Program.cs`, `SharpMUSH.Tests.Infrastructure` session setup (find the `[Before(TestSession)]` or `TestServerBase` hook; grep `Before(TestSession)`), `SharpMUSH.Tests.BUnit` session setup if it renders markup (grep), `SharpMUSH.Benchmarks` `[GlobalSetup]`: `MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();` guarded by `if (!MarkupRegistry.IsConfigured)`.
- Modify: `SharpMUSH.Library/Services/DatabaseConversion/PennMUSHDatabaseConverter.cs` (call `MarkupString.Ansi.AnsiEscapeParser.Parse`); delete `SharpMUSH.Library/Services/DatabaseConversion/AnsiEscapeParser.cs`.

GlobalUsings files: `SharpMUSH.Benchmarks, SharpMUSH.Database.ArangoDB, SharpMUSH.Database.Memgraph, SharpMUSH.Database.SurrealDB, SharpMUSH.Documentation, SharpMUSH.Implementation, SharpMUSH.Library, SharpMUSH.Plugins.Scene, SharpMUSH.Server, SharpMUSH.Tests.Infrastructure, SharpMUSH.Tests.Integration, SharpMUSH.Tests` (each `<proj>/GlobalUsings.cs`). Consumers without a GlobalUsings that use `MModule` via a local `using MModule = MarkupString.MarkupStringModule;` (Client, ConnectionServer, Contracts, CodeAnalysis, LanguageServer, Documentation renderers) keep working through the shim.

**Shim contents:** `public static class MarkupStringModule` reproducing every old public name used anywhere (`grep -rho 'MModule\.[A-Za-z0-9]*' --include=*.cs --include=*.razor . | sort -u`) as thin forwarders to the new API, with the old nullable-tolerant lowercase semantics (`single(null)` → `Empty`, `plainText(null)` → `""`, `getLength(null)` → 0, `concat(null,x)` → `x`, `multiple` filters nulls). MUSH-only names (`splitList`, `compressSpaces`, `isWildcardMatch*`, `getWildcardMatch*`, `getMatches`, `getRegexpMatches`) also forward, to copies of the old implementations placed in the shim file (they are deleted with it). `MarkupSingle` etc. accept `IMarkup`. `serialize`/`deserialize` forward to `MarkupTextSerializer`. `render(string, x)` and the instance `Render(string)` are NOT provided: fix those 7 call sites by hand in this task (`x.Render(MarkupFormat.Html)` etc.).

**`MushText`** (`SharpMUSH.Library.Markup`): `public static readonly MarkupText Error = MarkupText.Plain("#-1"), Zero, One, Comma;` `SplitList(MarkupText delimiter, MarkupText text)` (drops empties when delimiter is a single space); `CompressSpaces(MarkupText)` = `text.IndexOf("  ") < 0 ? text : text.ReplaceAll(<every run of 2+ spaces>, Space)` implemented by scanning the text once for runs of ≥2 spaces into `Edit`s and calling `Splice`; `Glob.ToRegex(string)`, `IsWildcardMatch(MarkupText, MarkupText)`, `IsWildcardMatch(MarkupText, string)`, `GetMatches(MarkupText, string)`, `GetRegexpMatches`, `GetWildcardMatches` ported from the old module (`git show HEAD~N:…MarkupStringModule.cs`, the `Glob*Regex` block).
**`TextAligner`** = old `TextAlignerModule` on the new API (`ExtractLine` uses `Substring`, `Justify` uses `Pad`, widths via `DisplayWidth`), `ColumnSpec`/`ColumnSpecParser` unchanged.

- [ ] **Step 1:** Write shim, `MushText`, `TextAligner`, update usings/csproj/bootstrap. Fix the direct uses of removed types outside the library (`grep -rn 'AnsiColor\.\(NoAnsi\|ANSI\|RGB\)\|AnsiCodeParser\.ParseCodes\|RenderFormat\.\|IRenderStrategy\|\.Render("' --include=*.cs --include=*.razor . | grep -v '/bin/\|/obj/\|SharpMUSH.MarkupString'`) by hand: `ParseCodes` → `Parse`; `NoAnsi.Instance` → `null`; `new AnsiColor.ANSI([31])` → `new AnsiColor.Standard(1, false)`; `Render("html")` → `Render(MarkupFormat.Html)`.
- [ ] **Step 2:** `dotnet build SharpMUSH.sln -p:SkipFormatVerification=true 2>&1 | grep -E 'error' | sort -u > /tmp/build.log` until empty. Then a formatted build without the skip.
- [ ] **Step 3:** Run the whole markup-related test surface: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/SharpMUSH.Tests.Markup/*/*" > /tmp/t.log 2>&1` and `/*/SharpMUSH.Tests.Services/*/*`, `/*/SharpMUSH.Tests.Commands/*/*`, `/*/SharpMUSH.Tests.Functions/*/*`. Update ANSI byte-stream expectations in `SharpMUSH.Tests` that encoded per-run resets (the diffed stream is the new contract; note it in each changed assertion). Everything else must pass unchanged.
- [ ] **Step 4:** Commit `"Compat shim, MushText, TextAligner: solution builds on the new packages"`.

## Phase 2: consumer migration (one subagent per group; groups B–F run in parallel after A)

Each migration task: (1) run the sed map below on the group's directories, (2) build the group's projects, (3) fix the remaining errors by hand using the spec's migration table, (4) audit `.ToString()` on `MString` values in the group (JSON/parse inputs → `ToPlainText()`; player-visible output → `Render(MarkupFormat.Ansi)` only where a raw string is genuinely needed, otherwise keep the `MString`), (5) run the group's tests, (6) `grep -rn 'MModule\.' <dirs>` must be empty, (7) format, commit `"Migrate <group> to MarkupText"`.

Sed map (run from the worktree root; `DIRS` = the group's directories):

```bash
for f in $(grep -rl 'MModule\.' --include=*.cs --include=*.razor $DIRS); do
sed -i -E \
 -e 's/MModule\.(single|Single)\(/MarkupText.Plain(/g' \
 -e 's/MModule\.(empty|Empty)\(\)/MarkupText.Empty/g' \
 -e 's/MModule\.Space\(\)/MarkupText.Space/g' \
 -e 's/MModule\.(concat|Concat)\(/MarkupText.Concat(/g' \
 -e 's/MModule\.(multiple|Multiple|ConcatMany|concatMany)\(/MarkupText.Concat(/g' \
 -e 's/MModule\.(multipleWithDelimiter|MultipleWithDelimiter)\(/MarkupText.Join(/g' \
 -e 's/MModule\.(multipleWithDelimiterFunc|MultipleWithDelimiterFunc)\(/MarkupText.Join(/g' \
 -e 's/MModule\.(MarkupSingle|markupSingle|MarkupSingle2|markupSingle2)\(/MarkupText.Wrap(/g' \
 -e 's/MModule\.(serialize|Serialize)\(/MarkupTextSerializer.Serialize(/g' \
 -e 's/MModule\.(deserialize|Deserialize)\(/MarkupTextSerializer.Deserialize(/g' \
 -e 's/MModule\.(splitList|SplitList)\(/MushText.SplitList(/g' \
 -e 's/MModule\.(compressSpaces|CompressSpaces)\(/MushText.CompressSpaces(/g' \
 -e 's/MModule\.(isWildcardMatch2?|IsWildcardMatch2?)\(/MushText.IsWildcardMatch(/g' \
 -e 's/MModule\.(getWildcardMatchAsRegex2?|GetWildcardMatchAsRegex2?)\(/MushText.Glob.ToRegex(/g' \
 -e 's/MModule\.(getWildcardMatches|GetWildcardMatches)\(/MushText.GetWildcardMatches(/g' \
 -e 's/MModule\.(getRegexpMatches|GetRegexpMatches)\(/MushText.GetRegexpMatches(/g' \
 -e 's/MModule\.(getMatches|GetMatches)\(/MushText.GetMatches(/g' \
 -e 's/TextAlignerModule\.(align|Align)\(/TextAligner.Align(/g' \
 "$f"; done
```

Argument-order changes must be done by hand after the sed (the compiler finds them): `plainText(x)` → `x.ToPlainText()`; `getLength(x)` → `x.Length`; `substring(s,l,x)` → `x.Substring(s,l)`; `split(d,x)` → `x.Split(d)`; `trim(x,c,t)` → `x.Trim(t,c)`; `pad(x,f,w,p,t)` → `x.Pad(f,w,p,t)`; `repeat(x,n)` → `x.Repeat(n)`; `remove/replace/insertAt/indexOf*/apply/apply2/concatAttach/center2` → instance methods; `MarkupSingleMulti(set,s)` → `MarkupText.Wrap(MarkupSet.Of(set), s)`; `MarkupMultiple(m,xs)` → `MarkupText.Wrap(m, MarkupText.Concat(xs))`; `plainText2` → `MarkupText.Plain(x.ToPlainText())`. Null-tolerant sites: the compiler flags `MarkupText?` arguments; resolve with `?? MarkupText.Empty` or an explicit branch — never suppress with `!` unless the value is provably non-null two lines up.

### Task 10 (group A): `SharpMUSH.Library` + `SharpMUSH.Contracts` + `SharpMUSH.Configuration` + `SharpMUSH.Messaging`
Includes `MarkupStringHandler.cs` and `MarkupTemplateFormatter.cs` rewritten onto the new API (handler builds a `List<MarkupText>` and `Concat`s; `color:` specifier uses `AnsiCodeParser.Parse`; `align:`/`trim:` use instance methods; XML docs no longer mention F#), `CallState.cs` (`_emptyMString = MarkupText.Empty`).

### Task 11 (group B): `SharpMUSH.Implementation` + `SharpMUSH.Implementation.Generated` + `SharpMUSH.Plugins.Scene`
Includes the `.ToString()` audit (27 `Message!.ToString()` sites → `ToPlainText()`), `HTMLFunctions.TagWrap` (returns `wrappedContent` as the `MString` itself, not `ToString()`), `GeneralCommands.WrapExitInSendTag` (`MarkupText.Wrap`), the `ansi()` function and `AnsiCodeParser.Parse`, `align()` → `TextAligner.Align`.

### Task 12 (group C): `SharpMUSH.Database`, `.ArangoDB`, `.Memgraph`, `.SurrealDB`, `SharpMUSH.Documentation`, `SharpMUSH.CodeAnalysis`, `SharpMUSH.LanguageServer`

### Task 13 (group D): `SharpMUSH.Server`, `SharpMUSH.ConnectionServer`, `SharpMUSH.Client`
`MarkupOutputRenderer` uses `MarkupFormat.TryParse`-free explicit mapping (`OutputFormat.Pueblo` → `MarkupFormat.Pueblo`, etc.); `TerminalFrameRenderer`/`SceneMarkupRenderer` use `Render(MarkupFormat.Html)`; the Blazor `MarkupString` struct clash is resolved with `using BlazorMarkup = Microsoft.AspNetCore.Components.MarkupString;` where needed.

### Task 14 (group E): `SharpMUSH.Tests.Infrastructure`, `SharpMUSH.Tests`, `SharpMUSH.Tests.Integration`, `SharpMUSH.Tests.BUnit`, test fixture plugins, `examples/plugins/hello-ui`, `SharpMUSH.Benchmarks`
`SharpMUSH.Tests/Markup/*` files are migrated (they now duplicate library tests; keep the ones that test SharpMUSH integration — handler, template formatter, align, compression on the bus — and delete pure-library duplicates that Task 1–7 ported, noting each deletion in the commit message).

### Task 15: delete the shim
- [ ] Delete `SharpMUSH.MarkupString/Compat/`, remove every `global using MModule` line, `grep -rn 'MModule\|MarkupStringModule\|TextAlignerModule' --include=*.cs --include=*.razor --include=*.md . | grep -v '/bin/\|/obj/'` must be empty (docs updated too). Full build, full `SharpMUSH.Tests` run (`> /tmp/full.log`, grep `failed:`), `SharpMUSH.Tests.BUnit` run, `SharpMUSH.MarkupString.Tests` run. Commit `"Remove the MModule compat shim"`.

## Phase 3: packaging and docs

### Task 16: NuGet hygiene and AOT smoke
**Files:** the three package csproj files; `SharpMUSH.MarkupString/README.md`, `SharpMUSH.MarkupString.Ansi/README.md`, `SharpMUSH.MarkupString.Html/README.md`; `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt` per package; `SharpMUSH.MarkupString.AotSmoke/SharpMUSH.MarkupString.AotSmoke.csproj` + `Program.cs`; `.github/workflows/_dotnet-build-test.yml`.

- [ ] Add to each package csproj: `<PackageReadmeFile>README.md</PackageReadmeFile>`, `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`, `<RepositoryUrl>` (from `git remote get-url origin`), `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<IncludeSymbols>true</IncludeSymbols>`, `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`, `<EnablePackageValidation>true</EnablePackageValidation>`, `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>`, `<MinVerTagPrefix>markupstring/v</MinVerTagPrefix>`, `<PackageReference Include="MinVer" Version="6.*" PrivateAssets="all" />`, `<PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="4.*" PrivateAssets="all" />`, `<None Include="README.md" Pack="true" PackagePath="/" />`. Generate the API files with `dotnet format analyzers <proj> --diagnostics=RS0016 --severity info` (creates entries in `PublicAPI.Unshipped.txt`).
- [ ] `AotSmoke`: console app, `PublishAot=true`, `TrimmerRootAssembly` for the three packages, `Program.cs` renders a nested string to all six formats and round-trips it, exits 1 on mismatch. CI: add a step to the `format` job: `dotnet publish SharpMUSH.MarkupString.AotSmoke -c Release -r linux-x64 -p:SkipFormatVerification=true 2>&1 | tee aot.log; ! grep -E 'IL[0-9]{4}' aot.log; ./SharpMUSH.MarkupString.AotSmoke/bin/Release/net10.0/linux-x64/publish/SharpMUSH.MarkupString.AotSmoke`. Verify locally first (needs `clang`; if unavailable, verify with `PublishTrimmed=true` and leave `PublishAot` for CI, saying so in the commit).
- [ ] `dotnet pack SharpMUSH.MarkupString -c Release -o /tmp/nupkgs` for each package succeeds. Commit `"MarkupString packages: NuGet metadata, public API tracking, AOT smoke test"`.

### Task 17: docs
- [ ] Update `CLAUDE.md` project map (three packages, `SharpMUSH.MarkupString.Tests`, `AotSmoke`), `docs/design/content-rendering-pipeline.md` (`Render(MarkupFormat.*)`, registry bootstrap, no `ToAnsi()/ToHtml()`), `docs/design/architectural-decisions.md` §(new) "Markup packages: registry, formats, kinds". Remove every remaining "F#" mention: `grep -rn 'F#' --include=*.cs --include=*.md . | grep -v '/bin/\|/obj/'`. Save the audit findings that motivated this as `docs/superpowers/specs/2026-09-06-markup-text-library-design.md` (already there). Commit `"Docs: markup packages"`.

## Self-review notes
- Spec coverage: defects 1 (Task 5/6), 2 (Task 1), 3 (Task 1/3), 4 (Task 2), 5 (Task 9 `MushText.CompressSpaces` via Task 2 `Splice`), 7 (Task 3), 8 (Task 3 encoders + Task 6 parser); expression pattern (Task 3); packages (Tasks 5–7); migration (Tasks 9–15); packaging (Task 16); docs (Task 17).
- Names used consistently: `MarkupText`, `MarkupSet.Of/Append`, `Run`, `MarkupFormat.{Plain,Ansi,Html,Pueblo,Mxp,BBCode,Custom}`, `MarkupRegistry.{Empty,Default,With,Find*}`, `IMarkupEmitter.Emit`, `IMarkupSetEmitter.TryEmit`, `IFormatFramer`, `IMarkupCodec`, `MarkupTextSerializer.{Serialize,Deserialize}`, `AnsiColor.{Default,Standard,Xterm,Rgb}`, `AnsiStyle.Combine`, `AnsiMarkup.Create`, `AnsiCodeParser.Parse`, `AnsiEscapeParser.Parse`, `SgrWriter.Transition`, `WithAnsi()`, `WithHtml()`, `MushText.*`, `TextAligner.Align`.
