# Area 11: URL Strategy — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (11.1–11.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Configure Blazor WASM routing (all client-side routes)
- [ ] Server fallback: `app.MapFallbackToFile("index.html")` for non-API routes
- [ ] Implement wiki URL pattern: `/wiki/Page_Name` (underscore convention)
- [ ] Implement case-insensitive wiki lookup (URL case doesn't matter, display uses canonical)
- [ ] Implement `/character/Name` alias → resolves to `Character:Name` wiki page
- [ ] Implement scene URLs: `/scenes/42` (numeric ID permalink)
- [ ] Implement all route patterns per url-strategy.md
- [ ] Canonical URL redirects (spaces → underscores, wrong case, trailing slash)
- [ ] `<link rel="canonical">` on all pages
- [ ] SEO: bot user-agent detection middleware
- [ ] SEO: pre-render public pages to static HTML for bots
- [ ] SEO: OpenGraph meta tags (title, description, image, type, url)
- [ ] SEO: pre-rendered page cache (1 hour TTL, event-invalidated)
- [ ] Query parameter handling (?search=, ?page=)

## Testing
- [ ] Direct-link to wiki page: renders correctly on fresh load
- [ ] Case-insensitive: `/wiki/page_name` finds "Page Name"
- [ ] Redirect: spaces, wrong case, trailing slash all 301 to canonical
- [ ] Bot detection: Googlebot gets pre-rendered HTML
- [ ] Authenticated pages: bot gets 403, not content leak
- [ ] All route patterns accessible and rendering correct components
