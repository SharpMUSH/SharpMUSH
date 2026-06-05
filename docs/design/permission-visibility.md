# Permission & Visibility

## Overview

Hierarchical role-based permissions. Start simple with PennMUSH's existing
hierarchy, expand later via @powers or custom @locks through API. No
over-engineering now — the hierarchy covers 95% of needs.

## Role Hierarchy

```
God (#1)          — Unrestricted. Cannot be overridden. Single entity.
  │
Wizard            — Full admin. All portal features. All game commands.
  │
Royalty           — Senior staff. Most admin features. Cannot modify Wizards.
  │
Player            — Standard authenticated user. Own content + public content.
  │
Guest             — Unauthenticated or guest character. Read-only public content.
```

**Rule:** Higher roles inherit ALL permissions of lower roles. A Wizard can
do everything a Player can do, plus Wizard-level actions.

## Permission Checks

### Portal-Level (Web)

```csharp
// Simple role check — covers most cases
[Authorize(Roles = "Player")]          // Player and above
[Authorize(Roles = "Royalty")]         // Royalty and above
[Authorize(Roles = "Wizard")]          // Wizard and above

// Ownership check — "is this my content?"
if (currentCharacter.Id == content.OwnerId || currentRole >= Role.Royalty)
```

### Game-Level (MUSH)

Existing PennMUSH permission model applies unchanged:
- Flags determine capability (WIZARD, ROYALTY, etc.)
- @locks gate per-object access
- @powers grant fine-grained abilities

The portal respects game-level permissions by routing through HTTP handlers
(which run in the game engine's permission context).

## Per-System Permissions

### Wiki

| Action          | Guest | Player | Royalty | Wizard | God |
|-----------------|-------|--------|---------|--------|-----|
| View public     | ✓     | ✓      | ✓       | ✓      | ✓   |
| View protected  | ✗     | ✓      | ✓       | ✓      | ✓   |
| Create page     | ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit own page   | ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit any page   | ✗     | ✗      | ✓       | ✓      | ✓   |
| Delete page     | ✗     | ✗      | ✗       | ✓      | ✓   |
| Protect/lock    | ✗     | ✗      | ✓       | ✓      | ✓   |
| Set AllowHtml   | ✗     | ✗      | ✗       | ✓      | ✓   |

**Character: namespace restriction:** Only the character's owner (or Royalty+)
can edit `Character:<name>` pages.

### Scenes

| Action              | Guest | Player | Royalty | Wizard | God |
|---------------------|-------|--------|---------|--------|-----|
| View public scenes  | ✓     | ✓      | ✓       | ✓      | ✓   |
| View private scenes | ✗     | part.  | ✓       | ✓      | ✓   |
| Create scene        | ✗     | ✓      | ✓       | ✓      | ✓   |
| Join scene          | ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit own poses      | ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit others' poses  | ✗     | ✗      | ✗       | ✓      | ✓   |
| Force-end scene     | ✗     | ✗      | ✓       | ✓      | ✓   |
| Delete scene        | ✗     | ✗      | ✗       | ✓      | ✓   |

"part." = participants only (player must be in the scene)

### Character Profiles

| Action               | Guest | Player | Royalty | Wizard | God |
|----------------------|-------|--------|---------|--------|-----|
| View public profile  | ✓     | ✓      | ✓       | ✓      | ✓   |
| View hidden fields   | ✗     | ✗      | ✓       | ✓      | ✓   |
| Edit own profile     | ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit others' profile | ✗     | ✗      | ✓       | ✓      | ✓   |
| Upload gallery       | ✗     | own    | any     | any    | any |

### Mail

| Action          | Guest | Player | Royalty | Wizard | God |
|-----------------|-------|--------|---------|--------|-----|
| Read own mail   | ✗     | ✓      | ✓       | ✓      | ✓   |
| Send mail       | ✗     | ✓      | ✓       | ✓      | ✓   |
| Read any mail   | ✗     | ✗      | ✗       | ✓      | ✓   |

### Admin Panel

| Section           | Guest | Player | Royalty | Wizard | God |
|-------------------|-------|--------|---------|--------|-----|
| View admin panel  | ✗     | ✗      | ✓       | ✓      | ✓   |
| Player management | ✗     | ✗      | ✓       | ✓      | ✓   |
| Game config       | ✗     | ✗      | ✗       | ✓      | ✓   |
| Server settings   | ✗     | ✗      | ✗       | ✗      | ✓   |

### Layout & Theming

| Action            | Guest | Player | Royalty | Wizard | God |
|-------------------|-------|--------|---------|--------|-----|
| Choose color/theme| ✗     | ✓      | ✓       | ✓      | ✓   |
| Edit site layout  | ✗     | ✗      | ✗       | ✓      | ✓   |
| Edit nav links    | ✗     | ✗      | ✗       | ✓      | ✓   |
| Custom CSS        | ✗     | ✗      | ✗       | ✓      | ✓   |

## Role Mapping: Game ↔ Portal

The portal determines a user's role from their game character's flags:

```
Character has WIZARD flag  → Portal role: Wizard
Character has ROYALTY flag → Portal role: Royalty
Character is God (#1)      → Portal role: God
Character exists, none above → Portal role: Player
No character (anonymous)   → Portal role: Guest
```

If a user has multiple characters, portal role = highest role among their
linked characters (for account-level operations like admin panel access).

Per-character operations use that character's specific role (reading mail
as Character A uses Character A's permissions, not the account's highest).

## Future Expansion (Not Now)

These are explicitly deferred. The hierarchy handles current needs.

- **@powers integration:** Fine-grained abilities (e.g., "can moderate BBS
  without full Royalty"). Would map to portal claims/policies.
- **Custom @locks via API:** Per-object access control (e.g., wiki page
  locked to members of a specific group). Would query game engine.
- **Group-based permissions:** "Members of Faction X can edit Faction X's
  wiki page." Requires group system integration.
- **Per-widget visibility:** "Only Royalty+ sees the admin stats widget."
  Widget system can add this when implemented.

When needed, these extend the hierarchy — they don't replace it. A future
`IPermissionService` could check: role hierarchy first, then @powers, then
@locks. But for now: just the hierarchy.

## Implementation Notes

```csharp
public enum PortalRole
{
    Guest = 0,
    Player = 10,
    Royalty = 20,
    Wizard = 30,
    God = 40
}

// Simple check: "at least this role"
public bool HasRole(PortalRole required) => CurrentRole >= required;

// Ownership + role fallback
public bool CanEdit(IOwnedContent content)
    => content.OwnerId == CurrentCharacterId || HasRole(PortalRole.Royalty);
```

Claims are set in the JWT at login time. Role is derived from character flags.
If character flags change in-game (Wizard promotes someone to Royalty), the
change takes effect on next token refresh (not instant — acceptable latency).
