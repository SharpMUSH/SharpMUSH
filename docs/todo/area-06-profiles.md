# Area 6: Character Profiles — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (6.1–6.5) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks
- [ ] Define profile schema (HTTP handler structured header + wiki body in Character: namespace)
- [ ] Implement PROFILE`* attribute convention on game objects
- [ ] Define field format types (text, mstring, markdown) and visibility (public, player, staff)
- [ ] HTTP handler: serve profile data (structured fields + wiki page content)
- [ ] HTTP handler: update profile fields (owner or Royalty+)
- [ ] Gallery system: upload images, URL references, profile icon designation
- [ ] IFileStorage interface for image uploads (local disk or object storage)
- [ ] Image validation (type, size limits)
- [ ] NATS event on profile edit (`portal.profile.edit`)
- [ ] Profile cache (invalidate on edit)

## Web UI
- [ ] Profile page component (`/character/Name`)
- [ ] Structured header rendering (fields by format type)
- [ ] Wiki body section (Markdown rendered)
- [ ] Gallery component (thumbnails, lightbox, reorder for owner)
- [ ] Profile editor (per-field editing based on editability schema)
- [ ] Character directory (`/characters`) with search/filter

## In-Game
- [ ] `+profile` command (view own profile as others see it)
- [ ] `+profile <name>` (view another character's public profile)
- [ ] `@profile/set <field>=<value>` (set a profile field)

## Testing
- [ ] Profile read: public fields visible to guests, hidden fields to staff only
- [ ] Profile edit: owner can edit own, Royalty+ can edit any
- [ ] Gallery: upload, reorder, set icon, delete
- [ ] Image validation: reject oversized, wrong type
- [ ] HTTP handler respects field visibility per requesting character's role
