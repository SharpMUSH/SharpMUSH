# Screenshot provenance

These JPEGs were captured on 2026-10-16 from actual SharpMUSH Blazor components, not drawn or reconstructed UI. `SharpMUSH.Tests.BUnit/Pages/DiscordGuideUiTests.cs` supplies synthetic GET-only API responses and the real English resources. The production `ThemeProvider` uses the built-in Phosphor theme; captures load the current generated scoped bundle, `wwwroot/css/custom.css`, its layered MudBlazor import, theme attributes and self-hosted fonts.

| Asset | Actual component | Viewport | State |
|---|---|---|---|
| `secret-desktop.jpg` | `Pages/Admin/Secrets.razor` | 1100 px | `discord.token` name, empty password field, unset secret |
| `connection-desktop.jpg` | `Pages/Admin/AdminDiscord.razor` | 1100 px | Synthetic connected bot with all five normal checklist steps complete |
| `channels-mobile.jpg` | `Pages/Admin/AdminDiscord.razor` | 390 px | Synthetic restricted roleplay and unrestricted newbie-help channels |

The screenshots crop real component boundaries. The bot name, server name, channel IDs and connection state are examples. No application, bot, invitation, credential or external message was created or changed for these captures. They are not live connection evidence or screenshots of Discord's Developer Portal.

To regenerate markup, build the bUnit project, then run `DiscordGuideUiTests` with `SHARPMUSH_DISCORD_GUIDE_CAPTURE_PATH` set to a disk-backed scratch directory. The optional capture writes both rendered component HTML and the production theme's `theme-parts.json`; apply its keys as `data-<key>` attributes on the HTML root, as `wwwroot/js/layout.js` does. Serve that markup locally with current portal CSS/assets and capture it in an isolated browser context. Both complete pages were checked at 390 and 1100 px: document width equals viewport width and no descendant extends beyond it. The screenshots use Chromium 1234 and Playwright Core 1.62.1.
