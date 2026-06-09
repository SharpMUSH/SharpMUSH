# Area 19: Custom Widgets — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (19.1–19.3) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### Plugin Loading
- [ ] Define plugins/ directory (relative to app base)
- [ ] Startup: scan plugins/ for .dll files
- [ ] Load assemblies, scan for IPortalWidget implementations
- [ ] Register discovered widgets in DI
- [ ] Handle load errors gracefully (log, skip broken DLL, don't crash startup)

### Widget Registration
- [ ] Custom widgets appear in admin widget palette alongside built-ins
- [ ] Admin can place/configure custom widgets same as built-ins
- [ ] Widget config system works identically for custom widgets

### Security
- [ ] Document: plugins are trusted code, no sandboxing
- [ ] Only server operators install plugins (file system access required)
- [ ] No runtime plugin installation via web UI (restart required)

### Developer Experience
- [ ] Template repository: sharpmush-widget-template
  - [ ] Pre-configured .csproj (correct TFM, MudBlazor + portal dependency)
  - [ ] Example widget with config
  - [ ] Example widget with SignalR subscription (real-time)
  - [ ] README: interface docs, available DI services, config system
  - [ ] Build script → dist/ folder with output DLL
- [ ] Document available DI services for plugin authors
- [ ] Document: what custom widgets can and cannot do

### Limitations (Documented, Not Implemented)
- [ ] Document: no IHostedService from plugins in v1
- [ ] Document: no route registration from plugins
- [ ] Document: no admin-panel extension from plugins
- [ ] Document future considerations: hot-reload, plugin manifests, declarative widgets

## Testing
- [ ] Plugin loading: valid DLL in plugins/ → widget available in palette
- [ ] Broken DLL: logged, skipped, other widgets unaffected
- [ ] Custom widget renders in assigned zone with correct config
- [ ] Custom widget with SignalR subscription receives updates
- [ ] No DLL in plugins/: portal starts normally with only built-in widgets
