# MarkupText library: core + kind packages

**Date:** 2026-09-06
**Status:** approved, in progress

## Problem

`SharpMUSH.MarkupString` has a sound core (immutable text + coalesced attribute runs, a paletted
wire format, no reflection, zero AOT/trim analyzer warnings) wrapped in a layer that is not
library-grade. An audit on 2026-09-06 confirmed, with a probe program and benchmarks:

| # | Defect | Decision |
|---|---|---|
| 1 | xterm-256 and default colours (`ansi(200,x)`, `ansi(d,x)`) render `#000000` in HTML. `AnsiColor.ANSI` stores raw SGR bytes and `AnsiToRgb` only decodes one- and two-byte sequences. | fix |
| 2 | Text not covered by a run vanishes from every render while `ToPlainText` returns it. The constructor accepts any runs; `Concat`/`MarkupSingle2` over a run-less non-empty string drop text or markup silently. | fix (by construction) |
| 3 | Equality ignores markup and `Equals(object)` against `string` is asymmetric. | text-only equality is intended; add format-scoped overload; drop asymmetry |
| 4 | `substring`/`pad` split surrogate pairs; width is UTF-16 units so CJK columns misalign. | grapheme-safe cuts + width-aware pad/align |
| 5 | `compressSpaces` is quadratic (355 ms at 16k spaces). | fix |
| 6 | `HtmlMarkup.Wrap` emits tag and attributes verbatim. | not a risk for the library; unchanged |
| 7 | `Render(string)` maps unknown formats to ANSI; `Render(RenderFormat)` lacks Pueblo/MXP; `ToString()` is format-ambiguous and mixes HTML and ANSI in one string. | fix |
| 8 | Raw ESC sequences passed to `single()` survive into HTML output. | fix |

Allocation, measured: `single()` 120 B; one styled run rendered to ANSI ~3,050 B; ANSI output
9.5 chars per text char for alternating styles; nested styles emit `ESC[4mESC[31mESC[1m`.

Architecture: `IMarkup.WrapAs(string format)` dispatches on format strings inside each markup
while each render strategy dispatches on markup type (the expression problem, solved with
strings). 113 public statics, 49 of them lowercase F#-era aliases. MUSH policy (`#-1`,
`#-1 COLUMN COUNT MISMATCH`, PennMUSH glob, `PE_COMPRESS_SPACES`) lives inside the library.

## Goals

1. Fix defects 1, 2, 4, 5, 7, 8; add the format-scoped equality overload for 3.
2. Replace the string-dispatch expression pattern with explicit, AOT-safe registration.
3. Split into a core package and one package per markup kind, each kind implementing every
   output format it supports.
4. Migrate every consumer to the new API; delete `MModule` and the lowercase aliases.
5. Make the packages NuGet-grade: AOT/trim compatible, public-API tracked, deterministic,
   snapshot- and property-tested, benchmarked.

## Non-goals

- Publishing to nuget.org (workflow exists for plugin packages; wiring these in is a follow-up).
- Changing PennMUSH-visible semantics of `strlen()` and friends: `Length` stays UTF-16 units.
- Sanitising `HtmlMarkup` (defect 6).
- `netstandard2.0` or .NET Framework targets.

## Packages

Three projects, all `net10.0`, `IsAotCompatible`, `TreatWarningsAsErrors`, tabs/indent 2.

| Project | Namespace | Contents |
|---|---|---|
| `MarkupString` (dir `SharpMUSH.MarkupString`, assembly/package id `MarkupString`) | `MarkupString` | `MarkupText`, `Run`, `MarkupSet`, `IMarkup`, `MarkupFormat`, `MarkupRegistry`, `IMarkupEmitter`, `IMarkupCodec`, `MarkupTextSerializer`, `Graphemes`, `DisplayWidth`, all text operations |
| `MarkupString.Ansi` (dir `SharpMUSH.MarkupString.Ansi`) | `MarkupString.Ansi` | `AnsiMarkup`, `AnsiStyle`, `AnsiColor`, `AnsiCodeParser`, `AnsiEscapeParser`, `SgrWriter`, `AnsiPalette`, emitters + codec, `AnsiRegistration` |
| `MarkupString.Html` (dir `SharpMUSH.MarkupString.Html`) | `MarkupString.Html` | `HtmlMarkup`, emitters + codec, `HtmlRegistration`. References `MarkupString.Ansi`. |

Client size note: both kinds appear in every game stream, so every consumer references all
three. The split is a dependency boundary and an extension path, not a size reduction. Size
comes from `IsTrimmable` and from dropping the `TrimmerRootAssembly` root in
`SharpMUSH.Client.csproj` (plugin WASM components that need markup must reference the
packages directly; the root is replaced by `TrimmerRootAssembly` on the three packages only
if the plugin-component test proves it necessary).

`SharpMUSH.MarkupString.csproj` today has `AssemblyName` = `SharpMUSH.MarkupString` and root
namespace `MarkupString`. The new core project keeps the directory name
`SharpMUSH.MarkupString` (so existing `ProjectReference` paths stay valid) but sets
`<AssemblyName>MarkupString</AssemblyName>` and `<PackageId>MarkupString</PackageId>`. The two
kind projects are new directories with `ProjectReference`s added to every consumer that
references the core today (15 csproj files, listed in the plan).

## Core data model

```csharp
public sealed class MarkupText : IEquatable<MarkupText>
{
	public string Text { get; }                 // plain text, never null
	public ImmutableArray<Run> Runs { get; }    // styled spans only, sorted, non-overlapping,
	                                            // non-adjacent-with-equal-set (coalesced),
	                                            // every run has Length > 0 and a non-empty set
	public int Length => Text.Length;           // UTF-16 code units
}

public readonly record struct Run(int Start, int Length, MarkupSet Markups)
{
	public int End => Start + Length;
}

/// Immutable ordered list of IMarkup, outermost first. Value-equal. Interned via
/// MarkupSet.Of(...) so equal sets share one instance (weak intern table, bounded).
public sealed class MarkupSet : IEquatable<MarkupSet>, IReadOnlyList<IMarkup>
{
	public static MarkupSet Of(IMarkup markup);
	public static MarkupSet Of(ReadOnlySpan<IMarkup> markups);
	public MarkupSet Append(IMarkup outer);   // wrap: the new markup becomes outermost
}

public interface IMarkup
{
	// Value equality is required (records / record structs). No render methods here.
}
```

Gaps between runs are plain text. `MarkupText.Plain(string)` allocates one object and no run
array. The constructor is internal; every public factory normalises (drops empty runs,
coalesces, sorts). Zero-length "marker" runs (today's `MarkupSingle2(markup, empty)`) are not
representable; `Wrap(markup)` over an empty text returns empty.

Interning: `MarkupSet.Of` looks up a `ConditionalWeakTable`-free, lock-free
`ConcurrentDictionary<MarkupSet, MarkupSet>` capped at 4,096 entries (cleared when full).
Interning is an optimisation only; equality never depends on reference identity.

### Operations (instance methods, PascalCase, null-hostile)

Factories: `Plain(string)`, `Empty`, `Space`, `NewLine`, `Wrap(IMarkup, string)`,
`Wrap(IMarkup, MarkupText)`, `Wrap(MarkupSet, string)`, `Concat(MarkupText, MarkupText)`,
`Concat(ReadOnlySpan<MarkupText>)`, `Concat(IEnumerable<MarkupText>)`,
`Join(MarkupText separator, IEnumerable<MarkupText>)`, `Join(Func<int, MarkupText>, …)`.
Parsing is not a core concern; the kind packages parse.

Instance: `Substring(int start, int length)`, `Substring(int start)`, `Split(string)`,
`Split(MarkupText)`, `SplitList(MarkupText)` (MUSH space-list semantics move out: see below),
`Trim(TrimType, string chars = " ")`, `Trim(TrimType, MarkupText)`, `Pad(MarkupText fill, int width,
PadType, TruncationType)`, `Center(fillLeft, fillRight, width, trunc)`, `Repeat(int)`,
`Remove(int, int)`, `Replace(int index, int length, MarkupText)`, `Insert(int, MarkupText)`
(inherits the enclosing run's markup as today), `Splice(ReadOnlySpan<(int Start, int Length,
MarkupText Replacement)> edits)` (single pass, edits sorted and non-overlapping; the primitive
behind `ReplaceAll` and `compressSpaces`), `ReplaceAll(string search, MarkupText replacement)`,
`IndexOf(string)`, `LastIndexOf(string)`, `IndexesOf(string)`, `Apply(Func<string,string>)`
(same-length transforms keep runs, else the result is plain), `Map(Func<MarkupText,MarkupText>)`
(per-run, today's `Apply2`), `AttachTail(MarkupText)` (today's `concatAttach`),
`ToPlainText()`, `ToString()` = `ToPlainText()`, `DisplayWidth`.
`Split("")` returns the text as a single segment (an empty delimiter matches nothing); splitting
into characters is the caller's job.

Unicode: `Substring`, `Split`, `Trim`, `Pad`, `Remove`, `Splice` never cut inside a grapheme
cluster. Which way a cut index moves depends on what the operation is: **extractions**
(`Substring`, `Split`, `Trim`, `Pad`/`Center` truncation) snap **inward** — the start back to
its cluster start, and the end, measured from that snapped start, back as well — so a result
never exceeds the requested length and may be shorter (empty, when the window sits inside one
cluster); **edits** (`Splice`, `Remove`, `Replace`, `Insert`) snap **outward** — start back,
end forward — so a replaced range never leaves half a cluster behind. `Graphemes` wraps `StringInfo.GetNextTextElementLength` and is only consulted
when the code unit at the cut is a surrogate, a combining mark, ZWJ, or variation selector
(`Rune.GetUnicodeCategory` check first, so ASCII pays one branch). `DisplayWidth` uses a
generated East Asian Width range table (Unicode 16, `W` and `F` = 2; `Mn`, `Me`, `Cf`
(ZWJ, ZWNJ, VS) = 0; control = 0; everything else 1). `Pad`/`Center` compute padding from
`DisplayWidth`, truncate by display width, and never split a cluster.

### Equality

- `Equals(MarkupText)`, `==`, `GetHashCode`: ordinal comparison of `Text` only.
- `TextEquals(string)`: ordinal comparison of `Text` with a string. No `Equals(object)` against
  `string`.
- `Equals(MarkupText other, MarkupFormat format, MarkupRegistry? registry = null)`: true when
  both render identically in `format`. `MarkupFormat.Plain` is equivalent to `Equals(other)`.

## Formats, emitters, registry

```csharp
public sealed class MarkupFormat : IEquatable<MarkupFormat>
{
	public string Name { get; }
	public TextEncoding Encoding { get; }        // None | StripControls | Html
	public static MarkupFormat Plain, Ansi, Html, Pueblo, Mxp, BBCode;
	public static MarkupFormat Custom(string name, TextEncoding encoding);
}

public enum TextEncoding { None, StripControls, Html }
// Plain/BBCode: StripControls (C0 except \t \n \r removed). Ansi: None. Html/Pueblo/Mxp: Html
// (WebUtility-equivalent encode, which also drops nothing: ESC is encoded to itself, so it is
// additionally stripped — Html implies StripControls first).

public interface IMarkupEmitter
{
	Type MarkupType { get; }
	MarkupFormat Format { get; }
	/// Called once per run. Write the opening/closing sequences around `body`, which is the
	/// already-encoded text of the run. `context.Previous`/`context.Next` expose neighbouring
	/// runs so stateful emitters (SGR) can diff.
	void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output);
}

public sealed class MarkupRegistry
{
	public static MarkupRegistry Empty { get; }
	public static MarkupRegistry Default { get; set; }   // set once at startup by the host
	public MarkupRegistry With(IMarkupEmitter emitter);
	public MarkupRegistry With(IMarkupCodec codec);
	public MarkupRegistry With(IFormatFramer framer);     // per-format preamble/epilogue
	public IMarkupEmitter? Find(Type markupType, MarkupFormat format);
}
```

Rendering (`MarkupTextRenderer`, core):

1. Walk `Text` from 0 to `Length`, alternating gaps (plain, encoded) and runs.
2. For each run, encode the body, then apply emitters innermost-first for each markup in the
   set: the innermost markup's emitter wraps the body; the next wraps that. An emitter's output
   is written straight into the final buffer, so wrapping is expressed as
   `emitter.Emit(open)` … `body` … `emitter.Emit(close)`. To keep `IMarkupEmitter` a single
   method, `Emit` receives the body and writes open+body+close; nesting is handled by the
   renderer rendering the inner layers into a pooled scratch buffer and passing that as `body`.
   Scratch buffers come from `ArrayPool<char>`.
3. Unregistered `(type, format)` → body is written unchanged.
4. The format's `IFormatFramer` (if registered) writes a preamble before the first run and an
   epilogue after the last (ANSI: trailing `ESC[0m` only if any SGR was emitted; MXP: nothing —
   the connection server keeps its per-line `ESC[0z`).

Public surface: `string MarkupText.Render(MarkupFormat, MarkupRegistry? = null)`,
`void MarkupText.Render(MarkupFormat, IBufferWriter<char>, MarkupRegistry? = null)`.
Absent registry → `MarkupRegistry.Default`.

Nested-emitter contract for SGR: the ANSI emitter for a run with several `AnsiMarkup` layers
gets called once per layer from innermost out. Layer merging (bold inside red inside underline)
must produce one SGR sequence, so core also defines
`IMarkupSetEmitter { MarkupFormat Format; bool TryEmit(MarkupSet set, body, context, output) }`,
registered with `With(IMarkupSetEmitter)` and consulted first by the renderer for every run; if
it returns true the per-markup path is skipped for that run.
The ANSI set-emitter folds every `AnsiMarkup` in the set into one `AnsiStyle`, diffs it against
the style in effect after the previous run, writes one SGR (or a reset + SGR when attributes
must be removed), the body, and nothing after; non-`AnsiMarkup` layers in the set (e.g. an
`HtmlMarkup`) are delegated to their own emitters around the ANSI output. The Html set-emitter
similarly folds ANSI layers into one `<span style="…" class="…">` per run.

## Serializer

Same `{"t","p","r"}` envelope and palette as today. Each palette entry is an array of markup
objects; each markup object carries `"k"` (kind id string, e.g. `"ansi"`, `"html"`, `"neutral"`).
Readers accept a missing `"k"`: `"h"` present → html, `"n"` present → neutral, else ansi.
Codecs:

```csharp
public interface IMarkupCodec
{
	string Kind { get; }
	Type MarkupType { get; }
	void Write(Utf8JsonWriter writer, IMarkup markup);   // properties only; "k" is written by core
	IMarkup Read(JsonElement element);
}
```

`MarkupTextSerializer.Serialize(MarkupText, MarkupRegistry?)` / `Deserialize(string, MarkupRegistry?)`.
Unknown kind on read → the run keeps an `UnknownMarkup(kind, rawJson)` (core type, round-trips
verbatim, emits as plain). Runs are written as styled spans; gaps use palette slot 0 as today,
so existing stored attributes read back unchanged.

ANSI colour wire form changes with the colour model: `"f"`/`"g"` become `"#rrggbb"`,
`"#rrggbbaa"`, an integer 0–255 (xterm/standard index) or `"d"` (default). Legacy byte arrays
are still read: `[30..37]`, `[40..47]`, `[90..97]`, `[100..107]`, `[1,n]`, `[38,5,n]`, `[48,5,n]`,
`[39]`, `[49]`, `[38,2,r,g,b]`, `[48,2,r,g,b]` map onto the new model.

## MarkupString.Ansi

```csharp
public abstract record AnsiColor
{
	public sealed record Default : AnsiColor;                // SGR 39/49
	public sealed record Standard(byte Index, bool Bright) : AnsiColor;  // 0–7, bright => 90+/100+
	public sealed record Xterm(byte Index) : AnsiColor;      // 0–255
	public sealed record Rgb(byte R, byte G, byte B) : AnsiColor;
	public Rgb ToRgb();                                      // palette lookup for non-Rgb
	public static AnsiColor NearestStandard(Rgb), NearestXterm(Rgb);   // redmean distance
}

public readonly record struct AnsiStyle   // today's AnsiStructure, minus raw bytes
{
	AnsiColor? Foreground, Background; bool Bold, Faint, Italic, Underlined, Overlined, Blink,
	Inverted, StrikeThrough, Clear; string? LinkUrl, LinkText; LinkKind LinkKind;
	public AnsiStyle Combine(AnsiStyle inner);   // inner's set fields win (right-biased)
}

public sealed record AnsiMarkup(AnsiStyle Style) : IMarkup;
```

- `AnsiCodeParser.Parse(string codes)` — today's `ParseCodes`, producing the semantic model.
  `#zz` no longer uses exceptions for control flow (span hex parse).
- `AnsiEscapeParser.Parse(string)` — moved from `SharpMUSH.Library/Services/DatabaseConversion`,
  producing `MarkupText`; supports SGR 0–9, 22–29, 30–37, 39, 40–47, 49, 90–97, 100–107,
  38;5;n, 48;5;n, 38;2;r;g;b, 48;2;r;g;b, OSC 8 links; unknown sequences dropped.
- `SgrWriter` — state machine: `Transition(AnsiStyle from, AnsiStyle to, IBufferWriter<char>)`
  emits the minimal SGR: additive attributes and colour changes as one `ESC[a;b;cm`; if any
  attribute must be turned off it emits `ESC[0m` then the full `to` style (turn-off codes 22–29
  are not used, PennMUSH clients handle reset better). Links use OSC 8 (URL kind only).
- Emitters: `AnsiMarkup` → Ansi (SGR), Html (`<span style class>` + `<a>` for links, colours as
  hex via `ToRgb`), Pueblo/Mxp (SGR for colour/attributes; links as `<A XCH_CMD>`/`<SEND>`
  exactly as today's `WrapAsPueblo`/`WrapAsMxp`), BBCode (as today), Plain (nothing).
- Codec `"ansi"`.
- `AnsiRegistration.Register(MarkupRegistry) => MarkupRegistry`.
- Downgrade: `MarkupFormat.Ansi` emits 24-bit as-is (the connection server's output transform
  already downgrades per client capability); `AnsiColor.NearestXterm/NearestStandard` are
  public for that transform to use later.

## MarkupString.Html

`HtmlMarkup(string TagName, string? Attributes) : IMarkup`. Emitters: Html/Pueblo/Mxp → raw
tag as today; Ansi → bold/italic/underline/strike mapped to `AnsiStyle` and emitted through the
ANSI set-emitter (so `<b>` inside red stays red); BBCode/Plain → body. Codec `"html"`.
`HtmlRegistration.Register(MarkupRegistry)`.

`NeutralMarkup` stays in core as `NeutralMarkup.Instance` (`IMarkup` that emits nothing;
codec `"neutral"`), since it is a core no-op rather than a kind.

## MUSH-specific code moves to `SharpMUSH.Library/Markup/` (namespace `SharpMUSH.Library.Markup`)

- `MushText` static: `Error` (`#-1`), `Zero`, `One`, `Comma`, `SplitList` (space-list
  semantics: drops empties for a single-space delimiter), `CompressSpaces` (single pass via
  `Splice`), `Glob.ToRegex`, `IsWildcardMatch`, `GetMatches`, `GetWildcardMatches`,
  `GetRegexpMatches`.
- `ColumnSpec`, `ColumnSpecParser`, `TextAligner` (from `TextAlignerModule`), error strings
  unchanged, width measured with `DisplayWidth`.
- `MarkupStringHandler` (interpolated string handler) and `MarkupTemplateFormatter` are
  updated in place to the new API. Its docs no longer mention F#.

`SharpMUSH.Library` already references the core; it gains references to the two kind packages.
`SharpMUSH.Database*`, `ConnectionServer`, `Client`, `Documentation`, `Contracts`,
`CodeAnalysis`, `LanguageServer`, `Plugins.Scene`, `Server`, `Tests*`, `Benchmarks`, the test
fixture plugins and `examples/plugins/hello-ui` reference all three.

Registry bootstrap: `MarkupRegistry.Default` is assigned in each host's startup
(`SharpMUSH.Server`, `ConnectionServer`, `Client` `Program.cs`, the Tests infrastructure
`[Before(TestSession)]`, the Benchmarks `GlobalSetup`, the LanguageServer) as
`MarkupRegistry.Empty.WithAnsi().WithHtml()` (extension methods from the kind packages). The
core throws `InvalidOperationException("MarkupRegistry.Default has not been configured")` on
first render/deserialize without one, rather than rendering silently as plain.

## Migration

- `MModule` and every lowercase alias are deleted. `MString` stays as
  `global using MString = global::MarkupString.MarkupText;` in every project's `GlobalUsings.cs`
  (it names the type in ~1,000 signatures; keeping it avoids noise).
- Mechanical map (scripted with `sed`, then compile):

| old | new |
|---|---|
| `MModule.single(x)` / `Single(x)` | `MarkupText.Plain(x)` (nullable arg sites: `x ?? ""` or explicit branch) |
| `MModule.empty()` / `Empty()` | `MarkupText.Empty` |
| `MModule.Space()` | `MarkupText.Space` |
| `MModule.plainText(x)` | `x.ToPlainText()` (nullable: `x?.ToPlainText() ?? ""`) |
| `MModule.getLength(x)` | `x.Length` |
| `MModule.concat(a,b)` | `MarkupText.Concat(a, b)` |
| `MModule.multiple(xs)` / `ConcatMany` | `MarkupText.Concat(xs)` |
| `MModule.multipleWithDelimiter(d, xs)` | `MarkupText.Join(d, xs)` |
| `MModule.substring(s,l,x)` | `x.Substring(s, l)` |
| `MModule.split(d,x)` / `split2` | `x.Split(d)` |
| `MModule.splitList(d,x)` | `MushText.SplitList(d, x)` |
| `MModule.trim(x,c,t)` / `trim2` | `x.Trim(t, c)` |
| `MModule.pad(...)` | `x.Pad(...)` |
| `MModule.MarkupSingle(m,s)` | `MarkupText.Wrap(m, s)` |
| `MModule.MarkupSingle2(m,x)` | `MarkupText.Wrap(m, x)` |
| `MModule.MarkupSingleMulti(set,s)` | `MarkupText.Wrap(MarkupSet.Of(set), s)` |
| `MModule.MarkupMultiple(m,xs)` | `MarkupText.Wrap(m, MarkupText.Concat(xs))` |
| `MModule.serialize(x)` | `MarkupTextSerializer.Serialize(x)` |
| `MModule.deserialize(s)` | `MarkupTextSerializer.Deserialize(s)` |
| `MModule.render("html", x)` / `x.Render("html")` | `x.Render(MarkupFormat.Html)` |
| `x.ToString()` (rendering intent) | `x.Render(MarkupFormat.Ansi)` or `ToPlainText()` per site |
| `MModule.compressSpaces(x)` | `MushText.CompressSpaces(x)` |
| `MModule.isWildcardMatch*`, `getWildcardMatch*`, `getMatches` | `MushText.*` |
| `MModule.apply(x,f)` / `apply2` | `x.Apply(f)` / `x.Map(f)` |
| `MModule.concatAttach(a,b)` | `a.AttachTail(b)` |
| `MModule.repeat(x,n)` / `remove` / `replace` / `insertAt` / `indexOf*` / `center2` | instance methods |
| `TextAlignerModule.align(...)` | `TextAligner.Align(...)` |
| `AnsiCodeParser.ParseCodes` | `AnsiCodeParser.Parse` |
| `AnsiMarkup.Create(...)` | `new AnsiMarkup(new AnsiStyle { … })` or `AnsiMarkup.Create(...)` kept as a factory with the same named parameters, colours now `AnsiColor` |
| `AnsiColor.NoAnsi.Instance` | `null` (colour absent) |
| `new AnsiColor.ANSI([31])` | `new AnsiColor.Standard(1, false)` |
| `new AnsiColor.RGB(Color)` | `new AnsiColor.Rgb(r, g, b)` |

- `ToString()` audit: the 27 `Message!.ToString()` sites in Implementation feed JSON or string
  parsing and become `ToPlainText()`. Every other `.ToString()` on a `MString` is inspected; sites
  that produce player-visible output render explicitly.
- `AnsiEscapeParser` in `SharpMUSH.Library` becomes a forwarder-free move: the PennMUSH
  converter calls `MarkupString.Ansi.AnsiEscapeParser.Parse`.
- The `RenderFormat` record hierarchy and `IRenderStrategy` are deleted; `Render(string)` is
  deleted.

## Testing

- Each defect gets a test that fails on the old code (ported first, in the new test files):
  xterm/default colour to HTML hex; a gap-free-by-construction test (`Concat(Plain("ab"),
  Wrap(bold,"c"))` renders "ab" in every format); grapheme-safe substring on `a😀b` and on
  `é`; display-width padding of `日本`; `CompressSpaces` on 64k spaces under 20 ms;
  `Render(MarkupFormat.Custom("bogus", None))` renders plain rather than ANSI; `ToString()` is
  plain; ESC in `Plain()` does not reach HTML output.
- Existing test files under `SharpMUSH.Tests/Markup` are migrated, not deleted; expectations
  that encoded the old ANSI byte stream (per-run reset, one SGR per attribute) are updated to
  the diffed stream and the change is noted in the test.
- Snapshot tests (Verify.TUnit) for each format over a fixture set of ~20 strings.
- Property test (CsCheck): `Deserialize(Serialize(x))` renders identically in every format for
  random texts and random run layouts; `Substring` never yields a lone surrogate.
- Benchmarks: `MStringBenchmarks` migrated and extended with `Render(Ansi)`, `Render(Html)`,
  `Serialize`, `Deserialize`, `Plain` allocation cases. Targets: `Plain` ≤ 40 B, one-run ANSI
  render ≤ 400 B.
- AOT: `SharpMUSH.MarkupString.AotSmoke` console project (`PublishAot=true`,
  `TrimmerRootAssembly` on the three packages) published in the `format`/build CI job for
  linux-x64; any IL warning fails.

## Packaging

In each package csproj: `IsAotCompatible`, `GenerateDocumentationFile`, `PackageReadmeFile`
(`README.md` per package), `PackageLicenseExpression` (repo licence), `EnablePackageValidation`
(baseline added on first release), `Deterministic`, `ContinuousIntegrationBuild` in CI,
`EmbedUntrackedSources`, `IncludeSymbols` + `snupkg`, `MinVer` (tag prefix `markupstring/v`),
`Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` (empty) and
`PublicAPI.Unshipped.txt`.

## Phases

1. Core + kinds + `MushText` behind a temporary `MModule` shim (`SharpMUSH.Library/Markup/MModule.cs`
   forwarding the old names to the new API, marked `[Obsolete]`) so the solution stays green.
2. Consumer migration, one subagent per project group, deleting shim usages; then the shim.
3. Packaging, AOT smoke project, benchmarks, docs (`docs/design/content-rendering-pipeline.md`
   and the CLAUDE.md project map updated; F# references removed).
