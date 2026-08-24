# Attribute Syntax Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `cmdsyntax` and `funsyntax` attribute flags that make `@examine` and `@grep/PRINT` render the attribute as indented, syntax-highlighted softcode with syntax errors marked.

**Architecture:** Three independent passes over the same source joined by character offsets — classify (existing `GetSemanticTokens`), lay out (new `SoftcodeLayout`), diagnose (existing `ValidateAndGetErrors`). Layout is driven by the lexer token stream, not the parse tree, so it works on malformed input. Every line break lands where the lexer already absorbs whitespace, making the output a semantically identical program.

**Tech Stack:** .NET 10, C#, ANTLR4, TUnit, MarkupString (`MModule`), ArangoDB / SurrealDB / Memgraph.

**Spec:** `docs/superpowers/specs/2026-08-23-attribute-syntax-formatting-design.md`

## Reuse Inventory — read before writing any code

The tree already contains four MUSH-code renderers. **Do not add a fifth.** Every
task below either consumes one of these or replaces it; none forks it.

| Existing | Does | Consumed by | This plan |
|---|---|---|---|
| `MUSHCodeParser.AnalyzeSemanticTokens` → `GetSemanticTokens` | ANTLR-tree classifier; the source of truth for token categories | help files, LSP | **consume as-is** |
| `IMUSHCodeParser.Tokenize` → `TokenInfo` | lexer stream with absolute offsets | syntax tests | **consume as-is** (drives layout) |
| `IMUSHCodeParser.ValidateAndGetErrors` → `ParseError` | parse errors with position and expected tokens | LSP, MCP | **consume as-is** |
| `SemanticTokenAnsiPalette` + `RecursiveMarkdownRenderer.BuildSharpLineContent` | semantic tokens → styled `MString` | help-file code blocks | **extract and share** (Task 4) |
| `MushCodeAnalyzer.Format` | line-preserving text formatter (trim, `,`→`, `) | LSP `textDocument/formatting`, MCP `format` tool, Softcode Editor | **back it with the shared layout engine** (Task 8) |
| `MushcodeHighlighter` | regex tokenizer → HTML spans, plus the dangerous-pattern scanner | package review UI (`PackagesController`) | **left alone** — see Deferred |
| `SharpMUSH.Client/wwwroot/js/mush-monaco.js` | Monarch regex tokenizer, browser-side | Softcode Editor | **left alone** — browser-side, cannot call C#; the editor already receives accurate tokens from the LSP `SemanticTokensHandler`, and Monarch is its offline fast path |

The only genuinely new component in this plan is the **layout engine** (Task 2).
Everything else is wiring, sharing, or seeding.

## Global Constraints

- **C# style:** tabs, indent size 2. Enforced at build; `FORMAT001` failures are fixed with `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"`, run twice (the formatter needs two passes to converge).
- **Never `git add -A`.** Stage only the paths you changed. `dotnet format --folder` reindents unrelated files on this SDK.
- `TreatWarningsAsErrors` is on in `SharpMUSH.Library`, `SharpMUSH.Implementation`, and `SharpMUSH.Tests`.
- Prefer `var`; no `this.` qualifier; `OneOf<T1,T2>` over nullable service returns.
- Test framework is **TUnit**, not xUnit. Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/<Class>/*"`.
- Flag names are stored **lowercase**: `cmdsyntax`, `funsyntax`. Symbols are `x` and `f`.
- Line breaks may ONLY be emitted immediately after these lexer tokens: `OBRACK`, `OBRACE`, `COMMAWS`, `EQUALS`, `SEMICOLON`, `OPAREN`, `FUNCHAR`. v1 uses only `FUNCHAR`, `OPAREN`, `OBRACK`, `COMMAWS`, `SEMICOLON`.
- **Never emit a newline before a closer** (`CPAREN`, `CBRACK`, `CBRACE`). There is no whitespace absorption there; the newline would become literal data. Closers cuddle the last item.
- **Never break inside an `OBRACE` group.** Brace contents are atomic in v1.

---

### Task 1: The two attribute flags

**Files:**
- Create: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddSyntaxFlags.cs`
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs:553-577`
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs:558-581`
- Modify: `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs`
- Test: `SharpMUSH.Tests/Database/AttributeSyntaxFlagTests.cs`

**Interfaces:**
- Produces: `SharpAttributeExtensions.IsCmdSyntax(this SharpAttribute) → bool`, `IsFunSyntax(this SharpAttribute) → bool`, `SyntaxParseType(this SharpAttribute) → ParseType?` (returns `ParseType.CommandList` for cmdsyntax, `ParseType.Function` for funsyntax, `null` for neither; cmdsyntax wins if both set).

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Database;

public class AttributeSyntaxFlagTests
{
	private static SharpAttribute WithFlags(params string[] names) => new()
	{
		Name = "TEST",
		Flags = names.Select(n => new SharpAttributeFlag
		{
			Name = n, Symbol = n == "cmdsyntax" ? "x" : "f", System = true, Inheritable = true
		}).ToArray(),
		Value = MModule.single("say hi"),
		LongName = "TEST",
		CommandListIndex = null,
		Owner = null!
	};

	[Test]
	public async Task CmdSyntaxFlag_MapsToCommandList()
	{
		await Assert.That(WithFlags("cmdsyntax").IsCmdSyntax()).IsTrue();
		await Assert.That(WithFlags("cmdsyntax").SyntaxParseType()).IsEqualTo(ParseType.CommandList);
	}

	[Test]
	public async Task FunSyntaxFlag_MapsToFunction()
	{
		await Assert.That(WithFlags("funsyntax").IsFunSyntax()).IsTrue();
		await Assert.That(WithFlags("funsyntax").SyntaxParseType()).IsEqualTo(ParseType.Function);
	}

	[Test]
	public async Task BothFlags_CommandWins()
		=> await Assert.That(WithFlags("cmdsyntax", "funsyntax").SyntaxParseType())
			.IsEqualTo(ParseType.CommandList);

	[Test]
	public async Task NoFlags_ReturnsNull()
		=> await Assert.That(WithFlags().SyntaxParseType()).IsNull();

	[Test]
	public async Task IsNoDebug_MatchesSeededFlagName()
		=> await Assert.That(WithFlags("no_debug").IsNoDebug()).IsTrue();
}
```

Note: the `SharpAttribute` initialiser above must match the real record's required members. Open `SharpMUSH.Library/Models/SharpAttribute.cs` and adjust the property list to compile — do not change the assertions.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeSyntaxFlagTests/*"`
Expected: compile failure — `IsCmdSyntax` is not defined.

- [ ] **Step 3: Add the extensions**

In `SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs`, add `using SharpMUSH.Library.ParserInterfaces;` and append inside the class:

```csharp
	/// <summary>
	/// The attribute holds a command list (<c>cmdsyntax</c>), for display formatting.
	/// Unrelated to <c>no_command</c>, which governs $-command matching.
	/// </summary>
	public static bool IsCmdSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "cmdsyntax");

	/// <summary>
	/// The attribute holds a function expression (<c>funsyntax</c>), for display formatting.
	/// </summary>
	public static bool IsFunSyntax(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "funsyntax");

	/// <summary>
	/// The parse dialect declared by the syntax flags, or <c>null</c> when neither is set.
	/// <c>cmdsyntax</c> wins when both are present: a command list may contain function
	/// calls, but not the reverse.
	/// </summary>
	public static ParseType? SyntaxParseType(this SharpAttribute attribute)
		=> attribute.IsCmdSyntax() ? ParseType.CommandList
			: attribute.IsFunSyntax() ? ParseType.Function
			: null;
```

Also fix the existing typo on line 65-66 — the seeded flag name is `no_debug`, so this check can never fire today:

```csharp
	public static bool IsNoDebug(this SharpAttribute attribute)
		=> attribute.Flags.Any(x => x.Name == "no_debug");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeSyntaxFlagTests/*"`
Expected: PASS (5 tests).

- [ ] **Step 5: Seed the flags in all three providers**

Create `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddSyntaxFlags.cs`:

```csharp
using Core.Arango;
using Core.Arango.Migration;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>cmdsyntax</c> and <c>funsyntax</c> attribute flags, which declare which
/// softcode dialect an attribute holds so display commands can format it correctly.
///
/// <para>An attribute that does not begin with <c>$</c> is genuinely ambiguous between a
/// command list invoked by <c>@trigger</c> and a function body invoked by <c>u()</c>. Both
/// parse, differently. These flags remove the guess; they map onto <c>ParseType.CommandList</c>
/// and <c>ParseType.Function</c> respectively.</para>
///
/// <para>UPSERT keyed on name, so it runs on fresh and existing databases alike. The Memgraph
/// and SurrealDB providers reach the same end through their always-run idempotent flag seeds;
/// only Arango needs a migration id.</para>
/// </summary>
public class Migration_AddSyntaxFlags : IArangoMigration
{
	public long Id => 20260823_001;

	public string Name => "add_syntax_flags";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		foreach (var (name, symbol) in new[] { ("cmdsyntax", "x"), ("funsyntax", "f") })
		{
			await migrator.Context.Query.ExecuteAsync<object>(
				handle,
				"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
				bindVars: new Dictionary<string, object>
				{
					{ "@c", DatabaseConstants.AttributeFlags },
					{ "name", name },
					{ "doc", new { Name = name, Symbol = symbol, System = true, Inheritable = true } }
				});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
```

Before committing, confirm the document shape matches what `Migration_CreateDatabase.CreateInitialAttributeFlags` writes for attribute flags (around `Migration_CreateDatabase.cs:2743-2913`) — attribute flags carry `Name`/`Symbol`/`System`/`Inheritable`, unlike object flags which also carry permissions. Confirm `20260823_001` is still unused: `grep -rho "Id => [0-9_]*" SharpMUSH.Database.ArangoDB/Migrations/`.

In `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs`, append to the `attrFlags` array (after the `prefixmatch` entry at :576):

```csharp
			("cmdsyntax", "x", true),
			("funsyntax", "f", true),
```

In `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs`, append the equivalent two entries to its tuple array at :558-581, matching that file's tuple shape exactly (read it first — it may differ from SurrealDB's).

- [ ] **Step 6: Verify the seeds and commit**

Run: `dotnet build SharpMUSH.Database.ArangoDB SharpMUSH.Database.SurrealDB SharpMUSH.Database.Memgraph SharpMUSH.Library`
Expected: builds clean.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/AttributeSyntaxFlagTests/*"`
Expected: PASS.

```bash
git add SharpMUSH.Database.ArangoDB/Migrations/Migration_AddSyntaxFlags.cs \
        SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs \
        SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs \
        SharpMUSH.Library/Extensions/SharpAttributeExtensions.cs \
        SharpMUSH.Tests/Database/AttributeSyntaxFlagTests.cs
git commit -m "Add cmdsyntax and funsyntax attribute flags"
```

---

### Task 2: The layout engine

**Files:**
- Create: `SharpMUSH.Library/Services/SoftcodeLayout.cs`
- Test: `SharpMUSH.Tests/Formatting/SoftcodeLayoutTests.cs`

**Interfaces:**
- Consumes: `TokenInfo` (`SharpMUSH.Library/Models/TokenInfo.cs`) — has `Type` (lexer rule name), `StartIndex`, `EndIndex` (inclusive), `Text`, `Length`.
- Produces: `SoftcodeBreak(int TokenIndex, int Indent)` and `SoftcodeLayout.Compute(IReadOnlyList<TokenInfo> tokens, int width, int indentUnit = 2) → IReadOnlyList<SoftcodeBreak>`. A break means: after emitting `tokens[TokenIndex]` **with its trailing whitespace trimmed**, emit `\n` followed by `Indent` spaces.

This task is pure — no parser, no MString, no I/O. The tests define correctness.

- [ ] **Step 1: Write the failing tests**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

public class SoftcodeLayoutTests
{
	/// <summary>Renders a token list plus its breaks back to text, so tests read as before/after.</summary>
	private static string Render(IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks)
	{
		var byIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < tokens.Count; i++)
		{
			if (byIndex.TryGetValue(i, out var indent))
			{
				sb.Append(tokens[i].Text.TrimEnd());
				sb.Append('\n').Append(new string(' ', indent));
			}
			else
			{
				sb.Append(tokens[i].Text);
			}
		}

		return sb.ToString();
	}

	private static IReadOnlyList<TokenInfo> Lex(string source) => TestLexer.Lex(source);

	[Test]
	public async Task ShortInput_FitsFlat_NoBreaks()
	{
		var tokens = Lex("add(1,2)");
		var breaks = SoftcodeLayout.Compute(tokens, width: 78);
		await Assert.That(breaks).IsEmpty();
	}

	[Test]
	public async Task LongCall_BreaksAfterOpenParenAndCommas()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered).IsEqualTo(
			"""
			switch(
			  words(%0),
			  0,
			  nothing at all,
			  1,
			  just one,
			  many words here)
			""");
	}

	[Test]
	public async Task Closer_CuddlesLastItem_NeverOnItsOwnLine()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered).DoesNotContain("\n)");
		await Assert.That(rendered.TrimEnd()).EndsWith(")");
	}

	[Test]
	public async Task BraceGroups_AreNeverBrokenInside()
	{
		const string src = "switch(%0,1,{say a very long thing indeed, honestly},2,{other})";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 20));

		await Assert.That(rendered).Contains("{say a very long thing indeed, honestly}");
	}

	[Test]
	public async Task NestedGroups_IndentByDepth()
	{
		const string src = "switch(add(one thing,another thing),1,yes,no)";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 20));

		await Assert.That(rendered).Contains("\n  ");
		await Assert.That(rendered).Contains("\n    ");
	}

	[Test]
	public async Task SemicolonsBreakCommandLists()
	{
		const string src = "@pemit %#=first message here;@emit second message here;@wait 0=third";
		var tokens = Lex(src);
		var rendered = Render(tokens, SoftcodeLayout.Compute(tokens, width: 30));

		await Assert.That(rendered.Split('\n')).HasCount().EqualTo(3);
	}

	[Test]
	public async Task UnbalancedOpenParen_DoesNotThrow()
	{
		var tokens = Lex("switch(a,b,c");
		var breaks = SoftcodeLayout.Compute(tokens, width: 10);
		await Assert.That(breaks).IsNotNull();
	}

	[Test]
	public async Task UnbalancedCloseParen_DoesNotThrow()
	{
		var tokens = Lex("a,b,c)))");
		var breaks = SoftcodeLayout.Compute(tokens, width: 10);
		await Assert.That(breaks).IsNotNull();
	}

	[Test]
	public async Task IndentIsClampedToHalfWidth()
	{
		var src = string.Concat(Enumerable.Repeat("f(", 40)) + "x" + string.Concat(Enumerable.Repeat(")", 40));
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 40);

		await Assert.That(breaks.All(b => b.Indent <= 20)).IsTrue();
	}

	[Test]
	public async Task EveryBreakFollowsAWhitespaceAbsorbingToken()
	{
		const string src = "switch(words(%0),0,nothing,1,[ucstr(%0)],{literal, text},done)";
		var tokens = Lex(src);
		var breaks = SoftcodeLayout.Compute(tokens, width: 20);

		string[] safe = ["FUNCHAR", "OPAREN", "OBRACK", "OBRACE", "COMMAWS", "EQUALS", "SEMICOLON"];
		foreach (var b in breaks)
		{
			await Assert.That(safe).Contains(tokens[b.TokenIndex].Type);
		}
	}
}
```

`TestLexer.Lex` does not exist yet. Create `SharpMUSH.Tests/Formatting/TestLexer.cs` wrapping the generated lexer directly so these tests need no server fixture:

```csharp
using Antlr4.Runtime;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Formatting;

/// <summary>Lexes source to <see cref="TokenInfo"/> without standing up a parser fixture.</summary>
public static class TestLexer
{
	public static IReadOnlyList<TokenInfo> Lex(string source)
	{
		var lexer = new SharpMUSHLexer(new AntlrInputStream(source));
		var stream = new CommonTokenStream(lexer);
		stream.Fill();

		return stream.GetTokens()
			.Where(t => t.Type != TokenConstants.EOF)
			.Select(t => new TokenInfo
			{
				Type = lexer.Vocabulary.GetSymbolicName(t.Type) ?? "UNKNOWN",
				StartIndex = t.StartIndex,
				EndIndex = t.StopIndex,
				Text = t.Text,
				Line = t.Line,
				Column = t.Column,
				Channel = t.Channel
			})
			.ToList();
	}
}
```

Check the generated lexer's namespace and adjust the `using` — see how `MUSHCodeParser.Tokenize` (`SharpMUSH.Implementation/MUSHCodeParser.cs:648-681`) constructs its lexer and mirror that exactly.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SoftcodeLayoutTests/*"`
Expected: compile failure — `SoftcodeLayout` is not defined.

- [ ] **Step 3: Implement the layout engine**

Create `SharpMUSH.Library/Services/SoftcodeLayout.cs`. The algorithm:

1. **Build a group tree.** Walk tokens with a stack, starting from a synthetic root group. On `FUNCHAR`, `OPAREN`, `OBRACK`, or `OBRACE`, push a new child group whose `OpenIndex` is that token. On `CPAREN`, `CBRACK`, or `CBRACE`, set `CloseIndex` and pop (ignore the closer if the stack holds only the root — unbalanced input must not throw). On `COMMAWS` or `SEMICOLON`, record the index in the current group's separator list. At end of input, any groups still open close implicitly at the last token.

2. **Compute flat widths** bottom-up: a group's flat width is the summed `Text.Length` of tokens from `OpenIndex` through `CloseIndex` inclusive.

3. **Render top-down** from the root, tracking the current column. For each group:
   - If `column + flatWidth <= width`, render flat: add no breaks anywhere inside it, advance `column` by `flatWidth`, and do not recurse.
   - Otherwise break: emit a break after `OpenIndex` and after each of the group's own separator indices, all at `Indent = Math.Min((depth + 1) * indentUnit, width / 2)`. Recurse into child groups at `depth + 1`. Never emit a break at `CloseIndex` or immediately before it.
   - A group whose opener is `OBRACE` is always rendered flat regardless of width, and is never recursed into.

4. Return the breaks ordered by token index.

Invariant to hold onto: because every break sits immediately after a whitespace-absorbing token, a wrong grouping decision produces ugly output but never changes meaning.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SoftcodeLayoutTests/*"`
Expected: PASS (10 tests).

If `LongCall_BreaksAfterOpenParenAndCommas` disagrees only in whitespace, fix the implementation rather than the expectation — the exact rendering is the contract.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/SoftcodeLayout.cs \
        SharpMUSH.Tests/Formatting/SoftcodeLayoutTests.cs \
        SharpMUSH.Tests/Formatting/TestLexer.cs
git commit -m "Add the softcode layout engine"
```

---

### Task 3: Prove the layout is semantics-preserving

**Files:**
- Test: `SharpMUSH.Tests/Formatting/SoftcodeLayoutEquivalenceTests.cs`

**Interfaces:**
- Consumes: `SoftcodeLayout.Compute` from Task 2.

This is the load-bearing test of the whole feature. It is worth its own task because it is the gate for ever relaxing the atomic-brace rule.

- [ ] **Step 1: Write the equivalence test**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// The formatter's central claim: breaks land only where the lexer already absorbs
/// whitespace (<c>fragment WS: [ \r\n\f\t]*</c>), so formatted source lexes to the same
/// token stream as the original. Compares lexer output modulo trailing whitespace inside
/// the whitespace-absorbing tokens themselves, which is exactly what WS discards.
/// </summary>
public class SoftcodeLayoutEquivalenceTests
{
	public static IEnumerable<Func<string>> Corpus() =>
	[
		() => "add(1,2)",
		() => "switch(words(%0),0,You said nothing.,1,[ucstr(%0)],Too many: [words(%0)])",
		() => "@pemit %#=Hello there;@emit %N waves;&LAST me=[secs()]",
		() => "$greet *:@pemit %#=[ansi(hy,Hi,)] [capstr(%0)]!;@emit %N greets [capstr(%0)].",
		() => "iter(lnum(1,10),[add(##,1)] ,%b,%b)",
		() => "[u(me/FUNC,arg one,arg two)]",
		() => "switch(%0,1,{say a, with comma},2,{other, thing},default)",
		() => "strcat(a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z)",
		() => "@dolist [lcon(here)]={@pemit ##=ping;@emit pong}",
		() => "ansi(hr,[center(Title,78,-)])",
	];

	private static string Render(IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks)
	{
		var byIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < tokens.Count; i++)
		{
			if (byIndex.TryGetValue(i, out var indent))
			{
				sb.Append(tokens[i].Text.TrimEnd()).Append('\n').Append(new string(' ', indent));
			}
			else
			{
				sb.Append(tokens[i].Text);
			}
		}

		return sb.ToString();
	}

	/// <summary>Token stream with whitespace-absorbing tokens normalised to their delimiter.</summary>
	private static string[] Normalised(string source) =>
		TestLexer.Lex(source)
			.Select(t => t.Type is "OBRACK" or "OBRACE" or "COMMAWS" or "EQUALS"
				or "SEMICOLON" or "OPAREN" or "FUNCHAR"
				? $"{t.Type}:{t.Text.TrimEnd()}"
				: $"{t.Type}:{t.Text}")
			.ToArray();

	[Test]
	[MethodDataSource(nameof(Corpus))]
	public async Task FormattedSource_LexesIdentically(string source)
	{
		foreach (var width in (int[])[20, 40, 78])
		{
			var tokens = TestLexer.Lex(source);
			var formatted = Render(tokens, SoftcodeLayout.Compute(tokens, width));

			await Assert.That(Normalised(formatted))
				.IsEquivalentTo(Normalised(source))
				.Because($"width {width} changed the token stream for: {source}");
		}
	}

	[Test]
	[MethodDataSource(nameof(Corpus))]
	public async Task FormattedSource_PreservesAllNonWhitespaceCharacters(string source)
	{
		var tokens = TestLexer.Lex(source);
		var formatted = Render(tokens, SoftcodeLayout.Compute(tokens, width: 20));

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

		await Assert.That(Strip(formatted)).IsEqualTo(Strip(source));
	}
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SoftcodeLayoutEquivalenceTests/*"`
Expected: PASS.

A failure here means the layout engine broke at an unsafe position. Fix `SoftcodeLayout`, never the test. If a corpus entry legitimately cannot be formatted safely, the correct fix is for `Compute` to emit fewer breaks for it.

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Tests/Formatting/SoftcodeLayoutEquivalenceTests.cs
git commit -m "Prove formatted softcode lexes identically to its source"
```

---

### Task 4: Extract the shared semantic-token ANSI renderer

**Files:**
- Create: `SharpMUSH.Library/Services/SemanticTokenAnsiPalette.cs` (moved)
- Delete: `SharpMUSH.Documentation/MarkdownToAsciiRenderer/SemanticTokenAnsiPalette.cs`
- Create: `SharpMUSH.Library/Services/SemanticTokenRenderer.cs`
- Modify: `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.CodeBlock.cs`
- Test: `SharpMUSH.Tests/Formatting/SemanticTokenRendererTests.cs`

**Interfaces:**
- Produces: `SemanticTokenAnsiPalette.GetStyle(SemanticTokenType, SemanticTokenModifier) → Ansi?` — unchanged signature, namespace becomes `SharpMUSH.Library.Services`.
- Produces:

```csharp
public static MString SemanticTokenRenderer.Render(
	MString source,
	IReadOnlyList<SemanticToken> tokens,
	Func<int, Ansi?>? overrideAt = null);
```

Applies palette styles to `source` over each token's span. `overrideAt` is consulted per character offset and takes precedence when it returns non-null — Task 5 uses it for error spans. Returns `source` unstyled when `tokens` is empty.

This is the deduplication step. `RecursiveMarkdownRenderer.BuildSharpLineContent` (`:230-268`) currently owns the only "semantic tokens → styled MString" loop in the tree, and Task 5 needs exactly that. Extract it once rather than writing a second copy.

`SharpMUSH.Library` cannot reference `SharpMUSH.Documentation` (the dependency runs the other way), so the shared code lands in Library. `ANSILibrary` is a folder inside `SharpMUSH.MarkupString`, which `SharpMUSH.Library` already references, so `Ansi` stays available.

- [ ] **Step 1: Move the palette**

```bash
git mv SharpMUSH.Documentation/MarkdownToAsciiRenderer/SemanticTokenAnsiPalette.cs \
       SharpMUSH.Library/Services/SemanticTokenAnsiPalette.cs
```

Change its namespace to `SharpMUSH.Library.Services`. Keep the body otherwise byte-identical — the palette's colours are already settled and must not drift.

- [ ] **Step 2: Write the failing test for the shared renderer**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

public class SemanticTokenRendererTests
{
	private static SemanticToken Tok(int start, string text, SemanticTokenType type) => new()
	{
		Range = new Range { Start = new Position(0, start), End = new Position(0, start + text.Length) },
		TokenType = type,
		Text = text
	};

	[Test]
	public async Task NoTokens_ReturnsSourceUnchanged()
	{
		var result = SemanticTokenRenderer.Render(MModule.single("add(1,2)"), []);
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task PlainTextIsPreserved_WhenStylesApply()
	{
		var src = MModule.single("add(1,2)");
		var result = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function), Tok(4, "1", SemanticTokenType.Number)]);

		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task StylesAreActuallyApplied()
	{
		var src = MModule.single("add(1,2)");
		var styled = SemanticTokenRenderer.Render(src, [Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.serialize(styled)).IsNotEqualTo(MModule.serialize(src));
	}

	[Test]
	public async Task OverrideTakesPrecedenceOverPalette()
	{
		var src = MModule.single("add(1,2)");
		var red = AnsiCodeParser.ParseCodes("r");
		var withOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)], offset => offset < 4 ? red : null);
		var withoutOverride = SemanticTokenRenderer.Render(src,
			[Tok(0, "add(", SemanticTokenType.Function)]);

		await Assert.That(MModule.serialize(withOverride)).IsNotEqualTo(MModule.serialize(withoutOverride));
		await Assert.That(MModule.plainText(withOverride)).IsEqualTo("add(1,2)");
	}
}
```

Adjust `MModule.serialize` to whatever the markup-comparison helper is actually called — check `SharpMUSH.MarkupString/MarkupStringModule.cs` for the serialisation entry point and use it consistently.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SemanticTokenRendererTests/*"`
Expected: compile failure — `SemanticTokenRenderer` is not defined.

- [ ] **Step 4: Implement the shared renderer**

Create `SharpMUSH.Library/Services/SemanticTokenRenderer.cs`. Lift the loop from `RecursiveMarkdownRenderer.BuildSharpLineContent:260-267`, with two changes:

- Slice spans out of `source` by offset (`MModule.substring`) instead of emitting `token.Text`, so pre-existing author markup in `source` survives and no characters are lost if the token list fails to tile the input.
- Consult `overrideAt` before the palette for each span.

Assemble with `MModule.multiple` (routes to `ConcatMany`, one `StringBuilder` pass). Never call `MModule.concat` in a loop — it is O(n) per call and quadratic over a token list.

Convert `SemanticToken.Range` (line/character) to absolute offsets with a line-start table over `MModule.plainText(source)`; attribute values may contain newlines since PR #775.

- [ ] **Step 5: Route the Markdown renderer through it**

In `RecursiveMarkdownRenderer.CodeBlock.cs`, replace the per-token loop in `BuildSharpLineContent` with a call to `SemanticTokenRenderer.Render`, keeping the existing prompt-prefix handling and the `sortedTokens.Count == 0` early return. Add `using SharpMUSH.Library.Services;`.

Keep its parse-type detection (`&`/`@`/`$` → `CommandList`, else `Function`) exactly where it is — that heuristic is correct for help files, which have no attribute flags to consult.

- [ ] **Step 6: Verify nothing regressed**

Run: `grep -rn "SemanticTokenAnsiPalette" --include="*.cs" .`
Expected: only the moved file and `SemanticTokenRenderer.cs`. Fix any other hit.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SemanticTokenRendererTests/*"`
Expected: PASS (4 tests).

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Markdown*/*"` and `--treenode-filter "/*/*/*Highlight*/*"`
Expected: PASS — help-file code block rendering must be byte-identical to before. This is the regression gate for the extraction.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Library/Services/SemanticTokenAnsiPalette.cs \
        SharpMUSH.Library/Services/SemanticTokenRenderer.cs \
        SharpMUSH.Documentation/MarkdownToAsciiRenderer/ \
        SharpMUSH.Tests/Formatting/SemanticTokenRendererTests.cs
git commit -m "Extract the shared semantic-token ANSI renderer into Library"
```

---

### Task 5: The formatter

**Files:**
- Create: `SharpMUSH.Library/Services/SoftcodeFormatter.cs`
- Test: `SharpMUSH.Tests/Formatting/SoftcodeFormatterTests.cs`

**Interfaces:**
- Consumes: `SoftcodeLayout.Compute` (Task 2), `SemanticTokenAnsiPalette.GetStyle` (Task 4), `TokenInfo`, `SemanticToken`, `ParseError`.
- Produces:

```csharp
public static MString Format(
	MString source,
	IReadOnlyList<TokenInfo> tokens,
	IReadOnlyList<SemanticToken> semanticTokens,
	IReadOnlyList<ParseError> errors,
	int width);
```

Takes already-computed analysis rather than an `IMUSHCodeParser`, so it unit-tests with no parse infrastructure. Callers do the parser calls.

- [ ] **Step 1: Write the failing tests**

```csharp
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

public class SoftcodeFormatterTests
{
	private static MString Format(string src, IReadOnlyList<SemanticToken>? sem = null,
		IReadOnlyList<ParseError>? errors = null, int width = 78)
		=> SoftcodeFormatter.Format(MModule.single(src), TestLexer.Lex(src),
			sem ?? [], errors ?? [], width);

	[Test]
	public async Task PlainText_RoundTripsUnchanged()
	{
		var result = Format("add(1,2)");
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task LongInput_GainsNewlines()
	{
		var result = Format("switch(words(%0),0,nothing at all,1,just one,many here)", width: 30);
		await Assert.That(MModule.plainText(result)).Contains("\n");
	}

	[Test]
	public async Task NoCharactersAreLost_EvenWithoutSemanticTokens()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many here)";
		var result = MModule.plainText(Format(src, width: 30));

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
		await Assert.That(Strip(result)).IsEqualTo(Strip(src));
	}

	[Test]
	public async Task ErrorSummary_IsAppendedBeneathTheCode()
	{
		var errors = new[]
		{
			new ParseError
			{
				Line = 1, Column = 7, Message = "mismatched input",
				OffendingToken = ")", ExpectedTokens = ["COMMAWS", "CPAREN"]
			}
		};

		var result = MModule.plainText(Format("add(1,2", errors: errors));

		await Assert.That(result).Contains("add(1,2");
		await Assert.That(result).Contains("position 7");
	}

	[Test]
	public async Task NoErrors_AppendsNoSummary()
	{
		var result = MModule.plainText(Format("add(1,2)"));
		await Assert.That(result.Split('\n')).HasCount().EqualTo(1);
	}

	[Test]
	public async Task EmptyInput_ReturnsEmpty()
	{
		var result = SoftcodeFormatter.Format(MModule.empty(), [], [], [], 78);
		await Assert.That(MModule.plainText(result)).IsEqualTo("");
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SoftcodeFormatterTests/*"`
Expected: compile failure — `SoftcodeFormatter` is not defined.

- [ ] **Step 3: Implement the formatter**

Create `SharpMUSH.Library/Services/SoftcodeFormatter.cs`. It **composes** Tasks 2 and 4 and owns no highlighting logic of its own:

1. Convert each `ParseError` (`Line` 1-based, `Column` 0-based) to an absolute offset using a line-start table over `MModule.plainText(source)`. Reuse the same table helper `SemanticTokenRenderer` uses — extract it to an internal shared helper if it is not already one; do not write a second copy.
2. Colour the source in one call: `SemanticTokenRenderer.Render(source, semanticTokens, overrideAt)`, where `overrideAt` returns the error style (inverse video plus red foreground, via `AnsiCodeParser.ParseCodes`) for offsets inside an error span and `null` elsewhere. This is the entire highlighting step — the palette lookup and span slicing already live in `SemanticTokenRenderer`.
3. Compute breaks via `SoftcodeLayout.Compute(tokens, width)`, then apply them to the coloured `MString`: for each break, trim the trailing whitespace inside that token's span and insert `\n` plus the indent. Use `MModule.substring` / `MModule.insertAt` and assemble with `MModule.multiple` (routes to `ConcatMany`, one `StringBuilder` pass). **Never** call `MModule.concat` in a loop; it is O(n) per call and quadratic over a token list.
4. If `errors` is non-empty, append `\n` and one line per error using `ParseError.ToMushFailureString()` — the existing MUSH-facing formatter, which already renders position, expected tokens, and snippet. Do not invent a new error string format.

Apply breaks by offset against the coloured result rather than rebuilding from `token.Text`, so styling and author markup both survive.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SoftcodeFormatterTests/*"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/SoftcodeFormatter.cs \
        SharpMUSH.Tests/Formatting/SoftcodeFormatterTests.cs
git commit -m "Add the softcode formatter"
```

---

### Task 6: Wire `@examine`

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/GeneralCommands.cs:1087-1111`
- Test: `SharpMUSH.Tests/Commands/ExamineSyntaxFormattingTests.cs`

**Interfaces:**
- Consumes: `SyntaxParseType` (Task 1), `SoftcodeFormatter.Format` (Task 5).

- [ ] **Step 1: Write the failing test**

Fixture pattern copied from `SharpMUSH.Tests/Commands/ExamineNullOwnerTests.cs`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ExamineSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private Task Expect(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	[Test]
	public async ValueTask FlaggedAttribute_IsBrokenAcrossLines()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOn");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// A newline followed by indentation is the formatter's signature.
		await Expect("\n  ");
	}

	[Test]
	public async ValueTask UnflaggedAttribute_RendersVerbatim()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOff");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		await Expect(LongCode);
	}

	[Test]
	public async ValueTask FlaggedAttribute_LosesNoCharacters()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtIntact");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// Whitespace moves; nothing else may.
		await Expect("many words indeed here)");
	}
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ExamineSyntaxFormattingTests/*"`
Expected: FAIL — flagged output is still one line.

- [ ] **Step 3: Implement**

In the attribute loop at `GeneralCommands.cs:1092-1111`, replace the single `Notify` with: compute `attr.SyntaxParseType()`; when `null`, keep today's exact behaviour; when non-null, notify the highlighted header on its own line, then notify the formatted block.

Read the connection width the way `ConnectionFunctions.cs:1033-1044` does for `width()`, falling back to `78` when the executor has no connection — a queued or `@trigger`ed `@examine` has no terminal.

While in this block, delete the dead local at line 1089: `showPublicOnly` is assigned from `Configuration!.CurrentValue.Cosmetic.ExaminePublicAttributes` and never read.

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ExamineSyntaxFormattingTests/*"`
Expected: PASS.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Examine*/*"`
Expected: PASS — no existing examine test regresses.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Implementation/Commands/GeneralCommands.cs \
        SharpMUSH.Tests/Commands/ExamineSyntaxFormattingTests.cs
git commit -m "Format flagged attributes in @examine output"
```

---

### Task 7: Wire `@grep/PRINT` and set-time validation

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/GeneralCommands.cs:6092-6135`
- Modify: `SharpMUSH.Library/Services/AttributeService.cs`
- Test: `SharpMUSH.Tests/Commands/GrepSyntaxFormattingTests.cs`

**Interfaces:**
- Consumes: `SoftcodeFormatter.Format` (Task 5), `SyntaxParseType` (Task 1).

- [ ] **Step 1: Wire `@grep/PRINT`**

At `GeneralCommands.cs:6092-6135`, apply the same treatment as Task 6: when the attribute carries a syntax flag, render the formatted block. The existing match-highlighting at `:6106-6122` composes on top — it slices by plain-text `IndexOf`, so apply it to the formatted result rather than the raw value.

- [ ] **Step 2: Add set-time validation**

In `SharpMUSH.Library/Services/AttributeService.cs`, in the attribute-set path, after the value is stored: if the attribute carries a syntax flag, call `ValidateAndGetErrors(value, parseType)` and, when it returns errors, notify the setter with each `ToMushFailureString()`.

**It must never refuse the set.** PennMUSH does not validate at set time, and parity governs. This is advisory output only.

- [ ] **Step 3: Write and run tests**

Same fixture shape as Task 6. Add to `SharpMUSH.Tests/Commands/GrepSyntaxFormattingTests.cs`:

```csharp
	private const string BrokenCode = "add(1,2";

	[Test]
	public async ValueTask SettingBrokenCodeIntoFlaggedAttribute_WarnsButStillStores()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetWarnOn");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}=placeholder"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/BAD=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}={BrokenCode}"));

		await Expect("PARSER FAILURE");

		// Advisory only — the value must still be stored.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"think [get({obj}/BAD)]"));
		await Expect(BrokenCode);
	}

	[Test]
	public async ValueTask SettingBrokenCodeIntoUnflaggedAttribute_IsSilent()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetWarnOff");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}={BrokenCode}"));

		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, "PARSER FAILURE")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_IsFormatted()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmt");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/print {obj}=words"));

		await Expect("\n  ");
	}
```

Confirm the expected `"PARSER FAILURE"` fragment against `ErrorMessages.Returns.ParserFailure` — `ParseError.ToMushFailureString()` formats through it, and the exact wording is defined there, not here.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/GrepSyntaxFormattingTests/*"`
Expected: PASS (3 tests).

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Implementation/Commands/GeneralCommands.cs \
        SharpMUSH.Library/Services/AttributeService.cs \
        SharpMUSH.Tests/Commands/GrepSyntaxFormattingTests.cs
git commit -m "Format flagged attributes in @grep/PRINT and warn on setting broken code"
```

---

### Task 8: Give the Softcode Editor the same layout engine

**Files:**
- Modify: `SharpMUSH.CodeAnalysis/IMushCodeAnalyzer.cs`
- Modify: `SharpMUSH.CodeAnalysis/MushCodeAnalyzer.cs:17-49`
- Modify: `SharpMUSH.LanguageServer/Handlers/DocumentFormattingHandler.cs:41-62`
- Test: `SharpMUSH.Tests/CodeAnalysis/MushCodeAnalyzerIndentTests.cs`

**Interfaces:**
- Consumes: `SoftcodeLayout.Compute` (Task 2).
- Produces: `IMushCodeAnalyzer.FormatIndented(string code, int width = 78) → string`.

Without this task there are two formatters: the good one only `@examine` can reach, and the existing line-preserving one the LSP, the MCP `format` tool, and the Softcode Editor are stuck with. `SharpMUSH.CodeAnalysis` already references `SharpMUSH.Library`, so it can consume the layout engine directly.

`Format` keeps its current behaviour and contract. The indenting behaviour is a **new, separate method**, because `Format`'s callers depend on line count being preserved.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUSH.CodeAnalysis;

namespace SharpMUSH.Tests.CodeAnalysis;

public class MushCodeAnalyzerIndentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMushCodeAnalyzer Analyzer => new MushCodeAnalyzer(WebAppFactoryArg.FunctionParser);

	[Test]
	public async Task FormatIndented_BreaksLongCalls()
	{
		var result = Analyzer.FormatIndented(
			"switch(words(%0),0,nothing at all,1,just one,many words here)", width: 30);

		await Assert.That(result).Contains("\n");
		await Assert.That(result).DoesNotContain("\n)");
	}

	[Test]
	public async Task FormatIndented_PreservesNonWhitespaceCharacters()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many words here)";
		var result = Analyzer.FormatIndented(src, width: 30);

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
		await Assert.That(Strip(result)).IsEqualTo(Strip(src));
	}

	[Test]
	public async Task Format_StillPreservesLineCount()
	{
		const string src = "add(1,2)\nsub(3,4)\nmul(5,6)";
		await Assert.That(Analyzer.Format(src).Split('\n')).HasCount().EqualTo(3);
	}
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MushCodeAnalyzerIndentTests/*"`
Expected: compile failure — `FormatIndented` is not defined.

- [ ] **Step 3: Implement**

Add `FormatIndented` to `IMushCodeAnalyzer` and implement it in `MushCodeAnalyzer` by calling `parser.Tokenize` then `SoftcodeLayout.Compute`, applying the breaks to plain text (no markup — this returns `string`). Leave `Format`, `Validate`, `Hover`, `Complete`, `SignatureHelp`, and `DocumentSymbols` untouched.

- [ ] **Step 4: Fix the LSP formatting handler**

`DocumentFormattingHandler.cs:41-62` does `Format(document.Text).Split('\n')` and emits one `TextEdit` per line, which structurally requires the line count to be preserved. Switch it to `FormatIndented` and emit a **single whole-document `TextEdit`** spanning the full range, so the editor gets real indentation.

Leave the MCP `format` tool (`SharpMUSH.Server/Mcp/MushTools.cs:48`) on `Format` — agents consuming it expect line-stable output.

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MushCodeAnalyzerIndentTests/*"`
Expected: PASS (3 tests).

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/*Analyzer*/*"` and `--treenode-filter "/*/*/*Formatting*/*"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.CodeAnalysis/ SharpMUSH.LanguageServer/Handlers/DocumentFormattingHandler.cs \
        SharpMUSH.Tests/CodeAnalysis/MushCodeAnalyzerIndentTests.cs
git commit -m "Back the LSP formatter with the shared softcode layout engine"
```

---

### Task 9: Unset prefix matching, help files, full suite

**Files:**
- Modify: `SharpMUSH.Library/Services/AttributeService.cs:667-701`
- Modify: help files (locate with `grep -ril "no_command" --include="*.md" --include="*.txt" .`)
- Test: `SharpMUSH.Tests/Database/AttributeSyntaxFlagTests.cs` (extend)

- [ ] **Step 1: Fix the unset asymmetry**

`SetAttributeFlagAsync` (`:640-643`) falls back to shortest-prefix matching; `UnsetAttributeFlagAsync` (`:667-701`) does exact-match only, so `@set obj/attr=wiz` works but `@set obj/attr=!wiz` fails. Copy the same fallback block into the unset path.

Add a test asserting `!wiz` unsets `wizard`, and that `!x` unsets `cmdsyntax`.

- [ ] **Step 2: Document both flags**

Add `cmdsyntax` and `funsyntax` to the attribute-flag help alongside the existing entries. State plainly that they affect **display only**, that `cmdsyntax` is unrelated to `no_command`, and that a `@switch` with braced branches will not have those branches broken open in v1.

- [ ] **Step 3: Run the full suite**

Run: `dotnet run --project SharpMUSH.Tests`
Expected: PASS. Investigate any failure before proceeding — do not assume it is pre-existing without checking against `origin/main`.

- [ ] **Step 4: Verify formatting and build clean**

```bash
dotnet format whitespace --folder SharpMUSH.Library --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Implementation --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" --exclude "**/obj/**"
```

Run each until it reports no changes — the formatter needs two passes. Stage only the paths this plan touched; never `git add -A`.

Run: `dotnet build`
Expected: clean, no `FORMAT001`.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Library/Services/AttributeService.cs \
        SharpMUSH.Tests/Database/AttributeSyntaxFlagTests.cs
git commit -m "Allow prefix matching when unsetting attribute flags, and document the syntax flags"
```

---

## Deferred

Recorded so they are not silently dropped:

- **Breaking inside `{}` groups.** Gated on extending the Task 3 corpus to prove which brace contexts re-parse as code. Until then a `@switch` with braced branches gets one line per branch.
- **Breaking after `=` and `{`.** Safe per the lexer, but rarely reads well.
- **Parse tree caching.** Every display of a flagged attribute is a full LL parse; `GetPredictionMode` forces LL for tooling paths. Acceptable for an interactive command. Revisit if `@examine` on an object with many flagged attributes drags.
- **Dead flag extensions.** `SharpAttributeExtensions` declares `IsInternal`, `IsNoprog`, `IsPrivate`, `IsListen`, `IsNoDump`, `IsMortalHear`, and `IsActionHear` against flag names present in no provider seed. Either the flags are missing or the extensions are dead; resolving it needs a PennMUSH parity audit of the attribute flag table.
- **`MushcodeHighlighter` is a second classifier.** It re-derives token categories with its own regexes and emits HTML spans for the package review UI (`PackagesController.cs:304-307`). The ANTLR classifier is strictly more accurate, so this is real duplication — but retargeting it means a `SemanticTokenType` → CSS class map to sit beside `SemanticTokenAnsiPalette`, new class names in the package-review markup, and matching client CSS. Its dangerous-pattern scanner (`FindDangerousPatterns`) is orthogonal and must survive any such change. Out of scope here; worth its own PR.
- **`mush-monaco.js` is a third.** Browser-side Monarch tokenizer, so it cannot call the C# classifier. The Softcode Editor already receives accurate tokens from the LSP `SemanticTokensHandler`; Monarch is the offline fast path. Leave both.
