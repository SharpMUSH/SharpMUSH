# Plural forms

**Blocking prerequisite.** Do this before translating any locale with more than
two plural categories — which is Russian, Polish and Croatian on the target
list. Retrofitting means reopening every count-bearing key in every locale.

## The problem

`.resx` stores one string per key and `string.Format` substitutes positionally.
Neither knows anything about grammatical number. English needs two forms, so the
codebase currently fakes it three different ways:

```
RolPoseCount              "{0} pose(s)"                  ← parenthesised s
NavCharacterRegistered    "{0} character registered"     ← one/many key pair
NavCharactersRegistered   "{0} characters registered"    ┘
ResChangeCountOne         "{0} change"                   ← another key pair
ResChangeCountMany        "{0} changes"                  ┘
```

None survives translation:

| Locale | CLDR plural categories | Forms needed |
|---|---|---|
| `zh-Hans` | `other` | **1** — the `(s)` and both key pairs are noise |
| `de` `es` `fr` `nl` `sv` `da` `nb` `bg` `hu` | `one`, `other` | 2 |
| `pt-BR` | `one`, `many`, `other` | 3 |
| `ro` | `one`, `few`, `other` | 3 |
| `hr` `ru` | `one`, `few`, `other` | 3 |
| `pl` | `one`, `few`, `many`, `other` | **4** |

Russian is the clearest illustration. `поза` (pose) takes three forms driven by
the last digits of the number:

- 1, 21, 31 … → `1 поза` (`one`)
- 2–4, 22–24 … → `2 позы` (`few`)
- 0, 5–20, 25–30 … → `5 поз` (`many`/`other`)

A key pair cannot express that. Neither can `(s)`. And Polish adds a fourth
category on top.

Note also `PkgUninstallConfirmBody`, which carries **two independent counts in
one sentence** (`{0} object(s) … {1} managed attribute record(s)`). Key pairs
cannot represent that at all — you would need one key per combination of
categories, which in Polish is sixteen.

## The fix: ICU MessageFormat for count-bearing keys

Keep `.resx` as the store and `IStringLocalizer` as the lookup. Change only how
count-bearing *values* are written and rendered.

Add a MessageFormat renderer — [`Jeffijoe.MessageFormat`](https://www.nuget.org/packages/Jeffijoe.MessageFormat)
is the maintained .NET implementation of the ICU syntax and understands CLDR
plural rules for all the target locales — and expose it as an extension on the
localizer so call sites stay short:

```csharp
// SharpMUSH.Client/Resources/PluralFormat.cs
public static class PluralFormat
{
    /// <summary>
    /// Renders an ICU MessageFormat resource value against the current UI culture, so a locale
    /// with three or four plural categories gets all of them. Use for every value whose text
    /// changes with a count; use the plain indexer for everything else.
    /// </summary>
    public static string Plural(
        this IStringLocalizer<SharedResource> loc, string key, string argName, object value)
        => Formatter.Format(
               loc[key].Value,
               new Dictionary<string, object?> { [argName] = value },
               CultureInfo.CurrentUICulture.Name);
}
```

The resx value becomes an ICU pattern. English:

```xml
<data name="RolPoseCount" xml:space="preserve">
  <value>{count, plural, one {# pose} other {# poses}}</value>
  <comment>Pose count on a scene card. Named arg: count.</comment>
</data>
```

Russian supplies the categories its grammar needs, with no code change:

```xml
<data name="RolPoseCount" xml:space="preserve">
  <value>{count, plural, one {# поза} few {# позы} other {# поз}}</value>
</data>
```

Chinese supplies one:

```xml
<data name="RolPoseCount" xml:space="preserve">
  <value>{count, plural, other {# 个姿势}}</value>
</data>
```

Call site: `@Loc.Plural("RolPoseCount", "count", poses)`.

**Named arguments, not positional.** ICU MessageFormat uses names, and a name
also tells the translator (or the LLM) what the number *is* — `count` versus
`bytes` versus `revisions`. That context is most of what makes a plural
translation correct.

### Collapse the key pairs

Each pair becomes one key. Delete the second and update its call site:

| Delete | Keep, as MessageFormat |
|---|---|
| `NavCharacterRegistered` | `NavCharactersRegistered` |
| `ResChangeCountOne` | `ResChangeCountMany` |

Prefer renaming the survivor to drop the now-misleading `Many`/`Characters`
plural in the key name itself — e.g. `NavCharacterCount`, `ResChangeCount`.

## Keys to convert

Twenty-four values are genuinely count-bearing. Everything else containing
`{0}` interpolates a name, an error message or a version, and must **not** be
touched.

```
AdmProfilesResetComplete    Reset complete: {0} attributes written.
ConfigSavedCount            {0} setting(s) saved successfully
ErrorCount                  {0} error(s)
Errors                      Errors ({0})
NavCharacterRegistered      {0} character registered          ← collapse pair
NavCharactersRegistered     {0} characters registered         ┘
PkgAttributesCount          Attributes ({0})
PkgConflictCount            {0} conflict(s)
PkgObjectsCount             Objects ({0})
PkgOccurrences              {0} occurrence(s), e.g. in {1}    ← also see case note
PkgUninstallConfirmBody     {0} object(s) … {1} record(s)     ← TWO counts
ProfileUpdated              Profile updated ({0} field(s)).
ResChangeCountMany          {0} changes                       ← collapse pair
ResChangeCountOne           {0} change                        ┘
RolEditedTimes              Edited {0} time(s)
RolPoseCount                {0} pose(s)
SettingsCount               {0} settings
WarningCount                {0} warning(s)
WarningsLabel               Warnings ({0})
WidScenePoseCount           {0} pose(s)
WikiBatchFailed             {0} page(s) failed
WikiBatchSucceeded          {0} page(s) updated
WikiDeleteConfirmText       Delete {0} page(s) and all their revisions? …
WkBytesDelta                {0} bytes
WkHistoryIntro              … {0} revisions in all.
WordsCount                  {0} words
```

Regenerate this list any time with:

```bash
python3 tools/i18n/validate_resx.py --list-count-bearing
```

Purely parenthesised counts (`Errors ({0})`, `Attributes ({0})`) are the
debatable ones: many languages leave a bare parenthesised number alone. Convert
them anyway — a translator who wants agreement can add it, and one who doesn't
writes a single `other` category. Leaving them as bare `{0}` removes the choice.

## Grammatical case on interpolated nouns

Separate problem, same root cause, and it must also be fixed before
translation. Thirteen values put a placeholder inside a prepositional phrase:

```
RolSceneIn          Scene in {0}
WidSceneIn          Scene in {0}
WikiMetadataSaved   Metadata saved for {0}
PkgRequiredBy       required by {0}
PkgAvailableFrom    Available from {0}{1}.
PkgUpgradingFrom    — upgrading from {0}
PkgRenamedFrom      renamed from {0} (keeps {1})
ResWsConnectedTo    [System] Connected to {0}
AuthSetupWelcome    Welcome to {0}
LayConfigJsonHelp   Per-instance configuration for {0}, as JSON. …
PkgOccurrences      {0} occurrence(s), e.g. in {1}
AdmProfilesSubtitle … the in-game {0} object via its {1} softcode …
PkgTrustWarningTagMoved  … the release tag for {0} {1} no longer …
```

`"Scene in {0}"` where `{0}` is a room name needs the noun inflected —
prepositional in Russian, locative in Polish, and in Slavic languages the
preposition itself can change with the noun. Substitution cannot do this, and an
LLM asked to translate the *template* will produce something that reads wrong
for most actual values.

**The rule: a placeholder holding a proper noun belongs at a syntactic boundary,
not inside a prepositional phrase.** Restructure so the noun is apposed rather
than governed:

| Before | After | Why it works |
|---|---|---|
| `Scene in {0}` | `Scene — {0}` | `{0}` is now apposition; no case is demanded |
| `Metadata saved for {0}` | `Metadata saved: {0}` | colon, not preposition |
| `Connected to {0}` | `Connected: {0}` | same |
| `Welcome to {0}` | `{0} — welcome` or keep | game name is often left uninflected anyway; low risk, translator's call |
| `required by {0}` | `Required: {0}` | drops the agent-phrase entirely |

Where the placeholder is a *code identifier* rather than a natural-language
noun — `LayConfigJsonHelp`'s widget name, `AdmProfilesSubtitle`'s attribute
names, `PkgTrustWarningTagMoved`'s package id — leaving the preposition is fine.
Identifiers are not inflected in any target language. Note this in the key's
`<comment>` so the translator knows not to try.

## Acceptance

- `python3 tools/i18n/validate_resx.py` reports no `(s)` or `one`/`many` key
  pairs remaining.
- Every MessageFormat value parses, and every locale's category set is a subset
  of what CLDR allows for that locale. The validator checks both.
- A unit test renders `RolPoseCount` for counts 1, 2 and 5 under `ru` and gets
  three distinct strings. That single test is what proves the whole change
  worked; without it, a three-category resx value silently rendering one form
  looks identical to success.
