# Current collection browser acceptance captures

Captured on 2026-10-16 from actual SharpMUSH Blazor components and the built-in Phosphor ThemeProvider, using current generated scoped CSS, layered vendor CSS and self-hosted fonts. These show the generic collection foundation before the authored PF2e landing milestone, not the completed information architecture.

The inputs are API responses exported by the explicit Pf2eCollectionImportTests test after installing and reapplying all 4,711 pages of the owned pf2e-remaster 1.1.1 package at 2659a733a4519b76e64a727be2687794afc04f6b in a disposable Lightning world. The test also preserves a local article annotation and page IDs. The game's extra setting page is acceptance data. No production world was read or changed.

WikiCollectionImportUiTests exercises real component callbacks against those response fixtures: Blinded lookup, Ancestries to Dwarf, one home collection entry and the bounded sidebar. Its optional capture writes component HTML and theme parts. A separate isolated Chromium render serves that HTML with the current portal assets. This is component rendering evidence, not a claim of full running-application browser integration.

| Image | Component | Viewport/state |
| --- | --- | --- |
| home-390.jpg | WikiIndexWidget | 390 px; game home with one imported collection |
| collection-1100.jpg | WikiCollectionBrowser | 1100 px; complete count, bounded first page and collapsed category directory |
| ancestries-390.jpg | WikiCollectionBrowser | 390 px; actual imported Ancestries results |

All four captured components (including WikiSidebar) were checked at 320, 390 and 1100 pixels: document width equals viewport width and no descendant extends past it. Native Enter opens the category directory; the following Tab reaches its search field with a visible focus outline. Chromium 1234 and Playwright Core 1.62.1 were used in an isolated local context. These checks do not establish complete WCAG conformance or replace task testing of the next authored layout.

## Full running-portal navigation captures, 2026-10-19

The following originals come from the published local portal using the actual 4,723-page PF2e import, including the authored Wizard learning revision. They supersede the older component-shell captures for this navigation increment. The world and two ordinary acceptance characters are disposable; no production game was changed.

| Image | Running application state |
| --- | --- |
| navigation-directory-1440.png | PF2e collection's expanded category directory, searched for spells; introduction and member-filter actions remain distinct |
| navigation-members-1440.png | Spells category, members 51–100 of 678, compact text-only entries and retained category badges |
| navigation-members-390.png | The same member page on a phone, with visible range and Previous/Next controls |

The full capture run also checked category introductions and article return links at 320 pixels. All nine desktop/phone captures had document width at most viewport width and no page errors. The 50 text-only member links had no media/fallback boxes and minimum heights above 44 pixels at both desktop and phone widths. Real images remain covered by rendered component regressions; the shared tile retains its default placeholder for other callers.

The actual HTTP trace shows one 50-row member request per selected page, unchanged filtered-results return state (51–100 of 678), and isolated game/package membership for the same category key. Activating the bottom Next button focuses and scrolls the new range into view. Separate real account-panel character-switch checks removed a formerly visible private collection row and draft article at unchanged URLs; the second reader's article response was 404. A private pinned preview on wiki home was also removed on the same-URL switch, with fresh navigation reads and a 404 for the new reader. An article → sidebar category introduction → Back to results check retained the original search, category, offset and requested language. Delayed directory, preview and hero responses, failed refreshes and unmounting are covered by the home component regressions.

These results establish local application behavior for this increment. They do not bound native database candidate work, replace the related/More panel's legacy drain, establish the complete PF2e play journey or concurrent-load gate, or activate the semantic prototype. Those remain tracked in #2044 and #2031.

Final local validation for this increment: 2,852 bUnit cases passed, zero failed or skipped, followed by a successful client publish. The full running-portal Wizard route and Fighter creation/example/encounter/recovery/advancement route also fit at 1,440 and 320 pixels with restored result navigation; these are UI journey checks, not an automated rules or load validator.
