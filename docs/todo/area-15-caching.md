# Area 15: Caching Strategy — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (15.1–15.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Server-Side Cache
- [ ] Register IDistributedCache (MemoryCache for dev, Redis option for prod)
- [ ] Define cache key conventions (wiki:rendered:{name}, profile:card:{id}, etc.)
- [ ] Implement cache-aside pattern for wiki rendered HTML
- [ ] Implement cache-aside pattern for profile card data
- [ ] Implement cache for active scenes list
- [ ] Implement cache for layout config
- [ ] Implement cache for help rendered HTML
- [ ] Search results: 60s TTL (no event invalidation)

### Event-Driven Invalidation
- [ ] Subscribe to `portal.wiki.changes` → invalidate wiki:rendered:{page}
- [ ] Subscribe to `portal.profile.edit` → invalidate profile:card:{character_id}
- [ ] Subscribe to `portal.scene.live` → invalidate scenes:active_list (start/end only)
- [ ] Subscribe to `portal.layout.change` → invalidate layout:config
- [ ] Subscribe to content edit events → invalidate seo:prerender:{url}

### Client-Side
- [ ] localStorage wrapper: theme_preference, sidebar_collapsed, last_character_id
- [ ] No content caching client-side (SignalR keeps fresh)
- [ ] JWT access token in memory only (not localStorage)

### Pre-rendering (SEO)
- [ ] Pre-render cache store (seo:prerender:{url_path})
- [ ] Lazy render on first bot request
- [ ] 1 hour TTL fallback
- [ ] Invalidate on content edit (specific page)

### Cache Warming
- [ ] On server start: load layout config + render front page widgets
- [ ] Everything else: lazy population

### Monitoring
- [ ] Cache hit/miss counters (exposed in admin System Status / /admin/server)
- [ ] Log cache eviction events at debug level

## Testing
- [ ] Wiki edit → cache invalidated → next read fresh
- [ ] Profile edit → cache invalidated → profile card updated
- [ ] Scene start/end → active list cache refreshed
- [ ] Layout save → all clients see new layout on next navigation
- [ ] Search results: stale for up to 60s after edit (acceptable)
- [ ] Server restart: cache cold, warms on first requests
