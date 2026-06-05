# Caching Strategy

## Overview

Event-driven cache invalidation via NATS. Server-side distributed cache for
rendered content. Client-side localStorage for preferences only. No stale
reads on critical content — always event-invalidated.

## What's Cached

| Content                    | Cache Location  | Invalidation Trigger              | TTL     |
|----------------------------|-----------------|-----------------------------------|---------|
| Wiki rendered HTML         | Server (dist.)  | Wiki page edit event              | None*   |
| Character profile card     | Server (dist.)  | Profile edit event                | None*   |
| Scene list (active)        | Server (dist.)  | Scene state change event          | None*   |
| Search results             | Server (dist.)  | —                                 | 60s     |
| Navigation/layout config   | Server (dist.)  | Admin layout save event           | None*   |
| Help file rendered HTML    | Server (dist.)  | Help file edit event              | None*   |
| Bot pre-rendered pages     | Server (file/mem)| Content edit event                | 1 hour  |
| User preferences (theme)   | Client (localStorage) | User action              | None    |
| JWT access token           | Client (memory) | Expiry                            | 15min   |
| Refresh token              | Client (httpOnly cookie) | Expiry                   | 7 days  |

*None = no TTL. Lives until explicitly invalidated by event. Survives server
restart only if using Redis/persistent store.

## Server-Side Cache

### Interface

```csharp
// Standard ASP.NET distributed cache
services.AddDistributedMemoryCache();          // Single instance (dev/small)
// OR
services.AddStackExchangeRedisCache(options => // Multi-instance / persistent
{
    options.Configuration = "localhost:6379";
});
```

### Cache Keys

```
wiki:rendered:{page_name}           → rendered HTML string
profile:card:{character_id}         → profile card JSON (name, icon, summary)
scenes:active_list                  → serialized scene list
layout:config                       → layout JSON
help:rendered:{topic_name}          → rendered HTML string
seo:prerender:{url_path}            → full HTML page for bots
```

### Write-Through Pattern

```csharp
// On wiki page save:
await _wikiStore.Save(page);
await _cache.RemoveAsync($"wiki:rendered:{page.Name}");
await _nats.Publish("portal.wiki.changes", new { page = page.Name, action = "edit" });

// On wiki page read (cache-aside):
var html = await _cache.GetStringAsync($"wiki:rendered:{page.Name}");
if (html == null)
{
    var page = await _wikiStore.GetByName(name);
    html = _markdig.ToHtml(page.Markdown);
    await _cache.SetStringAsync($"wiki:rendered:{name}", html);
}
return html;
```

## Event-Driven Invalidation

NATS events trigger cache invalidation. The portal server subscribes to
relevant subjects:

```
portal.wiki.changes   → invalidate wiki:rendered:{page}
portal.profile.edit   → invalidate profile:card:{character_id}
portal.scene.live     → invalidate scenes:active_list (on start/end only)
portal.layout.change  → invalidate layout:config
```

**Why not TTL-only:** Stale wiki pages or profiles are confusing. A player
edits their profile and still sees the old version for 30s — bad UX. Event
invalidation means the next request after an edit always gets fresh content.

**Search results use TTL only (60s):** Search is inherently approximate.
A 60s window of slightly stale results is acceptable and avoids invalidating
on every write to any indexed content.

## Client-Side Caching

### What's in localStorage

- `theme_preference`: user's chosen color theme (persists across sessions)
- `sidebar_collapsed`: left/right sidebar state
- `last_character_id`: last active character (for auto-switch on next login)

### What's NOT in localStorage

- Content (wiki, profiles, scenes) — SignalR push keeps the UI fresh
- Auth tokens — access token in memory, refresh in httpOnly cookie
- Mail — always fetched fresh (could be marked read from another client)

### WASM Memory

- MString.ToHtml() results are not cached client-side. They're fast enough
  to re-render on each display (MString objects are small, conversion is O(n)
  on string length).
- Scene history (scrollback) lives in component state. Lost on navigation away
  from the scene page. Re-fetched on return.

## Pre-rendering Cache (SEO)

Bot-requested pages are rendered to static HTML and cached:

```
Key:    seo:prerender:/wiki/Dragon_Lore
Value:  Full HTML page (head + meta + body)
TTL:    1 hour (fallback)
Invalidation: Content edit event clears specific page
```

Pre-render happens on first bot request (lazy). Not pre-generated for all
pages. High-traffic pages stay warm; obscure pages regenerate on demand.

## Cache Warming

On server start:
- Layout config loaded into cache (always needed)
- Front page widgets pre-rendered (first thing users hit)
- Nothing else pre-warmed — lazy population is fine for most content

## Monitoring

- Cache hit/miss ratio visible in admin panel (System Status widget or
  `/admin/server` for God)
- High miss ratio on wiki pages indicates frequent edits (normal for active
  games) or cache eviction pressure (increase memory/switch to Redis)
