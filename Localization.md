# Localization

SharpMUSH has two independent localization layers that share the same design
principles but target different runtimes.

| Layer | Scope | Technology | Resource location |
|-------|-------|------------|-------------------|
| **Server engine** | Messages sent to telnet / WebSocket game connections | `ILocalizationService` + `NotifyLocalized()` | `SharpMUSH.Library/Resources/Notifications.resx` |
| **Blazor admin UI** | Browser-rendered admin panel | `IStringLocalizer<SharedResource>` | `SharpMUSH.Client/Resources/SharedResource.resx` |

Both layers use standard `.resx` resource files. The .NET SDK compiles them into
satellite assemblies automatically — no manual `<EmbeddedResource>` entries are
needed in `.csproj` files.

---

## Server Engine Localization

### Architecture

```
ErrorMessages.Notifications.SomeKey   (compile-time constant, English fallback)
        │
        ▼
Notifications.resx / Notifications.fr.resx   (resource files, keyed by name)
        │
        ▼
ILocalizationService.Get(key, locale)         (plain string lookup)
ILocalizationService.Format(key, locale, args) (formatted string lookup)
        │
        ▼
NotifyService.NotifyLocalized(target, key, args...)
        │
        ▼
Per-connection locale lookup → translated message delivered to each connection
```

Each game connection stores its locale in
`IConnectionService.ConnectionData.Metadata["Locale"]` (e.g. `"en"`, `"fr"`).
When `NotifyLocalized` is called, it iterates every connection belonging to the
target `DBRef`, looks up the locale for each handle, translates via
`ILocalizationService`, and sends the result individually.

Players persist their preference with the `@locale` command, which writes a
`LOCALE` attribute on the player object. The attribute is restored into
connection metadata on login.  Passing `@locale =` (empty right-hand side)
clears the attribute and resets the connection locale to the server default.

### String categories

| Namespace | Purpose | Translate? |
|-----------|---------|------------|
| `ErrorMessages.Returns.*` | Machine-readable MUSH protocol values (`#-1 PERMISSION DENIED`) used as `CallState` returns | **Never** |
| `ErrorMessages.Notifications.*` | User-facing English strings displayed to players | **Yes** |

Format strings use `[StringSyntax(StringSyntaxAttribute.CompositeFormat)]` and
positional placeholders (`{0}`, `{1}`, …).

### Adding a new server notification

1. Add the English constant to
   `SharpMUSH.Library/Definitions/ErrorMessages.cs` under the `Notifications`
   class:

   ```csharp
   public static readonly CompositeFormat MyNewMessage =
       CompositeFormat.Parse("Something happened to {0}");
   ```

2. Add a matching entry to `SharpMUSH.Library/Resources/Notifications.resx`:

   ```xml
   <data name="MyNewMessage" xml:space="preserve">
     <value>Something happened to {0}</value>
   </data>
   ```

3. Send it with `NotifyLocalized`:

   ```csharp
   notifyService.NotifyLocalized(
       target,
       nameof(ErrorMessages.Notifications.MyNewMessage),
       arg0);
   ```

   `nameof()` ensures the resource key stays in sync with the constant name at
   compile time.

4. To translate, add the same key to `Notifications.fr.resx` (or any other
   culture file):

   ```xml
   <data name="MyNewMessage" xml:space="preserve">
     <value>Quelque chose est arrivé à {0}</value>
   </data>
   ```

### NotifyLocalized overloads

```csharp
// Target: all connections for a DBRef (with optional format args)
ValueTask NotifyLocalized(DBRef who, string key, params object[] args);

// Target: all connections for an AnySharpObject (convenience overload)
ValueTask NotifyLocalized(AnySharpObject who, string key, params object[] args);

// Target: a single connection handle
ValueTask NotifyLocalized(long handle, string key, params object[] args);

// Sender-bearing overloads (enable listener routing):
ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args);
ValueTask NotifyLocalized(AnySharpObject who, string key, AnySharpObject? sender, params object[] args);
ValueTask NotifyLocalized(long handle, string key, AnySharpObject? sender, params object[] args);
```

`NotifyLocalized()` has both senderless and sender-bearing overloads.
Use the **sender-bearing overloads** when the notification originates from a
specific in-game object or actor and listener routing should take that sender
into account. Use the **senderless overloads** for true system messages, service
notifications, or any case where there is no meaningful sender and listener
routing should not be attributed to an object.

In short: if sender identity affects who should hear the message or how
listeners evaluate it, pass a sender; if the message is purely system-generated,
use the senderless overloads.

### ILocalizationService interface

```csharp
// Plain string lookup (no format substitution)
string Get(string key, string? locale = null);

// Formatted string lookup (substitutes {0}, {1}, … placeholders)
string Format(string key, string? locale, params object[] args);
```

---

## Blazor Admin UI Localization

### Architecture

```
SharedResource.resx / SharedResource.fr.resx   (resource files)
        │
        ▼
IStringLocalizer<SharedResource>   (standard ASP.NET localization)
        │
        ▼
@Loc["Key"] in .razor files   (renders translated string)
```

Culture is set at startup in `Program.cs` by reading `localStorage["locale"]`
and applying it to `CultureInfo.DefaultThreadCurrentUICulture`. The
`LanguagePicker` component in the AppBar lets users switch languages, which
persists the choice to `localStorage` and reloads the page (Blazor WASM
requires a restart to change culture).

### Setup (already done)

- `Program.cs` calls `builder.Services.AddLocalization(options => options.ResourcesPath = "Resources")`
- `_Imports.razor` includes `@using Microsoft.Extensions.Localization` and `@using SharpMUSH.Client.Resources`
- `Resources/SharedResource.cs` is the marker class that `IStringLocalizer<SharedResource>` resolves against

### Adding a new UI string

1. Add the English key/value to `SharpMUSH.Client/Resources/SharedResource.resx`:

   ```xml
   <data name="MyButtonLabel" xml:space="preserve">
     <value>Click Here</value>
   </data>
   ```

2. Inject the localizer in your `.razor` file:

   ```razor
   @inject IStringLocalizer<SharedResource> Loc
   ```

3. Use it in markup — the pattern depends on context:

   **Inline content** (renders `LocalizedString` directly):
   ```razor
   <MudButton>@Loc["MyButtonLabel"]</MudButton>
   <MudText>@Loc["MyButtonLabel"]</MudText>
   <p>@Loc["MyButtonLabel"]</p>
   ```

   **Component attributes that expect `string`** (`Label=`, `Placeholder=`,
   `Text=`, `HelperText=`, `Title=`) — append `.Value`:
   ```razor
   <MudTextField Label="@Loc["MyButtonLabel"].Value" />
   <MudChip T="string" Text="@Loc["MyButtonLabel"].Value" />
   <MudNavGroup Title="@Loc["MyButtonLabel"].Value" />
   ```

   **Format strings with placeholders**:
   ```razor
   <MudText>@string.Format(Loc["CountMessage"], count)</MudText>
   ```

   **In `@code` blocks** (e.g. Snackbar messages):
   ```csharp
   Snackbar.Add(string.Format(Loc["ItemAdded"], itemName), Severity.Success);
   ```

4. To translate, add the key to `SharedResource.fr.resx`:

   ```xml
   <data name="MyButtonLabel" xml:space="preserve">
     <value>Cliquez ici</value>
   </data>
   ```

---

## Wiki Content Localization

Portal chrome and engine notifications are localized through resx files. Wiki
*content* is not — it lives in the database, so it has its own mechanism.

### Shape

A `WikiPage` keeps identity, metadata and the body in the locale it was authored
in (`SourceLocale`), which is stamped once — at creation for new pages, by
`Migration_AddWikiTranslations` for pages predating the field — and never
re-derived on read, so changing `wiki_default_locale` cannot relabel existing
content. Each translation is an overlay row, `WikiTranslation`, keyed
by `(PageId, Locale)`. A translation owns its `Title`, `MarkdownSource`,
`Published` flag and revision history, and **inherits** `Category`, `Tags` and
`IsProtected` from the source page — structurally, because `WikiTranslation` has
no field for them.

### Reading

`?lang=<tag>` selects a locale; absent it, the portal sends the `locale`
localStorage key the language picker writes. Resolution order:

1. Requested locale, normalised; unparseable becomes `Wiki.DefaultLocale` — a bad
   `?lang=` is never an error, only a write of a bad locale is
2. The page's own stamped source locale, if it is the requested language
3. Exact match against the visible translations
4. Neutral-language match (`fr-CA` finds `fr`, and vice versa)
5. `Wiki.DefaultLocale`, if a translation exists for it
6. The source locale — always available, so a read never fails

When the served language differs from the requested one, the reader gets the
fallback page plus a dismissible notice. Dismissal is per-session on purpose.

### Which locales are allowed?

**Any tag `CultureInfo.GetCultureInfo` accepts** — not only the locales the
portal chrome has a resx for. A game can translate its wiki into Spanish while
the chrome falls back to English. The editor's locale dropdown offers
`PortalLocales.Codes` ∪ existing translations, plus a free-text field.

### Drafts do not leak

`IWikiLocalizationService` filters the candidate translation set by visibility
*before* the resolver sees it, so an unpublished translation is unreachable
rather than merely un-rendered: an ordinary reader falls through to the next
step exactly as if it did not exist. `IWikiLocaleResolver` is permission-blind by
design, which is what keeps the fallback rules unit-testable with no auth graph.

### Adding a translation

- Portal: `/wiki/{ns}/{category}/{slug}/edit?lang=fr`
- API: `PUT /api/wiki/{slug}/translations/{locale}?ns=&category=`, carrying the
  `expectedRevisionNumber` the editor loaded. A concurrent save answers **409**;
  the editor offers a reload and never retries, because retrying would re-apply
  the loser's stale markdown over the winner's.
- In-game: read with `@wiki` in your `@locale`; `@wiki/view/source` reads the
  source. `wiki(<page>, <field>, [<locale>])` takes an explicit locale, and its
  `locale` field returns what was actually served.

Configuration: `wiki_default_locale` (`Wiki.DefaultLocale`, default `en`) in
`/admin/config/wiki`.

### Not localized

- The on-disk helpfiles under `SharpMUSH.Documentation/Helpfiles/` served to the
  telnet `help` command. They never touch `IWikiService`.
- `mush-defs.json`, generated from `[SharpFunction]`/`[SharpCommand]` attributes.
- Category *names*. Category is part of page identity, so a translation cannot
  carry its own; localized category display names are a separate concern.
- Wiki search (`@wiki/search`, `wikisearch()`, the omnisearch box). It matches
  the source `PlainText` only.

---

## Adding a New Language

Three things are needed:

1. **Server side** — create `Notifications.{culture}.resx` in
   `SharpMUSH.Library/Resources/` (e.g. `Notifications.de.resx`).

2. **Blazor side** — create `SharedResource.{culture}.resx` in
   `SharpMUSH.Client/Resources/`.

3. **Language picker** — add the language to the `_languages` array in
   `SharpMUSH.Client/Components/LanguagePicker.razor`:

   ```csharp
   private static readonly LanguageOption[] _languages =
   [
       new("en", "English", "\U0001F1FA\U0001F1F8"),
       new("fr", "Français", "\U0001F1EB\U0001F1F7"),
       new("de", "Deutsch", "\U0001F1E9\U0001F1EA"),   // ← new
   ];
   ```

The SDK auto-generates satellite assemblies at build time. No other
configuration is required.

### Partial translations

You do not need to translate every key. If a key is missing from a
culture-specific `.resx` file, the system falls back to the invariant/English
value from the base `.resx` file. This lets you ship partial translations
incrementally.

---

## How Locale Selection Works at Runtime

### Game connections (telnet / WebSocket)

1. Player runs `@locale fr` in-game.
2. The command writes `fr` to the `LOCALE` attribute on their player object and
   updates `ConnectionData.Metadata["Locale"]` for the active connection.
3. All subsequent `NotifyLocalized` calls read the per-connection metadata and
   deliver translated messages.
4. On reconnect/login, the `LOCALE` attribute is read from the database and
   restored into connection metadata.
5. The `locale()` function lets softcode read the current locale.

### Blazor admin UI

1. User clicks the globe icon in the top-right AppBar.
2. The `LanguagePicker` component writes the selected locale to
   `localStorage["locale"]` and triggers a full page reload.
3. On startup, `Program.cs` reads `localStorage["locale"]` and sets
   `CultureInfo.DefaultThreadCurrentCulture` and
   `CultureInfo.DefaultThreadCurrentUICulture`.
4. `IStringLocalizer<SharedResource>` resolves strings for the active culture.

---

## Conventions

- Resource keys are **PascalCase** (`BannedNameAdded`, `ServerConfiguration`).
- Format-string placeholders are positional: `{0}`, `{1}`, etc.
- XML-special characters in `.resx` values are escaped: `&lt;`, `&gt;`,
  `&amp;`. Newlines use `&#xD;&#xA;`.
- Server-side constants and `.resx` keys must share the same name (enforced by
  `nameof()`).
- Blazor `.razor` keys and `.resx` keys must match exactly (string-based
  lookup).

## File Reference

```
SharpMUSH.Library/
  Definitions/ErrorMessages.cs              — Notifications.* constants
  Resources/Notifications.resx              — English baseline (~350 keys)
  Resources/Notifications.fr.resx           — French proof-of-concept (~20 keys)
  Services/Interfaces/ILocalizationService.cs
  Services/LocalizationService.cs

SharpMUSH.Client/
  Resources/SharedResource.cs               — Marker class
  Resources/SharedResource.resx             — English baseline (~150 keys)
  Resources/SharedResource.fr.resx          — French proof-of-concept (~70 keys)
  Components/LanguagePicker.razor           — Language switcher component
  Program.cs                                — AddLocalization() + culture init
  _Imports.razor                            — Global @using for localization

SharpMUSH.Implementation/
  Commands/MoreCommands.cs                  — @locale command
  Functions/InformationFunctions.cs         — locale() function
  Services/LocalizedTextFileService.cs      — Locale-aware .txt file selection
```

Wiki content localization (the database-backed mechanism, not resx):

```
SharpMUSH.Library/
  Services/WikiLocaleResolver.cs            — The fallback chain (pure)
  Services/WikiLocalizationService.cs       — Visibility filtering; the only
                                              LocalizedWikiPage factory
  Models/Wiki/WikiTranslation.cs            — The overlay row

SharpMUSH.Contracts/
  Services/WikiHelpers.cs                   — NormalizeLocale (write boundary,
                                              returns OneOf) / NormalizeLocaleOrEmpty
                                              (permissive read path) / NeutralLocale /
                                              SameLanguage

SharpMUSH.Database.ArangoDB/
  Migrations/Migration_AddWikiTranslations.cs — SourceLocale / WikiRevision.Locale
                                              backfill and the unique revision
                                              constraint (equivalents live in the
                                              SurrealDB and Memgraph migration
                                              statement lists)

SharpMUSH.Client/
  Resources/PortalLocales.cs                — The portal's locale list, shared by the
                                              language picker and the wiki editor

SharpMUSH.Configuration/
  Options/WikiOptions.cs                    — wiki_default_locale
```
