# Theme Editor

## Overview

Admin-facing theme editor in the admin panel. Maps directly to MudBlazor's
`MudTheme` system. Players choose from admin-defined presets. CSS escape hatch
for edge cases.

## Admin Theme Editing (`/admin/theme`)

### Palette Editor

Visual color pickers mapped to MudBlazor palette properties:

```
Primary         → Palette.Primary (main brand color)
Secondary       → Palette.Secondary (accent color)
Background      → Palette.Background (page background)
Surface         → Palette.Surface (card/panel background)
AppbarBackground→ Palette.AppbarBackground (top bar)
DrawerBackground→ Palette.DrawerBackground (sidebars)
TextPrimary     → Palette.TextPrimary (main text)
TextSecondary   → Palette.TextSecondary (secondary text)
ActionDefault   → Palette.ActionDefault (icon default)
Success         → Palette.Success (success indicators)
Warning         → Palette.Warning (warning indicators)
Error           → Palette.Error (error indicators)
Info            → Palette.Info (info indicators)
```

Each color has:
- Color picker (visual)
- Hex input (direct entry)
- Live preview (right side of editor shows sample components)

### Typography

```
Default Font Family     → Typography.Default.FontFamily
Heading Font Family     → Typography.H1-H6.FontFamily (optional override)
Base Font Size          → Typography.Default.FontSize
Line Height             → Typography.Default.LineHeight
```

Font selection from:
- System fonts (safe defaults: Inter, Roboto, system-ui, etc.)
- Google Fonts subset (curated list of 20-30 good options)
- Custom URL (admin provides font URL — at their own risk)

### Branding

- Site logo: image upload (replaces text in top bar)
- Favicon: image upload
- Header background: image upload (optional, overlays AppbarBackground)

### Dark/Light Variants

Admin can define multiple named themes. Each theme stores both a dark and
light palette (MudBlazor supports `PaletteDark` natively):

```json
{
  "themes": {
    "default": {
      "name": "Default Dark",
      "palette": { ... },
      "paletteDark": { ... },
      "typography": { ... }
    },
    "light": {
      "name": "Clean Light",
      "palette": { ... },
      "paletteDark": null,
      "typography": { ... }
    },
    "highcontrast": {
      "name": "High Contrast",
      "palette": { ... },
      "paletteDark": { ... },
      "typography": { ... }
    }
  },
  "defaultTheme": "default"
}
```

### Live Preview

Right side of the editor shows a miniature portal mockup with sample
components (card, button, nav bar, text block, table). Updates in real-time
as admin adjusts colors. "Apply" saves and broadcasts.

## Player Theme Choice (`/settings/theme`)

Players see a list of admin-defined themes and pick one:

```
┌─────────────────────────────────────────────────────────┐
│  🎨 Appearance                                           │
├─────────────────────────────────────────────────────────┤
│  Choose your theme:                                     │
│                                                         │
│  ● Default Dark    (dark background, blue accent)       │
│  ○ Clean Light     (light background, green accent)     │
│  ○ High Contrast   (black/white, large text)            │
│  ○ Crimson Night   (dark, red accent)                   │
│                                                         │
│  [Preview]  [Apply]                                     │
└─────────────────────────────────────────────────────────┘
```

- Stored in localStorage (`theme_preference: "default"`)
- Applied on page load before render (no flash of wrong theme)
- Does NOT affect layout, widget placement, or navigation
- Persists across sessions (localStorage survives)

## MudBlazor Integration

```csharp
// Theme applied at app root
<MudThemeProvider Theme="@_currentTheme" />

// Theme loaded from config
private MudTheme _currentTheme = ThemeService.GetTheme(userPreference);

// ThemeService builds MudTheme from stored JSON
public MudTheme GetTheme(string themeName)
{
    var config = _themeConfig.Themes[themeName];
    return new MudTheme
    {
        PaletteLight = MapPalette(config.Palette),
        PaletteDark = MapPalette(config.PaletteDark),
        Typography = MapTypography(config.Typography)
    };
}
```

No custom CSS needed for 90% of theming. The MudTheme object controls all
MudBlazor component colors, typography, and spacing natively.

## CSS Escape Hatch

For cases the palette editor can't handle (custom animations, specific
component overrides, unusual layout tweaks):

### Admin Custom CSS (`/admin/theme` → "Advanced" tab)

```
┌─────────────────────────────────────────────────────────┐
│  Custom CSS (Advanced)                                   │
│  ⚠️ May break on MudBlazor updates. Use sparingly.       │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────┐    │
│  │ .mud-appbar {                                   │    │
│  │   border-bottom: 2px solid var(--mud-primary);  │    │
│  │ }                                               │    │
│  │ .scene-panel .mud-card {                        │    │
│  │   border-left: 3px solid var(--mud-secondary);  │    │
│  │ }                                               │    │
│  └─────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────┤
│  [Save]  [Clear]                                         │
└─────────────────────────────────────────────────────────┘
```

- Wizard+ only
- Injected as `<style>` tag after MudBlazor theme CSS
- Validated: must parse as valid CSS (no `<script>`, no `@import` from
  external domains)
- Stored in site config
- Applied to all users (not per-player custom CSS — that's a security risk)

## Theme Storage

```json
// In site config
{
  "theming": {
    "themes": { ... },
    "defaultTheme": "default",
    "customCss": ".mud-appbar { border-bottom: ... }",
    "branding": {
      "logo": "/files/site/logo.png",
      "favicon": "/files/site/favicon.ico",
      "headerImage": null
    }
  }
}
```

Cached server-side. Invalidated on admin save. Client fetches theme config
on initial load (included in app bootstrap payload — not a separate request).
