# Portal localization

How to add languages to the Blazor portal (`SharpMUSH.Client`). For the server
engine's separate localization layer — telnet/WebSocket messages via
`ILocalizationService` and `Notifications.resx` — see [`../../Localization.md`](../../Localization.md).

| Document | Read it when |
|---|---|
| [`adding-languages.md`](adding-languages.md) | Adding one or more locales. The runbook; start here. |
| [`plural-forms.md`](plural-forms.md) | **Before** adding any locale with more than two plural categories. Blocking prerequisite. |
| [`ai-translation.md`](ai-translation.md) | Producing the translations with an LLM rather than human translators. |

## Current state

- English keys live in `SharpMUSH.Client/Resources/SharedResource.resx`. Every
  player-facing key is translated in all fifteen locales; staff keys are partly
  translated. `python3 tools/i18n/extract_untranslated.py <locale> --stats`
  prints the counts per surface.
- Every translation is machine-drafted: each value carries an `MT` comment
  marking that no human has read it. `grep -c '<comment>MT' SharpMUSH.Client/Resources/SharedResource.de.resx`
  is the review worklist for a given locale.
- **All sixteen locales are declared** — `PortalLocales.Codes` and
  `SatelliteResourceLanguages` in `SharpMUSH.Client.csproj`, kept in step by
  `PortalSurfacesTests`. Declaring a locale with no resx would offer a language
  that silently renders English, which is what that test exists to prevent.
- The client loads **full ICU** (`BlazorWebAssemblyLoadAllGlobalizationData`),
  so `CultureInfo` works for every locale, not only the declared ones.
- `DeclaredLocaleCoverageTests` fails if a declared locale is missing any
  **player-facing** key. Staff surfaces may lag.
- MudBlazor's own text (pagers, pickers, screen-reader labels, converter errors)
  is in the same resx under MudBlazor's key names, served by
  `PortalMudLocalizationInterceptor`. `MudBlazorTextTests` fails if a MudBlazor
  upgrade ships a key the resx lacks.
- Dates and times shown to readers go through `DateDisplay`
  (`SharpMUSH.Client/Components/Kit`), which writes them in the reader's
  culture. Technical timestamps (backups, package revisions, UTC lines) keep
  fixed ISO patterns.
- Lookup goes through `IStringLocalizer<SharedResource>`, registered by
  `AddSharedResourceLocalization()` in `SharpMUSH.Client/Resources/LocalizationServiceCollectionExtensions.cs`.
  **`ResourcesPath` must stay unset** — setting it double-roots the manifest
  name and makes every lookup silently echo its key. That was a live bug; the
  extension method's XML doc explains it and
  `SharedResourceLocalizationTests` guards it.
- The locale comes from `localStorage["locale"]`, read in `Program.cs`, which
  sets `CultureInfo.DefaultThreadCurrentUICulture`. Blazor WASM cannot change
  culture without a reload, so every switch stores the locale and reloads.
- The portal and the game share one language per character: the character's
  `LOCALE` attribute, the one `@locale` sets. `Services/CharacterLanguage.cs`
  runs `@locale` for the acting character when `Components/LanguagePicker.razor`
  switches, and switches the tab when the roster's `Locale` for the acting
  character is a language the portal speaks (`PortalLocales.Match`: `de-DE` is
  served by `de`). With no character the choice stays in the browser.
- The picker names languages by `CultureInfo.NativeName` and shows **no flag**.
  A flag is a country and a country is not a language: `es` would have to pick
  Spain over Latin America, `pt-BR`/`pt-PT` are one language and two flags, and
  `zh-Hans` is a script with no flag at all.

## Target locales

The fifteen below were chosen from the twenty locales in PennMUSH's own
`pennmush/po/*.pox` files — the ones MUSH communities actually volunteered
translations for — ranked by strings genuinely filled in, plus Russian and
Chinese for community size.

| Tag | Language | Penn evidence | ICU shard | Portal status |
|---|---|---|---|---|
| `de` | German | 653 strings | EFIGS | player-facing done |
| `es` | Spanish | 855 | EFIGS | player-facing done |
| `fr` | French | 977 | EFIGS | player-facing done |
| `bg` | Bulgarian | 527 | no_CJK | player-facing done |
| `da` | Danish | 557 | no_CJK | player-facing done |
| `hr` | Croatian | 1597 — deepest of any locale | no_CJK | player-facing done |
| `hu` | Hungarian | 1020 | no_CJK | player-facing done |
| `nb` | Norwegian Bokmål | 1468 (as `no_NO`) | no_CJK | player-facing done |
| `nl` | Dutch | 1572 | no_CJK | player-facing done |
| `pl` | Polish | 1313 | no_CJK | player-facing done |
| `pt-BR` | Portuguese (Brazil) | 261 | no_CJK | player-facing done |
| `ro` | Romanian | 186 | no_CJK | player-facing done |
| `ru` | Russian | 52 — barely started | no_CJK | player-facing done |
| `sv` | Swedish | 960 | no_CJK | player-facing done |
| `zh-Hans` | Chinese (Simplified) | 68 (as `zh_CN`) | CJK | player-facing done |

Fifteen locales. Two tag choices differ deliberately from Penn's filenames:

- **`nb`, not `no`.** `no` is a macrolanguage; `nb` (Bokmål) is what Penn's
  `no_NO` actually contained and what .NET resolves cleanly.
- **`zh-Hans`, not `zh-CN`.** The distinction that matters for Chinese is
  script, not region. Penn's `zh_TW` was an empty stub, so Traditional is
  omitted; add `zh-Hant` later if someone asks.

Deliberately omitted despite appearing in Penn's `po/`: `eo` (3 strings),
`zh_TW` (0), `pt_PT` (0), `fi_FI` (0), `id_ID` (56 but no active community).
Four of those five are empty stub files — a locale created before anyone
committed to it. Don't repeat that.

Also note `it` (Italian) has **no** PennMUSH translation at all, which is weak
evidence of demand. Add it only on request. The client loads full ICU, so no locale is cheaper
than any other.

## The one thing that will bite you

Translation volume is not the hard part, especially with an LLM doing the
drafting. The hard parts are two things an LLM cannot fix because they are
limitations of the data model, not of the prose:

1. **Plural categories.** *(Done — the count-bearing values are ICU
   MessageFormat and `validate_resx.py` enforces legal categories per locale.)*
   `.resx` plus `string.Format` can express exactly two forms. Croatian and
   Romanian need three, Russian three plus a fourth for fractions, Polish four;
   Chinese needs one, making English's two actively wrong. See [`plural-forms.md`](plural-forms.md).
2. **Grammatical case on interpolated nouns.** `"Scene in {0}"` requires the
   room name in the prepositional case in Russian and the locative in Polish.
   No substitution can produce agreement. The strings must be restructured
   before translation, not after.

Both are cheap now (~24 and ~13 keys respectively) and expensive once fifteen
locales have translated the broken shapes.
