<!-- help-article
{
  "corpus": "help",
  "id": "object-snapshots",
  "lookup": "object snapshots",
  "aliases": [
    "@SNAPSHOT"
  ],
  "sections": [
    {
      "id": "commands",
      "heading": "Commands",
      "lookup": "object snapshots commands"
    },
    {
      "id": "preview-and-restore-selection",
      "heading": "Preview and restore selection",
      "lookup": "object snapshots preview and restore selection"
    },
    {
      "id": "captured-data",
      "heading": "Captured data",
      "lookup": "object snapshots captured data"
    },
    {
      "id": "retention-limits",
      "heading": "Retention limits",
      "lookup": "object snapshots retention limits"
    },
    {
      "id": "recovery",
      "heading": "Recovery",
      "lookup": "object snapshots recovery"
    },
    {
      "id": "validation",
      "heading": "Validation",
      "lookup": "object snapshots validation"
    }
  ]
}
-->
# Object Snapshots

An object snapshot saves a room, exit or code object's attributes, and optionally its locks, flags and name, so a mistake on that one object can be undone without restoring the whole world. Players cannot be snapshotted.

| Permission | Allows |
| --- | --- |
| `snapshots.capture` | capturing, and listing |
| `snapshots.restore` | previewing and restoring, and listing |

Capture does not imply restore. You must also control the object, and the permission must be held by the account linked to the player you are playing: an object you own cannot borrow it. Every operation checks the account and its permissions again, so a change of ownership or a revoked role applies at once.

A snapshot is visible to the account that took it, for as long as the character using it can still read the attributes and locks it holds.

## Commands

- `@snapshot/capture <object>=<description>` - take a snapshot
- `@snapshot/list <object>` - list the object's snapshots, and any pending recovery
- `@snapshot/preview <object>=<snapshot-id>` - show what a restore would change, with a preview token
- `@snapshot/restore <object>=<snapshot-id>,<preview-token>` - restore what the preview showed
- `@snapshot/resolve <object>=<pending-recovery-id>` - keep the object as it is after an interrupted restore

The portal's Object snapshots page does the same.

Output: the message `@snapshot` shows, such as `Captured snapshot <id>`, the list, or the preview and its token.

## Preview and restore selection

By default a preview and restore cover the captured attributes and their attribute flags. Add switches to include more:

- `/locks` - the object's locks
- `/flags` - the object's flags
- `/name` - the object's name

Give the restore exactly the switches you gave the preview. On the portal you can also pick individual attributes before previewing.

A restore leaves alone attributes created after the snapshot, locks the snapshot does not hold, and where the object sits in the world. If the object or the selection changes after the preview, its token is no longer accepted: preview again and check the new result.

## Captured data

A snapshot records:

- each attribute's value with its markup, its flags, and who created it;
- the object's locks and their flags;
- the object's flags, name, type, full identity and creator;
- when it was taken, its description, and its format version.

A restore never touches the owner, location, home, parent, zone, powers, quota or money. Restored attributes are written as ordinary attribute writes, so the restoring character's normal rights decide their owner, and normal attribute and flag restrictions apply. A privileged or locked lock still needs its usual administrative command.

## Retention limits

| Limit | Value |
| --- | --- |
| Snapshots kept per object | 1-20 (10 by default) |
| Attributes per snapshot | 1024 readable attributes |
| Size of one snapshot | 2 MiB |

A pending recovery snapshot is kept on top of these and is never pruned. Snapshots live in the world database: they survive restarts and travel in world backups, with no separate files to copy.

## Recovery

A restore changes the object one write at a time; it is not one all-or-nothing step. Before the first change it saves the object as it was (a recovery snapshot) and marks the object as having a pending recovery.

### After an interrupted restore

If a restore is cancelled or fails part way, `@snapshot/list` shows the pending recovery. Preview and restore that recovery snapshot before any other restore. It covers exactly the fields the interrupted restore selected, and removes attributes or locks that restore created; by default it selects only the interrupted restore's attributes and locks, so unrelated later edits are kept. The portal fixes the fields and switches to match; in the game, give the original switches yourself.

If a SAFE flag or another restriction was applied in between, clear it with its usual command first. A failure to clear the pending marker after a successful restore is reported as needing recovery too.

### Keeping the current state

When the original account can no longer recover the object, for example after the object or the account changed hands, anyone who controls the object and holds `snapshots.restore` can accept it as it is with `@snapshot/resolve` and the exact pending recovery ID, or the portal's Acknowledge current state button. That clears the marker without undoing the partial changes, keeps the recovery snapshot, and records who resolved it and when.

### Concurrent edits

The server runs one snapshot write at a time, but other commands can still change the object during a restore. Avoid editing it while a restore runs.

## Validation

A restore is refused before any change when the snapshot is malformed or in an unsupported format, the object's type changed, its dbref was recycled, an attribute's creator or flag or a lock's reference no longer exists, the preview is invalid, or a permission is missing.

Snapshots do not replace [@backup] for the whole world, or package rollback for packages.

::: seealso
- [administrative capabilities]
- [@backup]
:::
