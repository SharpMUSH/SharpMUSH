# SharpMUSH Configuration UI Analysis & Design Plan

**Date:** 2026-01-31  
**Status:** Analysis Complete

---

## 📊 Current State Analysis

### Configuration Structure

The SharpMUSH configuration is exposed via API endpoint: `GET /api/configuration`

**Response Structure:**
```json
{
  "Configuration": { /* SharpMUSHOptions */ },
  "Metadata": { /* SharpConfigAttribute metadata */ }
}
```

### Setting Categories (22 total)

| Category | Settings Count | Priority |
|----------|---------------|----------|
| **Limit** | 31 | 🔥 High |
| **Net** | 23 | 🔥 High |
| **Message** | 21 | 🔥 High |
| **Cosmetic** | 17 | Medium |
| **Attribute** | 14 | Medium |
| **Database** | 13 | 🔥 High |
| **Log** | 11 | High |
| **Command** | 10 | Medium |
| **File** | 8 | Medium |
| **Chat** | 7 | Low |
| **Cost** | 7 | Low |
| **Compatibility** | 5 | Low |
| **Flag** | 5 | Low |
| **TextFile** | 3 | Low |
| **Alias** | 2 | Low |
| **Debug** | 2 | Medium |
| **Function** | 2 | Low |
| **Restriction** | 2 | Medium |
| **Sitelock** | 2 | High |
| **Dump** | 1 | Medium |
| **Warning** | 1 | Low |
| **BannedNames** | TBD | Medium |
| **SitelockRules** | TBD | High |

**TOTAL: ~187+ settings** (estimated, some categories need deeper inspection)

---

## 🎯 Recommended UI Pattern: **Sidebar Navigation**

### Why Sidebar Wins at This Scale

❌ **Cards Don't Work:**
- 187 settings across 22 categories = massive scrolling
- Visual overload with that many cards
- Difficult to navigate between sections

✅ **Sidebar Navigation Benefits:**
- Clear category hierarchy
- One section visible at a time
- Scalable to 500+ settings
- Familiar pattern (VS Code, Discord, etc.)
- Easy to add search later

---

## 🏗️ Proposed UI Structure

### Layout Architecture

```
┌──────────────┬────────────────────────────────────────┐
│              │  🔍 Search Configuration               │
│  SIDEBAR     ├────────────────────────────────────────┤
│              │                                        │
│ 🔍 Search    │  [Current Category Name]              │
│              │                                        │
│ 🖥️ Server    │  ┌─ Section Header ──────────────┐   │
│   Network    │  │                                │   │
│   Database   │  │ Setting Name    [value input]  │   │
│              │  │ Help text here                 │   │
│ ⚡ Performance│  │                                │   │
│   Limits     │  │ Another Setting [input]        │   │
│              │  │ Description text               │   │
│ 🔒 Security  │  └────────────────────────────────┘   │
│   Sitelock   │                                        │
│              │  ┌─ Another Section ─────────────┐   │
│ 📝 Content   │  │ ...                            │   │
│   Messages   │  └────────────────────────────────┘   │
│   Cosmetic   │                                        │
│   Chat       │                                        │
│              │  ┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓   │
│ 📊 Logs      │  ┃ ⚠️ Unsaved Changes           ┃   │
│              │  ┃ [Reset] [Save Configuration] ┃   │
│ ⚙️ Advanced  │  ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛   │
│   Alias      │                                        │
│   Function   │                                        │
│   Warning    │                                        │
└──────────────┴────────────────────────────────────────┘
```

### Sidebar Category Grouping

**🖥️ Server**
- Network (23 settings)
- Database (13 settings)

**⚡ Performance**
- Limits (31 settings)
- Command (10 settings)

**🔒 Security**
- Sitelock (2 + rules)
- BannedNames (TBD)
- Restriction (2 settings)

**📝 Content**
- Messages (21 settings)
- Cosmetic (17 settings)
- Chat (7 settings)

**📊 Logs & Files**
- Log (11 settings)
- File (8 settings)
- TextFile (3 settings)
- Dump (1 setting)

**⚙️ Advanced**
- Attributes (14 settings)
- Flags (5 settings)
- Cost (7 settings)
- Compatibility (5 settings)
- Alias (2 settings)
- Debug (2 settings)
- Function (2 settings)
- Warning (1 setting)

---

## 🎨 UI Components Needed

### 1. ConfigSidebar Component
```razor
- Collapsible category groups
- Active section highlighting
- Unsaved changes indicator per section
- Search integration
```

### 2. ConfigSection Component
```razor
- Renders one category's settings
- Groups related settings with visual separators
- Auto-generates inputs based on type
- Shows validation errors inline
```

### 3. ConfigInput Component
```razor
- Smart input type detection (number, text, bool, etc.)
- Validation pattern enforcement
- Help text tooltip
- Changed state indicator
```

### 4. ConfigSearch Component
```razor
- Full-text search across all settings
- Filters sidebar to matching categories
- Highlights matching settings
- Jump-to-setting navigation
```

### 5. UnsavedChangesBar Component
```razor
- Sticky bottom bar
- Shows count of changed settings
- Warns before navigation
- Reset/Save actions
```

---

## 🔧 Technical Implementation Plan

### MudBlazor Dual-Sidebar Pattern

**Key Decision:** When navigating to `/admin/config`, implement **collapsible main drawer** pattern:
- Main `MudDrawer` (primary navigation) collapses to icon-only mode
- Secondary config drawer appears for category navigation
- Matches patterns from Figma, VS Code, Discord

### MudBlazor Components to Use

1. **MudDrawer** (x2) - Primary nav + Config nav
2. **MudNavMenu** / **MudNavGroup** - Hierarchical config categories
3. **MudCard** - Setting sections
4. **MudTextField** - Text/number inputs
5. **MudSwitch** - Boolean settings
6. **MudNumericField** - Number inputs
7. **MudExpansionPanels** - Optional for subsections within categories
8. **MudChip** - Setting counts, badges
9. **MudSnackbar** - Save confirmation
10. **MudDialog** - Unsaved changes warning

### Phase 1: Layout Infrastructure (2-3 hours)
- [ ] Modify `MainLayout.razor` to detect config route
- [ ] Add state management for drawer collapse/expand
- [ ] Create `ConfigLayout.razor` component
- [ ] Implement responsive behavior (mobile hamburger)

### Phase 2: Config Navigation Drawer (2-3 hours)
- [ ] Build `ConfigNavDrawer.razor` component
- [ ] Implement `MudNavGroup` for category hierarchy
- [ ] Add search functionality with highlighting
- [ ] URL routing per section (`/admin/config/network`, `/admin/config/limits`)
- [ ] Changed state indicators (dots on nav items)

### Phase 3: Content Rendering (3-4 hours)
- [ ] Create `ConfigSection.razor` base component
- [ ] Build input components per type:
  - `ConfigTextField.razor` (strings)
  - `ConfigNumericField.razor` (int, uint)
  - `ConfigSwitchField.razor` (bool)
  - `ConfigArrayField.razor` (string[])
  - `ConfigDictionaryField.razor` (Dictionary<string, string[]>)
- [ ] Add inline validation with `MudForm`
- [ ] Implement change tracking service

### Phase 4: Features & Polish (2-3 hours)
- [ ] Unsaved changes warning (`NavigationManager.RegisterLocationChangingHandler`)
- [ ] Save/Reset action bar (sticky `MudPaper` at bottom)
- [ ] Search with `MudAutocomplete` or custom filter
- [ ] Loading states with `MudProgressLinear`
- [ ] Success/error `MudSnackbar` notifications
- [ ] Keyboard shortcuts (Ctrl+S to save)

**Total Estimated Time:** 9-13 hours

### MudBlazor Theme Considerations

Current theme:
```csharp
PaletteDark.AppbarText = "#00f5b7"       // Cyan accent
PaletteDark.Secondary = "#00f5b7"
PaletteDark.Surface = "#242424"          // Dark surface
```

**Design Recommendations:**
- Use `Color.Secondary` for active states (cyan #00f5b7)
- Use `Color.Warning` for unsaved changes (amber)
- Keep dark theme consistent
- Use `Elevation` sparingly (0-2 max)

---

## 🎨 MudBlazor Dual-Sidebar Pattern

### Layout Structure

```razor
<MudLayout>
    <MudAppBar>
        <!-- Existing appbar -->
    </MudAppBar>
    
    <!-- Primary Drawer: Collapsed to icons when in config -->
    <MudDrawer @bind-Open="_mainDrawerOpen" 
               ClipMode="DrawerClipMode.Always" 
               Variant="@(_isConfigRoute ? DrawerVariant.Mini : DrawerVariant.Responsive)">
        <NavMenu Collapsed="@_isConfigRoute" />
    </MudDrawer>
    
    <!-- Secondary Drawer: Config navigation -->
    @if (_isConfigRoute)
    {
        <MudDrawer Open="true" 
                   ClipMode="DrawerClipMode.Always" 
                   Variant="DrawerVariant.Persistent"
                   Anchor="Anchor.Left"
                   Width="280px">
            <ConfigNavDrawer />
        </MudDrawer>
    }
    
    <MudMainContent>
        @Body
    </MudMainContent>
</MudLayout>
```

### Responsive Behavior

**Desktop (>960px):**
```
┌──┬──────────┬────────────────┐
│🏠│ 🔍       │  Content       │
│📊│ 🖥️ Server│                │
│⚙️│  Network │  Settings      │
│  │  Database│                │
└──┴──────────┴────────────────┘
60px  280px      Flexible
```

**Tablet (600-960px):**
- Main drawer: Overlay mode (hamburger menu)
- Config drawer: Persistent, 240px width

**Mobile (<600px):**
- Both drawers: Overlay mode
- Config drawer toggles with dedicated button in appbar

### MudBlazor Component Mapping

| UI Element | MudBlazor Component | Props |
|------------|-------------------|-------|
| Config Sidebar | `MudDrawer` | `Variant="Persistent"`, `Width="280px"` |
| Category Groups | `MudNavGroup` | `Expanded`, `Title`, `Icon` |
| Nav Links | `MudNavLink` | `Href`, `Match="NavLinkMatch.All"` |
| Search Bar | `MudTextField` | `Adornment="Start"`, `AdornmentIcon="@Icons.Material.Filled.Search"` |
| Setting Sections | `MudCard` | `Outlined="true"`, `Elevation="0"` |
| Text Inputs | `MudTextField<T>` | `Variant="Outlined"`, `For="@(() => model.Property)"` |
| Number Inputs | `MudNumericField<T>` | `Min`, `Max`, `Step` |
| Toggles | `MudSwitch<bool>` | `Color="Color.Secondary"` |
| Badges | `MudChip` | `Size="Size.Small"`, `Color="Color.Error"` |
| Save Bar | `MudPaper` | `Elevation="4"`, `Class="sticky-bottom"` |

### State Management Pattern

```csharp
// Services/ConfigStateService.cs
public class ConfigStateService
{
    private SharpMUSHOptions _originalConfig;
    private SharpMUSHOptions _currentConfig;
    
    public Dictionary<string, bool> ChangedSections { get; } = new();
    public event Action OnStateChanged;
    
    public void MarkChanged(string section)
    {
        ChangedSections[section] = true;
        OnStateChanged?.Invoke();
    }
}
```

---

## 📐 Design Specifications

### Color Palette
```css
Primary:     #667eea (purple)
Success:     #10b981 (green)
Warning:     #f59e0b (orange)
Danger:      #ef4444 (red)
Background:  #f9fafb (light gray)
Sidebar:     #ffffff (white)
Border:      #e5e7eb (light gray)
```

### Typography
```css
Headers:     1.5rem, 600 weight
Labels:      0.9rem, 500 weight
Help text:   0.85rem, 400 weight, muted color
```

### Spacing
```css
Sidebar width:  240px (desktop), 100% (mobile)
Content max:    1200px
Section gap:    24px
Input gap:      16px
```

---

## 🔍 Search Strategy

### Indexed Fields:
- Setting name (e.g., "max_logins")
- Display label (e.g., "Maximum Logins")
- Description text
- Category name

### Search Features:
- Case-insensitive
- Partial match
- Highlight matches in sidebar
- Auto-scroll to first match
- Keyboard navigation (↑/↓ arrow keys)

---

## ⚠️ Edge Cases to Handle

1. **Validation Errors**
   - Show inline below input
   - Prevent save if any validation fails
   - Highlight invalid sections in sidebar

2. **Browser Refresh with Unsaved Changes**
   - Show browser confirmation dialog
   - Optional: Local storage persistence

3. **Concurrent Edits**
   - Server should return version/timestamp
   - Detect stale data on save
   - Offer merge/overwrite options

4. **Mobile Experience**
   - Sidebar becomes hamburger menu
   - Settings stack vertically
   - Sticky save bar remains accessible

5. **Large Lists (BannedNames, SitelockRules)**
   - Paginate if >50 items
   - Add/remove item UI
   - Bulk import/export

---

## 🚀 Next Steps

1. **Immediate:** Review this analysis with stakeholders
2. **Design:** Create detailed wireframes/mockups for approval
3. **Implement:** Start with Phase 1 (data layer)
4. **Test:** Validate with real config data
5. **Ship:** Deploy behind feature flag initially

---

## 📝 Open Questions

- [ ] Should we show "Advanced" badge on dangerous settings?
- [ ] Do we need role-based permissions (some settings admin-only)?
- [ ] Import/export entire config as JSON?
- [ ] Real-time preview of changes before save?
- [ ] Config history/audit log?

---

**Author:** OpenClaw AI  
**Project:** SharpMUSH Configuration UI Redesign
