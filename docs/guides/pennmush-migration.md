# Migrating a PennMUSH world

SharpMUSH can import a PennMUSH flatfile and, optionally, the world's `mush.cnf`. This is the only supported direct world import. RhostMUSH and TinyMUX have no direct supported importer; move those worlds through a compatible PennMUSH export only after validating that intermediate conversion independently.

Import is one-way. Keep the original PennMUSH installation and its immutable pre-cutover backup until the SharpMUSH world has passed its observation period. Never treat a successful upload as proof that the game is ready for players.

## 1. Decide whether the world is eligible

Make a current inventory before changing either server:

- PennMUSH version, flatfile format, database size, highest dbref, and object count;
- custom flags, powers, attributes, locks, command/function restrictions, and `mush.cnf` includes;
- channels, mail, aliases, player passwords, guests, and login policy;
- globals, master-room objects, HTTP and event handlers, startup attributes, and scheduled jobs;
- packages and representative softcode entry points;
- DNS, telnet/WebSocket endpoints, bots, monitoring, mail delivery, and other external integrations;
- uploaded wiki assets and any files used by plugins or operational scripts.

Create and checksum a PennMUSH flatfile while the source is quiescent. Copy the flatfile, the complete configuration tree, help/text files, and any other external state to storage the SharpMUSH host cannot overwrite. The importer accepts one uploaded `mush.cnf`; it does not follow that file's includes, so inventory included files and reconcile their effective settings yourself.

Check the destination with `@storage`. The import needs room for the live world, a staging world, and the world it replaces. A production-sized first rehearsal belongs on a disposable, non-public SharpMUSH installation with the same image and storage settings planned for production.

## 2. Rehearse the import

1. Start a clean SharpMUSH stack and protect `/setup` from untrusted visitors. Claim the administrator account linked to `#1`.
2. From setup, open **Import PennMUSH**. Upload the optional `mush.cnf` with the database so configuration-derived object references and restrictions are in place before objects are converted.
3. Read every skipped-configuration warning. Reproduce settings SharpMUSH supports and record intentional exceptions.
4. Upload the flatfile and let conversion finish. SharpMUSH writes into a separate `<world>.staging-*` LMDB environment; a failed conversion does not partially replace the live world.
5. Review the import diagnostics before promotion. Promotion moves the former live world to `<world>.previous` and atomically installs the staging world.
6. Return to setup to choose HTTP/event handlers and bundled packages. An imported world's own handlers may already be present; do not install a second handler without understanding which attributes the setup choice moves or removes.

An import rehearsal is evidence, not authority. Do not expose its endpoints, accept player changes, or quietly promote it into the production game. Repeat the process from a new final flatfile at cutover.

The parity harness in [`tools/parity/README.md`](../../tools/parity/README.md) explains the project's automated PennMUSH comparison method. Its passing scenarios are useful compatibility evidence, but they do not replace validation of a game's custom code.

## 3. Validate the rehearsed world

Record pass/fail, the command or screen used, and any accepted difference for every row.

| Domain | Minimum evidence |
| --- | --- |
| Identity and passwords | God, wizard, ordinary player, guest, aliases, failed login, password reset, and account-to-character linking behave as expected. Imported password hashes should be replaced with new passwords over time. |
| Objects and dbrefs | Object counts and highest dbref are plausible; known rooms, exits, things, and players retain the dbrefs embedded in softcode. |
| Attributes and locks | Sample inherited, private, wizard, visual, and large attributes; evaluate representative lock types and failure messages. |
| Flags and powers | Custom and built-in definitions exist, permissions are correct, and representative objects retained assignments. |
| Channels and mail | Membership, aliases, history expectations, mail folders, send/read/delete, and privacy are checked with distinct accounts. |
| Configuration | Effective `mush.cnf` settings, command/function restrictions, names, limits, login policy, and object references are reconciled against the inventory. |
| Handlers and packages | HTTP/event handlers, globals, master-room code, startup behavior, and each selected package have one intentional owner. |
| Softcode | Run a risk-ranked suite of player commands, building commands, queues, iterators, regexes, locks, persistence, and error paths. |
| Connections | Log in over telnet and the portal WebSocket terminal; exercise ANSI, negotiation, disconnect, and reconnect behavior. |
| Portal | Claim/login state, account-character linking, wiki, permissions, terminal, and administrator pages work for the intended roles. |
| Integrations | DNS, proxy routes, bots, outbound HTTP, mail, monitoring, metrics, and backups work from the isolated environment. |

Investigate differences rather than normalizing the transcript until it looks green. Database import coverage and engine parity are separate questions.

## 4. Cut over

Define the go/no-go checklist and rollback authority before the maintenance window.

1. Announce the freeze and stop logins or place PennMUSH in maintenance mode.
2. Drain queued work, create the final source flatfile, archive source configuration and external state, and record checksums and counts.
3. Repeat the rehearsed import on the intended SharpMUSH host. Do not reuse a changed rehearsal database.
4. Run the abbreviated acceptance suite: administrator and player login, known dbrefs, core softcode, mail/channel samples, telnet, portal/WebSocket, integrations, backup, storage, health, logs, and metrics.
5. Change DNS/proxy/client endpoints only after the named approver signs off. Keep PennMUSH stopped but immediately recoverable.
6. Observe error rates, queues, storage, authentication, connection churn, and player reports through the agreed window. Preserve the import report and cutover log.

## 5. Roll back safely

Rollback triggers should be objective: authentication failure, widespread data mismatch, critical softcode incompatibility, unsafe storage behavior, or an unrecoverable integration failure.

To return to PennMUSH, stop accepting SharpMUSH changes, restore the old endpoint, and start the preserved PennMUSH world from the pre-cutover state. Player changes made after SharpMUSH opened are not automatically transferable; record that data-loss boundary in the announcement.

`<world>.previous` is different: it is the SharpMUSH world displaced when the staged import was promoted. It is useful for undoing a just-promoted import on the SharpMUSH side, not a backup of PennMUSH. Stop the SharpMUSH stack before moving LMDB directories; never copy or read a live `data.mdb` directly. For ongoing SharpMUSH recovery, restore a point-in-time world copy made by `@backup` or a Restic snapshot containing such a copy, following the [backup and restore procedures](../../deploy/README.md#backups-restic).

Keep the source archive and `<world>.previous` until acceptance and the observation window are complete. Delete superseded data only after the operator has recorded the decision and verified a separate recoverable backup.
