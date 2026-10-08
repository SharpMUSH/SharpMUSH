# Portal themes

The portal's look comes from three layers, each laid over the one before:

1. **`tokens.css`**: the default value of every CSS custom property the portal paints with (Phosphor's).
2. **The theme**: thirteen colours and a set of style choices, turned into a `:root` rule by
   `ThemeResolver.Resolve`. Every other colour (status, code, syntax, shadows, gradients) is derived from those
   thirteen, so each stays readable on that theme's surfaces.
3. **The theme's stylesheet**: CSS staff write for anything the colours and choices do not reach. It loads last.

## Choosing a theme (players)

Each character has its own theme and accent, stored with the character, so a player can tell their windows apart
and the choice follows them to every device. To set it, open the account menu (the profile card at the bottom of
the sidebar) and choose **Theme**. The page is `/settings/theme`, also under Settings > Preferences, and each
character on the Account page has a palette button that opens it at that character.

**Game default** follows the browser's light or dark preference. An accent that would be too faint on the chosen
theme is moved toward white or black until it reads at 4.5:1.

## Built-in themes

`BuiltInThemes.All` (`SharpMUSH.Contracts/Models/Portal/PortalThemes.cs`):

- **Phosphor** (dark) and **Daylight** (light), the plain looks.
- One per MSSP genre, with the ids the telnet layout themes use: `fantasy`, `historical`, `horror`, `modern`,
  `mystery`, `romance`, `science-fiction`, `spiritual`.
- Anime: `magical-girl`, `shojo`, `idol`, `slice-of-life`, `shonen`, `sports`, `mecha`, `isekai`, `yokai`.
- Cartoons and comics: `comic-book`, `rubber-hose`, `eighties-cartoon`.
- Neon and space: `cyberpunk`, `synthwave`, `space-opera`, `starship-console`.

The anime, cartoon and space themes are portal themes only; there is no layout theme of the same name. Every theme
after Phosphor and Daylight has a texture, a card frame and a title face no other theme uses
(`ThemeResolverTests.EachGenreHasATextureAndFrameOfItsOwn`).

## Making themes (staff)

`/admin/themes` (Admin > Portal > Themes) needs the `layout.admin` permission. Built-in themes are read-only;
**Duplicate** starts an editable copy. **Try on the portal** paints the whole portal with the unsaved draft.

### Colours

The thirteen colour tokens:

| Token | Used for |
|---|---|
| `bg` | The page |
| `surface` | Cards and panels |
| `surface-2` | Raised rows, the current sidebar row |
| `surface-3` | Sidebars and the top bar |
| `rail-bg` | The icon rail |
| `text`, `text-dim`, `text-faint` | Body, secondary and hint text |
| `border`, `border-soft` | Borders and dividers |
| `accent` | Links, the current item, primary buttons (a character's own accent replaces it) |
| `warn` | Unsaved and changed |
| `link-missing` | Links to wiki pages that do not exist, alerts |

The contrast report lists every pair that must reach 4.5:1 (WCAG AA for text) and flags any that fall short.

### Generating colours

The **Generate colours** panel makes all thirteen from a MarkupString `ThemePalette`, the same palettes the game's
layout themes (`layout_theme`, a layout function's `theme` option) use:

- **From one colour.** Pick a seed, a harmony and a contrast level. This runs
  `ThemePalette.Generate(seed, harmony, mode, contrast)`. The harmony picks the other hues: `monochrome` keeps the
  seed's, `analogous` takes its neighbours, `complementary` its opposite, `split` the two either side of the
  opposite, `triadic` and `tetradic` three or four evenly round the wheel. Contrast (0 to 1) raises every colour from
  the minimum toward 7:1.
- **From a preset.** `ThemePalette.Presets`: `terminal`, the eight genre palettes (`fantasy`, `historical`,
  `horror`, `modern`, `mystery`, `romance`, `science-fiction`, `spiritual`), and base16 schemes (`nord`, `dracula`,
  `gruvbox-dark`, `solarized-dark`, `solarized-light`, `tokyo-night`, `catppuccin-mocha`, `catppuccin-latte`). A
  generated palette (a genre, or anything with a seed) is made again for the theme's light or dark mode. A base16
  scheme is fixed colours and brings its own mode.
- **From JSON.** Anything `ThemePalette.TryParse` reads: a preset's name, or an object with any of `preset`,
  `base16` (sixteen colours), `seed` with `harmony` and `contrast`, `mode`, and `colors` setting roles:

  ```json
  {"seed": "#6b4fa8", "harmony": "triadic", "contrast": 0.3, "mode": "light"}
  {"preset": "nord", "colors": {"primary": "#88c0d0"}}
  ```

`ThemeGenerator.FromPalette` maps the palette's roles onto the tokens: background is the page and surface the cards
(in a light theme the cards take the palette's background and the page sits a step darker), foreground is the text
with the dim and faint shades mixed toward the page, primary is the accent, warning is `warn` and error is
`link-missing`. Every colour that carries text is then moved toward white or black until it reads at 4.5:1 on every
surface, so a generated theme passes the contrast report. Generating replaces only the colours; style choices and
the stylesheet stay.

The same from C#, in a plugin or a tool:

```csharp
using MarkupString;
using SharpMUSH.Library.Models.Portal;

var palette = ThemePalette.Generate(new RgbColor(0x6b, 0x4f, 0xa8), ThemeHarmony.Triadic, ThemeMode.Light, contrast: 0.3);
// or ThemePalette.Preset("nord"), or ThemePalette.TryParse(json, out var palette, out var error)

var (colors, dark) = ThemeGenerator.FromPalette(palette);
// colors: the thirteen colour tokens. POST them to api/themes as the Tokens of a PortalThemeRequest
// (with style choices if you like); the caller needs layout.admin.
```

### Style choices

Typefaces, corners, page texture, title ornament, card frame, title lettering, title effect and picture tone. Each
is one of a fixed list (`ThemeStyles.Choices`) and sets the custom properties in the Decoration, Type and Shape
groups below.

### The stylesheet

The **Stylesheet** box takes CSS for everything else. It loads after the theme's `:root` rule, so it wins over the
colours and choices. A theme without one shows a starter: every variable with this theme's value, commented out, and
an empty rule for each part of the portal listed below. The starter changes nothing until it is edited, and is not
stored if left as it is.

**Where things belong:**

1. **Variables first.** A `:root` rule reaches every page at once and survives portal updates that move classes
   around. Uncomment a line in the starter's `:root` and change its value.
2. **The shell**, then **cards and page headers**, then **content**, then **controls**: rules for the parts in the
   table below, for what a variable cannot say (a gradient behind the rail, a border on one side only).

Colour tokens set in a `:root` rule count as the theme's own: they replace the colour pickers (the editor says
which), the contrast report checks them, and MudBlazor's palette uses them. A character's own accent is set once more
after the stylesheet, so a theme that sets `--accent` still shows each character's.

Component classes are doubled in the starter (`.kit-card.kit-card`). A component's own scoped rule carries an
attribute as well as the class, and a rule here needs the same weight to win; a single class that does nothing is
the sign to double it.

#### Variables

| Group | Properties |
|---|---|
| Surfaces | `--bg`, `--surface`, `--surface-2`, `--surface-3`, `--rail-bg`, `--card-bg` (may be a gradient or layered images), `--code-bg` |
| Text | `--text`, `--text-dim`, `--text-faint`, `--title-color`, `--code-text` |
| Lines | `--border`, `--border-soft` |
| Accent | `--accent`, `--accent-dim`, `--accent-on` (text on an accent fill) |
| Status | `--warn`, `--link-missing`, `--danger`, `--success`, `--info`, `--special` |
| Syntax | `--syntax-command`, `--syntax-function`, `--syntax-substitution`, `--syntax-dbref`, `--syntax-reference`, `--syntax-at-command`, `--syntax-danger` (softcode); `--syntax-string`, `--syntax-link`, `--syntax-heading`, `--syntax-emphasis` (help) |
| Shape | `--radius`, `--radius-lg`, `--radius-card`, `--radius-row`, `--card-border-style`, `--card-border-width`, `--card-shadow`, `--shadow` |
| Type | `--font-ui`, `--font-display`, `--font-mono`, `--font-title-weight`, `--title-transform`, `--title-tracking`, `--title-style` |
| Decoration | `--texture`, `--texture-size`, `--ornament-before`, `--ornament-after`, `--title-underline`, `--title-shadow`, `--image-filter` |

`ThemeStylesheet.Variables` is the list the starter is written from.

#### Parts of the portal

| Section | Selector | Reaches |
|---|---|---|
| Shell | `.phosphor-shell` | The whole page behind everything: background and texture |
| Shell | `.phosphor-rail` | The icon rail on the far left |
| Shell | `.phosphor-sidebar` | The main navigation sidebar |
| Shell | `.phosphor-topbar` | The bar along the top |
| Shell | `.phosphor-pagebar` | A section's own sidebar (Wiki, Settings, Admin...) |
| Shell | `.phosphor-main` | The page area |
| Shell | `.phosphor-bottomnav` | The tabs along the bottom on a phone |
| Shell | `.phosphor-terminal` | The docked terminal |
| Cards | `.kit-card.kit-card`, `.kit-card-head.kit-card-head`, `.kit-card-title.kit-card-title` | Every card, its header row and title |
| Cards | `.kit-link-card.kit-link-card` | The linked cards on overview pages |
| Page headers | `.kit-page-head.kit-page-head`, `.kit-page-kicker.kit-page-kicker`, `.kit-page-title.kit-page-title`, `.kit-page-desc.kit-page-desc` | A page's header, the line above its title, the title, the line under it |
| Content | `.wiki-content`, `.mush-help-md` | Rendered wiki pages and help |
| Controls | `.mud-button-root`, `.mud-input`, `.mud-chip`, `.mud-table` | Buttons, fields, chips, tables (MudBlazor) |

`ThemeStylesheet.Hooks` is the list; a test fails if one stops naming a class the portal renders.

#### What saving refuses

The stylesheet is written into the page for every visitor, so `ThemeStylesheet.Validate` refuses, after reading CSS
escapes (a disguised `\75 rl(` is `url(`):

- `<` anywhere, even in a string (write it as `%3C` inside a `data:` URL);
- `@import`, `@charset`, `@namespace`;
- a `url()` that is not a path on this site (`/uploads/paper.png`), a fragment (`#grain`), or a
  `data:image/png|jpeg|gif|webp|avif|svg+xml` URL: nothing loads from another site, so no visitor's address is
  handed to one;
- `image-set()` and `src()` (use `url()`), `expression()`, `javascript:` and `vbscript:` URLs, `behavior`,
  `-moz-binding`;
- one of the thirteen colour tokens set anywhere but a plain `:root` rule, or to anything but a `#rrggbb` or `#rgb`
  colour: the contrast report could not read it, so the page would wear one colour while the report checked another;
- braces that do not balance, a comment that is not closed, and more than 64 KiB.

## Writing portal CSS

Component CSS never writes a colour of its own: a hex colour or `rgb()` there stays the same on every theme, and
reads as dark text on a light theme's surface or the reverse. Use the variables above. A tint is
`color-mix(in srgb, var(--danger) 12%, transparent)`; a dark wash under text over a picture is
`rgba(var(--scrim-ink), 0.6)`; a raised menu's shadow is `var(--shadow-pop)`. Pure black and white shades for depth
are fine, as is a fallback inside `var()`. `ThemeVariableTests.NoComponentStylesheetWritesAColourOfItsOwn` enforces
this; a new colour belongs in `tokens.css` and, when it must follow the theme, in `ThemeResolver.Derive`.

The Monaco editor paints with its own dark theme whatever the portal's, so its frame reads `--editor-bg`, which no
theme derives.
