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
