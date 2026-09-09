# Object snapshots

Object snapshots recover mistakes on a room, exit or code object without restoring the
world. The active player needs snapshots.capture to capture and snapshots.restore
to preview/restore. Either scope permits listing. The player must control the object. All operations recheck the linked account
and current capabilities. Another owned object cannot borrow the player's delegation.
Snapshots are visible to their creating account while the current character can still
read their attributes and locks. Ownership changes and role revocation apply immediately.

    @snapshot/capture object=description
    @snapshot/list object
    @snapshot/preview object=snapshot-id
    @snapshot/restore object=snapshot-id,preview-token
    @snapshot/resolve object=pending-recovery-id

Add /locks, /flags or /name to preview and restore to include those fields. Use identical
switches for preview and restore. The basic operation restores the captured attributes
and their flags. Attributes created afterward, locks not present in the snapshot, and
structural relationships are retained. The portal's Object snapshots page also lets you
select individual attribute names before previewing. A changed target or selection
invalidates the preview; inspect a fresh preview before trying again.

Capture records markup, attribute flags, ancestor access metadata and creator identities, locks and their flags,
object flags, name, full object identity, type, creator, time, description, schema and
retention policy. Credentials and account relationships are excluded by rejecting player
objects. Object owner, location, home, parent, zone, powers, quota and currency are never
restored. Attribute creator identities validate historical read access; ordinary attribute
writes determine ownership when restoring. Privileged or locked locks require their normal
administrative workflow. Normal attribute and object flag restrictions still apply.

Histories retain 1–20 snapshots per object (10 by default), with an additional pending
recovery image protected from pruning. Each image is limited to 1024 readable attributes
and 2 MiB. Histories use the existing database expanded-object store; they survive restarts
and travel in provider world backups. No external file directory needs copying.

Restore is a sequence of cache-invalidating Mediator mutations, not a cross-command
transaction. Before the first change, a durable before-image and pending recovery marker
are stored. If a mutation, cancellation or process failure interrupts restoration, the
marker identifies the recovery image. Preview and restore that image before another
restore. Recovery uses exactly the fields selected by the interrupted operation and explicitly
removes attributes or locks that operation created. Default recovery selections include only
the interrupted operation’s attributes and locks, preserving unrelated later edits. The portal fixes the fields and switches to the recorded recovery selection; game commands require the original switches. Correct any newly applied SAFE/privileged restrictions through their normal
commands first. Storage failure while clearing the marker is also reported as requiring
recovery. If the original account can no longer recover (for example after ownership or
account changes), a current controller with snapshots.restore can explicitly acknowledge
the current object using /resolve and the exact pending recovery ID, or the portal’s
Acknowledge current state action. This clears the marker without undoing partial changes,
retains the image, and records the resolving account, character and time.

Writes through this service are serialized within the single engine; other
commands can still change an object, so avoid concurrent editing during restore.

A malformed image, unsupported schema, changed object type, recycled object number,
missing attribute creator/flag, missing stable lock reference, invalid preview or missing
permission fails before restoration. Whole-world backups and package rollback continue
to serve their existing purposes.
