# Area 18: Theme Editor — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (18.1–18.3) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Theme Storage
- [ ] Define theme JSON schema (named themes, each with palette, paletteDark, typography)
- [ ] Default theme shipped with portal (dark, MudBlazor defaults + game accent)
- [ ] Theme config stored in site config (alongside layout, branding)
- [ ] Theme loaded in app bootstrap payload (no separate fetch on page load)

### MudBlazor Integration
- [ ] `ThemeService`: loads theme JSON → builds `MudTheme` object
- [ ] Apply theme at app root via `<MudThemeProvider Theme="@_currentTheme" />`
- [ ] Player preference in localStorage (`theme_preference: "default"`)
- [ ] Apply before first render (no flash of wrong theme)
- [ ] Dark/light variant support per named theme (PaletteDark)

### Admin Theme Editor (`/admin/theme`)
- [ ] Palette section: color pickers for all MudBlazor palette properties
- [ ] Hex input alongside each color picker (direct entry)
- [ ] Typography section: font family selection, base size, line height
- [ ] Font list: system fonts + curated Google Fonts subset (20-30 options)
- [ ] Branding section: logo upload, favicon upload, header image (optional)
- [ ] Live preview panel (miniature site mockup with sample components)
- [ ] Named themes: create, edit, duplicate, delete
- [ ] Default theme selector (which theme new users get)
- [ ] Save → update config → NATS event → cache invalidation

### CSS Escape Hatch (Advanced Tab)
- [ ] Textarea for custom CSS (Wizard+ only)
- [ ] Validation: must parse as valid CSS
- [ ] Reject: `<script>`, `@import` from external domains, `expression()`
- [ ] Injected as `<style>` after MudBlazor theme CSS
- [ ] Warning label: "May break on MudBlazor updates"
- [ ] Applied site-wide (not per-player)

### Player Theme Selection (`/settings/theme`)
- [ ] List of admin-defined themes with preview swatch
- [ ] Radio selection → "Preview" → "Apply"
- [ ] Stored in localStorage, persists across sessions

## Testing
- [ ] Theme applies correctly to all MudBlazor components
- [ ] Player switches theme: immediate visual change, persists on reload
- [ ] Admin edits theme: live preview accurate, save propagates to all clients
- [ ] Custom CSS: valid CSS applies, invalid CSS rejected with error
- [ ] No flash of wrong theme on page load
- [ ] Branding: logo appears in TopBar, favicon in browser tab
