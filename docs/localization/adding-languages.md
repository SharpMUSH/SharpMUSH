# Adding languages to the portal

The runbook. Follow it top to bottom; the prerequisites are prerequisites because
skipping them means redoing every locale.

Target locales and their rationale are in [`README.md`](README.md).

---

## Step 1 — Switch to full ICU globalization data

**Required the moment you add a third language, and non-negotiable if you want
Russian and Chinese in the same build.**

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

**Blocking.** See [`plural-forms.md`](plural-forms.md) for the full argument, the
24 count-bearing keys, the 13 case-risk keys and the prescribed ICU MessageFormat
change.

`python3 tools/i18n/validate_resx.py` fails until step 2 is complete. That is
deliberate — it is the gate that stops fifteen locales translating a shape that
cannot express their grammar.

## Step 3 — Declare the locales

`Components/LanguagePicker.razor` derives display names from
`CultureInfo.NativeName`, so adding a locale is one line per language in
`SupportedLocales`:

```csharp
private static readonly (string Code, string Flag)[] SupportedLocales =
[
    ("en", "\U0001F1FA\U0001F1F8"),
    ("de", "\U0001F1E9\U0001F1EA"),
    ("es", "\U0001F1EA\U0001F1F8"),
    ("fr", "\U0001F1EB\U0001F1F7"),
    // … one per locale from README.md's table
    ("zh-Hans", "\U0001F1E8\U0001F1F3"),
];
```

Two cautions:

- **Flags are not languages.** A flag for `es` privileges Spain over Latin
  America, and `zh-Hans` has no flag at all — it is a script. The picker already
  shows `CultureInfo.NativeName` beside the flag, so consider dropping the emoji
  rather than picking a country per language. Cosmetic, but it is the kind of thing
  users write in about.
- `Program.cs` already falls back to `en` and clears `localStorage["locale"]` on
  `CultureNotFoundException`, so a bad stored tag self-heals. No change needed.

Also register the locales for the trimmer so satellite assemblies survive
publishing:

```xml
<PropertyGroup>
  <!-- Satellite assemblies for every locale the LanguagePicker offers. Without
       this, PublishTrimmed can drop resources the app only reaches by culture
       lookup at runtime. -->
  <SatelliteResourceLanguages>en;de;es;fr;bg;da;hr;hu;nb;nl;pl;pt-BR;ro;ru;sv;zh-Hans</SatelliteResourceLanguages>
</PropertyGroup>
```

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

1. **`de`** first. EFIGS-covered, well-attested in Penn's data, and the longest
   strings — so it shakes out UI overflow before fourteen other locales are
   sitting on top of the same layout.
2. **`ru`** second. First three-category plural locale and first Cyrillic script;
   proves step 2 actually worked.
3. **`zh-Hans`** third. First CJK; proves step 1, and surfaces the word-break and
   font-fallback problems while there is still appetite to fix them.
4. Everything else in any order. By this point the mechanism is proven and the
   remaining work is volume.

Also finish **`fr`**: 264 keys still have no French value, of which 257 are the
legacy MUSH server configuration surfaces.

## Step 5 — Add the guard test per locale

`SharpMUSH.Tests.BUnit/Resources/SharedResourceLocalizationTests.cs` already
proves French resolves to its satellite value. Extend that to every declared
locale, so "we support Russian" is a claim a test can fail:

```csharp
[Test]
[Arguments("de")]
[Arguments("ru")]
[Arguments("zh-Hans")]
// … one per declared locale
public async Task Declared_locales_resolve_their_player_facing_keys(string tag)
{
    var loc = PortalLocalizer.Create();
    var previous = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentUICulture = new CultureInfo(tag);
    try
    {
        var missing = PlayerFacingKeys
            .Where(k => loc[k].ResourceNotFound)
            .ToList();
        await Assert.That(missing).IsEmpty();
    }
    finally
    {
        CultureInfo.CurrentUICulture = previous;
    }
}
```

Only player-facing keys are gated. Staff surfaces are allowed to lag — which is
the honest position, since they are two-thirds of the strings and the least
urgent. Derive `PlayerFacingKeys` from the prefix map in
`tools/i18n/extract_untranslated.py`, or split the resx (see below).

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

## Optional: split the resx by surface

Not required, and less compelling now that an LLM does the drafting rather than a
volunteer facing one 1097-key file. Still worth it for two reasons:

- It makes step 5's `PlayerFacingKeys` a file rather than a prefix heuristic.
- It lets a locale be *completely* translated for players while staff strings lag,
  which is a shippable state rather than an embarrassing one.

Split `SharedResource` (≈400 player-facing keys) from `AdminResource` (≈700 staff
keys), following the existing prefix boundaries. `AddSharedResourceLocalization()`
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

- [ ] `BlazorWebAssemblyLoadAllGlobalizationData` set; stale csproj comment removed
- [ ] `icudt.dat` confirmed in the build output
- [ ] 24 count-bearing values converted to ICU MessageFormat
- [ ] `ResChangeCountOne`/`Many` and `NavCharacterRegistered`/`Characters…` collapsed
- [ ] 13 prepositional-phrase placeholders restructured
- [ ] Plural render test: `RolPoseCount` gives three distinct strings for 1/2/5 under `ru`
- [ ] `SupportedLocales` and `SatelliteResourceLanguages` list every locale
- [ ] `validate_resx.py` reports zero hard failures
- [ ] Per-locale guard test covers every declared locale
- [ ] CI runs the validator
- [ ] `fr` completed (264 keys outstanding)
