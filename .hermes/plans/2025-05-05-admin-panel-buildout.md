# Admin Panel Build-out Plan

## Current State
- ConfigLayout + ConfigNavDrawer already wired up
- DynamicConfig.razor renders config forms from schema metadata (groups, properties)
- GET/PUT /api/configuration endpoints working with tests
- Client-side + server-side validation in place
- Save bar with dirty tracking functional

## What's Missing / Needs Improvement

### 1. ConfigIndex page — Replace placeholder "Coming Soon" chips with actual settings counts
The ConfigIndex cards say "Coming Soon" but the DynamicConfig page already works for all categories.
- Remove all "Coming Soon" chips
- Add dynamic category setting counts from the schema

### 2. ConfigNavDrawer search — Actually filter the nav items
Search box exists but does nothing. Wire it up to filter categories.

### 3. BannedNames, Sitelock, Restrictions pages — Ensure they work with the API
These are custom pages that manage list/dictionary config data. Verify they have proper
CRUD operations via the API.

### 4. Import/Export Config functionality
- ImportConfig.razor exists — wire it to the import endpoint
- Add Export button that downloads current config as JSON

### 5. Visual polish
- Consistent theme usage (dark theme, Color.Secondary accent)
- Responsive save bar on mobile
- Loading skeletons instead of plain progress bar
- Success/error feedback animations

## Execution Order
1. Fix ConfigIndex — remove "Coming Soon", make all categories navigable
2. Wire up ConfigNavDrawer search filtering  
3. Verify BannedNames/Sitelock/Restrictions pages work
4. Wire Import/Export
5. Visual polish pass
