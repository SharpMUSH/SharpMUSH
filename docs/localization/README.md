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

- **1123 English keys** in `SharpMUSH.Client/Resources/SharedResource.resx`.
- **German and French are both complete** — 1123/1123 keys each, in
  `SharedResource.de.resx` and `SharedResource.fr.resx`. Both are
  machine-drafted: every value carries an `MT` comment marking that no human has
  read it. `grep -c '<comment>MT' SharpMUSH.Client/Resources/SharedResource.de.resx`
  is the review worklist.
- **`en`, `de` and `fr` are the declared locales** — `PortalLocales.Codes` and
  `SatelliteResourceLanguages` in `SharpMUSH.Client.csproj`, kept in step by
  `PortalSurfacesTests`. The other twelve in the table below are targets, not
  claims: declaring a locale with no resx offers a language that silently
  renders English.
- The client loads **full ICU** (`BlazorWebAssemblyLoadAllGlobalizationData`),
  so `CultureInfo` works for every locale, not only the declared ones.
- `DeclaredLocaleCoverageTests` fails if a declared locale is missing any
  **player-facing** key (267 of the 1123). Staff surfaces may lag.
- Lookup goes through `IStringLocalizer<SharedResource>`, registered by
  `AddSharedResourceLocalization()` in `SharpMUSH.Client/Resources/LocalizationServiceCollectionExtensions.cs`.
  **`ResourcesPath` must stay unset** — setting it double-roots the manifest
  name and makes every lookup silently echo its key. That was a live bug; the
  extension method's XML doc explains it and
  `SharedResourceLocalizationTests` guards it.
- The locale comes from `localStorage["locale"]`, written by
  `Components/LanguagePicker.razor` and read in `Program.cs`, which sets
  `CultureInfo.DefaultThreadCurrentUICulture` and reloads the app.
  Blazor WASM cannot change culture without a reload.
- The picker names languages by `CultureInfo.NativeName` and shows **no flag**.
  A flag is a country and a country is not a language: `es` would have to pick
  Spain over Latin America, `pt-BR`/`pt-PT` are one language and two flags, and
  `zh-Hans` is a script with no flag at all.

## Target locales

Chosen from PennMUSH's own `pennmush/po/*.pox` files — twenty locales that MUSH
communities actually volunteered translations for — ranked by strings genuinely
filled in, plus Russian and Chinese for community size.

| Tag | Language | Penn evidence | ICU shard | Portal status |
|---|---|---|---|---|
| `de` | German | 653 strings | EFIGS | **done — 1123/1123** |
| `es` | Spanish | 855 | EFIGS | |
| `fr` | French | 977 | EFIGS | **done — 1123/1123** |
| `bg` | Bulgarian | 527 | no_CJK | |
| `da` | Danish | 557 | no_CJK | |
| `hr` | Croatian | 1597 — deepest of any locale | no_CJK | |
| `hu` | Hungarian | 1020 | no_CJK | |
| `nb` | Norwegian Bokmål | 1468 (as `no_NO`) | no_CJK | |
| `nl` | Dutch | 1572 | no_CJK | |
| `pl` | Polish | 1313 | no_CJK | |
| `pt-BR` | Portuguese (Brazil) | 261 | no_CJK | |
| `ro` | Romanian | 186 | no_CJK | |
| `ru` | Russian | 52 — barely started | no_CJK | |
| `sv` | Swedish | 960 | no_CJK | |
| `zh-Hans` | Chinese (Simplified) | 68 (as `zh_CN`) | CJK | |

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
evidence of demand. Add it only on request. (It used to be "free under the EFIGS
shard"; the client loads full ICU now, so no locale is cheaper than any other.)

## The one thing that will bite you

Translation volume is not the hard part, especially with an LLM doing the
drafting. The hard parts are two things an LLM cannot fix because they are
limitations of the data model, not of the prose:

1. **Plural categories.** *(Done — the count-bearing values are ICU
   MessageFormat and `validate_resx.py` enforces legal categories per locale.)*
   `.resx` plus `string.Format` can express exactly two forms. Russian, Polish
   and Croatian need three; Chinese needs one, making English's two actively
   wrong. See [`plural-forms.md`](plural-forms.md).
2. **Grammatical case on interpolated nouns.** `"Scene in {0}"` requires the
   room name in the prepositional case in Russian and the locative in Polish.
   No substitution can produce agreement. The strings must be restructured
   before translation, not after.

Both are cheap now (~24 and ~13 keys respectively) and expensive once fifteen
locales have translated the broken shapes.
