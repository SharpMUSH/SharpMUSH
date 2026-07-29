# Adding languages to the portal

The runbook. Follow it top to bottom; the prerequisites are prerequisites because
skipping them means redoing every locale.

Target locales and their rationale are in [`README.md`](README.md).

---

## Step 1 — Switch to full ICU globalization data

**Done.** `SharpMUSH.Client.csproj` sets
`BlazorWebAssemblyLoadAllGlobalizationData`, and `PortalSurfacesTests` fails if
it is ever removed. Kept here because the reasoning is what stops someone
"optimising" it back to a shard.

Blazor WASM does not ship all of CLDR. It ships one of four prebuilt shards, and
you pick exactly one — they do not compose:

| Shard | Uncompressed | Covers |
|---|---|---|
| `icudt_EFIGS.dat` | 608 K | en, fr, it, de, es |
| `icudt_CJK.dat` | 1012 K | zh, ja, ko |
| `icudt_no_CJK.dat` | 1.2 M | everything except CJK |
| `icudt.dat` | 1.6 M | everything |

The target list spans all three partial shards: German/French/Spanish are EFIGS,
eleven locales are `no_CJK`, Chinese is `CJK`. There is no shard that covers them,
so full ICU it is.

```xml
<!-- SharpMUSH.Client/SharpMUSH.Client.csproj -->
<PropertyGroup>
  <!-- Full ICU: the portal's locales span the EFIGS, no_CJK and CJK shards
       (ru/pl/hr are no_CJK, zh-Hans is CJK, de/es/fr are EFIGS) and only one
       shard can be selected, so the partial sets cannot cover them. ~1 MB more
       than EFIGS, fetched once and cached. -->
  <BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>
</PropertyGroup>
```

Delete the existing comment at `SharpMUSH.Client.csproj:14` justifying the shard
on the grounds that "the portal offers en and fr" — it becomes false here, and it
describes an SDK default that no property actually pins.

**Why this matters beyond formatting completeness.** The shard governs
`CultureInfo` behaviour, not translation lookup. Satellite assemblies resolve
regardless. So on an EFIGS build a Russian UI renders fully in Russian *and*
prints `Jul 26, 2026` for dates, with no test failing. `AdminRoles.razor` uses
`ToString("MMM d, yyyy HH:mm")` and is exactly this case.

Verify: `dotnet build SharpMUSH.Client` then confirm `icudt.dat` (not
`icudt_EFIGS.dat`) is what lands in `bin/*/wwwroot/_framework/`.

## Step 2 — Fix plurals and interpolated-noun case

**Done.** The count-bearing values are ICU MessageFormat and `validate_resx.py`
enforces legal plural categories per locale. See [`plural-forms.md`](plural-forms.md) for the full argument, the
24 count-bearing keys, the 13 case-risk keys and the prescribed ICU MessageFormat
change.

`python3 tools/i18n/validate_resx.py` failed until step 2 was complete. That was
deliberate — it is the gate that stops fifteen locales translating a shape that
cannot express their grammar, and it still enforces legal categories per locale
on every new translation.

## Step 3 — Declare the locales

Display names come from `CultureInfo.NativeName`, so declaring a locale is one
entry in `SharpMUSH.Client/Resources/PortalLocales.cs`:

```csharp
public static IReadOnlyList<string> Codes { get; } = ["en", "de", "fr"];
```

…**and** the matching tag in `SatelliteResourceLanguages`
(`SharpMUSH.Client.csproj`), or `PublishTrimmed` can drop resources the app only
reaches by culture lookup at runtime — a failure that appears only in a
published build. `PortalSurfacesTests` fails if the two lists disagree.

Three things to know:

- **Do not declare a locale you have not translated.** A picker entry with no
  resx offers a language and renders English. `DeclaredLocaleCoverageTests`
  fails on it, which is the intended answer.
- **There are no flags.** A flag is a country and a country is not a language:
  `es` would pick Spain over Latin America, `pt-BR`/`pt-PT` are one language and
  two flags, and `zh-Hans` is a script with no flag at all. `PortalLocales.Flag`
  used to exist; it was deleted rather than extended. The native name is what a
  speaker scans for.
- `Program.cs` already falls back to `en` and clears `localStorage["locale"]` on
  `CultureNotFoundException`, so a bad stored tag self-heals. No change needed.

## Step 4 — Translate

See [`ai-translation.md`](ai-translation.md) for the LLM workflow, prompt and
glossary. The loop, per locale:

```bash
python3 tools/i18n/extract_untranslated.py ru --stats
python3 tools/i18n/extract_untranslated.py ru --batch-size 40 --out /tmp/i18n/ru
# … translate …
python3 tools/i18n/merge_translations.py ru /tmp/i18n/ru/*.reply.json
python3 tools/i18n/validate_resx.py --locale ru
```

Sequence the locales by risk, not alphabetically:

1. ~~**`de`** first. EFIGS-covered, well-attested in Penn's data, and the longest
   strings — so it shakes out UI overflow before fourteen other locales are
   sitting on top of the same layout.~~ Done; only three short strings tripped
   the length advisory.
2. **`ru`** next. First three-category plural locale and first Cyrillic script;
   proves step 2 actually worked.
3. **`zh-Hans`** after that. First CJK; proves step 1, and surfaces the word-break and
   font-fallback problems while there is still appetite to fix them.
4. Everything else in any order. By this point the mechanism is proven and the
   remaining work is volume.

**`de` and `fr` are done** — 1123/1123 keys each, machine-drafted, every value
carrying an `MT` comment because no human has read them.

## Step 5 — Add the guard test per locale

**Done, and generalised.** `SharpMUSH.Tests.BUnit/Resources/DeclaredLocaleCoverageTests.cs`
takes its cases from `PortalLocales.Codes`, so a locale added to the picker is
gated automatically — nothing to write per locale.

It gates **player-facing keys only** (267 of 1123). Staff surfaces are two thirds
of the strings and the least urgent, so they are allowed to lag; that is the
honest position, not a concession. The split is derived from the key-prefix map
in `tools/i18n/extract_untranslated.py`, mirrored in `PortalSurfaces` because the
tooling is Python, and `PortalSurfacesTests` parses that Python at test time and
fails if the two drift.

**Do not gate on `IStringLocalizer.ResourceNotFound`.** An earlier draft of this
runbook proposed exactly that, and it does not work: `ResourceManager` falls back
to the neutral resource, so for a key the locale does not have the localizer
returns `ResourceNotFound == false` and the English string. Verified against the
real resx — `loc["ResetToDefault"]` under `fr`, before `fr` was finished, was
`ResourceNotFound=False, Value="Reset to Default"`. The assertion passes for a
locale that renders entirely in English, which is the one case it exists to
catch.

Read the locale's **own** resource set instead:

```csharp
manager.GetResourceSet(new CultureInfo(tag), createIfNotExists: true, tryParents: false)
```

That returns `null` when the locale ships no satellite at all, and `GetString`
returns `null` for a key only the neutral resource has. A companion test then
checks the registered `IStringLocalizer` actually serves that same value under
that culture — the production path, and the one that broke once already via
`ResourcesPath` double-rooting.

Use `CultureScope`, which pins both `CurrentUICulture` and `CurrentCulture`: the
latter matters because a value containing `{0}` renders through `string.Format`
against `CurrentCulture`.

## Step 6 — Wire the validator into CI

```yaml
- name: Validate resource files
  run: python3 tools/i18n/validate_resx.py
```

Hard failures fail the build: an unknown key, a placeholder mismatch, an illegal
plural category for the locale, a plural downgraded to a flat string. Advisories
(identical-to-English, length ratio) print without failing, because both produce
genuine false positives — every French/Spanish cognate trips the first.

Use `--strict` locally when reviewing a locale, never in CI.

---

## Step 7 — If the locale uses CJK script, check the font

Only needed for Chinese, Japanese and Korean; every other locale on the list is
covered by the fonts already shipped.

The portal's monospace stack is one CSS variable, `--font-mono` in
`wwwroot/css/custom.css`, and `custom.css` swaps that whole variable — rather than
appending a fallback — for `:lang(zh)`, `:lang(ja)` and `:lang(ko)`:

```css
:root:lang(zh), :root:lang(ja), :root:lang(ko) {
	--font-mono: "Sarasa Term SC", monospace;
}
```

Swapping rather than falling back is deliberate. Per-glyph fallback would render
Latin from Cascadia Mono and CJK from Sarasa, and Cascadia's advance width is not
half of a CJK glyph's — so a mixed line drifts out of the character grid the
terminal measures and reports to the server over NAWS. Sarasa Term is drawn to
that 1:2 ratio and supplies the Latin too, so the ratio holds across the line.
"Term" and not "Mono", because Term renders East-Asian-ambiguous characters — the
box-drawing runs MUSH tables are framed with — one cell wide, where Mono renders
them two and shears every framed row.

Two things this depends on:

- **`<html lang>` must carry the tag.** `index.html` ships a static `lang="en"`;
  `Program.cs` overwrites it at startup from the resolved culture. Without that the
  `:lang()` rules never match, and a screen reader also announces the page in the
  wrong voice.
- **Nothing may name a font directly.** Every mono surface reads `var(--font-mono)`;
  a hardcoded `font-family:'…',monospace` in an inline style silently opts that
  element out of the switch. That is how 24 of them were opted out before this.

Adding a new CJK script means vendoring the matching Sarasa family into
`wwwroot/fonts/` (SC covers Simplified Chinese; J and K exist for Japanese and
Korean) and adding its `@font-face` plus a `:lang()` arm. The shipped SC subset is
the GB2312 repertoire plus Latin, box drawing and fullwidth forms — about 1 MB per
weight; rarer hanzi fall through to the system CJK font rather than rendering tofu.

## Optional: split the resx by surface

Not required, and less compelling now that an LLM does the drafting rather than a
volunteer facing one 1123-key file. Still worth it for two reasons:

- It makes step 5's `PlayerFacingKeys` a file rather than a prefix heuristic.
- It lets a locale be *completely* translated for players while staff strings lag,
  which is a shippable state rather than an embarrassing one.

Split `SharedResource` (267 player-facing keys) from `AdminResource` (856 staff
and mixed keys), following the existing prefix boundaries. `AddSharedResourceLocalization()`
registers both; `Loc` injections in admin pages switch to
`IStringLocalizer<AdminResource>`. Mechanical, but it touches every admin page, so
it deserves its own PR.

## Optional: pseudolocalization

The highest-value technique that needs no translators. Generate a `qps-ploc`
locale from English that expands each string ~40% and brackets it —
`[Ŝçéñé Årçhîvé——]` — then run the bUnit suite against it. It catches three things
no amount of translation review will:

- strings that never went through `Loc` at all
- truncation and overflow in fixed-width chrome, before German arrives
- accidental concatenation, which pseudo-text makes visually obvious

Generated from the existing resx, so it costs one script and never needs
maintaining. Worth doing before step 4 if you want the German overflow work found
up front rather than reported.

---

## Checklist

- [x] `BlazorWebAssemblyLoadAllGlobalizationData` set; stale csproj comment removed
- [x] `icudt.dat` confirmed in the build output
- [x] Every count-bearing value converted to ICU MessageFormat
      *(`validate_resx.py --list-count-bearing` is the current list; the count is not
      restated here because a hand-kept copy went stale as soon as a key was added)*
- [x] `ResChangeCountOne`/`Many` and `NavCharacterRegistered`/`Characters…` collapsed
- [x] `Joined` restructured to `MemberSince` — a participle cannot agree with a
      character whose gender the directory does not hold
- [x] 13 prepositional-phrase placeholders restructured
- [x] Plural render test: `RolPoseCount` gives three distinct strings for 1/2/5 under `ru`
      *(`TheRussianSatelliteItselfSuppliesThreeForms` — asserts the satellite's own
      categories, not just the mechanism an inline pattern can prove)*
- [x] `PortalLocales.Codes` and `SatelliteResourceLanguages` list every declared locale
- [x] `validate_resx.py` reports zero hard failures
- [x] Per-locale guard test covers every declared locale
- [ ] CI runs the validator
- [x] `fr` completed
- [x] `de` completed
