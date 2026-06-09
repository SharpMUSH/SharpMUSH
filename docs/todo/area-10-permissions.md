# Area 10: Permission & Visibility — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (10.1–10.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define PortalRole enum (Guest=0, Player=10, Royalty=20, Wizard=30, God=40)
- [ ] Implement role derivation from character flags → JWT claims
- [ ] Implement `HasRole(PortalRole required)` helper (simple >= comparison)
- [ ] Implement `CanEdit(IOwnedContent)` helper (owner OR Royalty+)
- [ ] Wire up [Authorize(Roles = "...")] attributes on controllers/pages
- [ ] Account-level role = max among linked characters (for admin access)
- [ ] Per-character role used for character-specific operations (mail, scenes)
- [ ] Role change propagation: flag change in-game → takes effect on token refresh
- [ ] Guest access: public wiki + public profiles (if site config allows)
- [ ] Hide admin nav link for roles below Royalty

## Per-System Permission Wiring
- [ ] Wiki: view public (all), view protected (Player+), create (Player+), edit own (Player+), edit any (Royalty+), delete (Wizard+)
- [ ] Scenes: view public (all), view private (participants + Royalty+), create/join (Player+), edit own poses (Player+), force-end (Royalty+)
- [ ] Profiles: view public (all), view hidden fields (Royalty+), edit own (Player+), edit any (Royalty+)
- [ ] Mail: own only (Player+), any (Wizard+)
- [ ] Admin panel: dashboard (Royalty+), config (Wizard+), server (God)
- [ ] Layout/theme editing: Wizard+ only

## Testing
- [ ] Role hierarchy: each role can do everything below it
- [ ] Guest cannot access authenticated content (mail, active scenes, settings)
- [ ] Player cannot access admin panel
- [ ] Royalty cannot access Wizard-level config or God-level server settings
- [ ] Ownership checks: player can edit own wiki page, cannot edit others'
- [ ] Token refresh picks up flag changes from game
