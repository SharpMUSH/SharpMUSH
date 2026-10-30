# Migrating a PennMUSH, RhostMUSH or TinyMUX world

SharpMUSH can import a PennMUSH flatfile and, optionally, the world's `mush.cnf`, mail database (`maildb`), and chat database (`chatdb`). It can also import a RhostMUSH flatfile with its mail dumps, or a TinyMUX flatfile with its `mail.db` and `comsys.db`; see [Importing from RhostMUSH or TinyMUX](#importing-from-rhostmush-or-tinymux) for what those imports carry over and what they leave behind. Everything below applies to all three; where it names a PennMUSH file, use the matching file from your server.

Import is one-way. Keep the original PennMUSH installation and its immutable pre-cutover backup until the SharpMUSH world has passed its observation period. Never treat a successful upload as proof that the game is ready for players.

## 1. Decide whether the world is eligible

Make a current inventory before changing either server:

- PennMUSH version, flatfile format, database size, highest dbref, and object count;
- custom flags, powers, attributes, locks, command/function restrictions, and `mush.cnf` includes;
- channels, mail, mail aliases, player passwords, guests, and login policy, including the separate PennMUSH `maildb` and `chatdb` files;
- globals, master-room objects, HTTP and event handlers, startup attributes, and scheduled jobs;
- packages and representative softcode entry points;
- DNS, telnet/WebSocket endpoints, bots, monitoring, mail delivery, and other external integrations;
- uploaded wiki assets and any files used by plugins or operational scripts.

Create and checksum a PennMUSH flatfile, `maildb`, and `chatdb` while the source is quiescent. Copy them, the complete configuration tree, help/text files, and any other external state to storage the SharpMUSH host cannot overwrite. The importer accepts one uploaded `mush.cnf`; it does not follow that file's includes, so inventory included files and reconcile their effective settings yourself. The mail and chat inputs are optional to the importer, but omitting them intentionally imports no mail aliases/channels from those stores.

Check the destination with `@storage`. Allow space for the disposable destination, source archive, and independent backups. Portal and configured startup imports now build a private preview in `<world>.imports/<session-id>/world`; the running world, configuration, and credentials remain unchanged until a separate promotion and restart. A production-sized first rehearsal belongs on a disposable, non-public SharpMUSH installation with the same image and storage settings planned for production.

## 2. Rehearse the import

1. Start a clean SharpMUSH stack and protect `/setup` from untrusted visitors. Claim the administrator account linked to `#1`.
2. From setup, open **Import a PennMUSH, RhostMUSH or TinyMUX database** (later, **Database import** under Build & manage, at `/admin/import`). Leave the format on **Tell from the file** unless the import picks the wrong one. Upload the flatfile and the matching optional `mush.cnf`, `maildb`, and `chatdb` together. Configuration-derived object references and restrictions are applied only in the private preview before objects are converted; mail aliases, messages, channels, memberships, and history come from the ancillary databases.
3. Read every skipped-configuration warning. Reproduce settings SharpMUSH supports and record intentional exceptions.
4. Upload the flatfile and let the private preview finish. Every declared mail/chat/folder input is parsed before conversion writes begin. Failed or cancelled previews discard their private world and encryption keys, leaving the running game unchanged. Review counts, warnings, rewrites and the downloadable report. **Recent imports** recovers reports and ready previews after a page reload.
5. A successful preview still needs acceptance and a maintenance window. Select the explicit replacement/restart confirmation, then **Promote preview and restart**. The process stops; its next start installs the staged world and matching encryption keys before opening the provider. The outgoing world and keys are retained together at `<world>.previous` and `<key-ring>.previous`. Existing connections are closed and their outgoing-world bindings removed before imported-world consumers start. If your supervisor does not automatically restart a clean exit, start the server again.
6. Return to setup to choose HTTP/event handlers and bundled packages. An imported world's own handlers may already be present; do not install a second handler without understanding which attributes the setup choice moves or removes.

An import rehearsal is evidence, not authority. Do not expose its endpoints, accept player changes, or quietly promote it into the production game. Repeat the process from a new final flatfile at cutover.

The parity harness in [`tools/parity/README.md`](../../tools/parity/README.md) explains the project's automated PennMUSH comparison method. Its passing scenarios are useful compatibility evidence, but they do not replace validation of a game's custom code.

## Importing from RhostMUSH or TinyMUX

The import page and the start-up import read three formats. Each is told from the file's first lines: a TinyMUX flatfile starts `+X`, and a PennMUSH outdb and a RhostMUSH flatfile both start `+V` and differ in the line after it. If the guess is wrong, choose the format yourself: the page has a format choice, the upload API takes a `format` field (`auto`, `pennmush`, `rhostmush` or `tinymux`), and a start-up import reads `PENNMUSH_DATABASE_FORMAT` beside `PENNMUSH_DATABASE_PATH`.

The files that go with each format:

| Format | Database | Mail | Channels |
| --- | --- | --- | --- |
| PennMUSH | the outdb | `maildb` | `chatdb` |
| RhostMUSH | the flatfile `@dump/flat` writes | `.dump.mail` from `wmail/unload`, with its `.dump.folder` in the mail-folders slot | none: see below |
| TinyMUX | the flatfile (versions 1 to 5) | `mail.db` | `comsys.db` in the chat slot |

Any of these files may be gzipped, as PennMUSH writes its dumps by default (`outdb.gz`); the import tells a gzipped file from its first bytes and unpacks it, to at most 512 MiB. An upload takes up to 100 MB per file, so compress a larger one first. An import is stopped if it runs longer than ten minutes plus a minute for every 8 MiB of input.

The configuration slot takes the source's configuration file: PennMUSH's `mush.cnf`, TinyMUX's `netmux.conf` or RhostMUSH's `netrhost.conf`. A directive with the same name as a PennMUSH one is read as PennMUSH reads it. TinyMUX and RhostMUSH call some settings by other names, which the import reads as their SharpMUSH options: `player_starting_room` is `player_start`, `money_name_singular` and `money_name_plural` are `money_singular` and `money_plural`, `dig_cost`, `open_cost`, `create_min_cost` and `wait_cost` are `room_cost`, `exit_cost`, `object_cost` and `queue_cost`, `quotas` is `use_quota`, `max_players` is `max_logins`, `room_parent`, `exit_parent`, `thing_parent` and `player_parent` are the `ancestor_` options, `examine_public_attrs` is `ex_public_attribs`, `ip_address` is `ip_addr` and `conn_timeout` is `unconnected_idle_timeout`. TinyMUX's `parent_recursion_limit`, `sql_server`, `sql_user`, `port_ssl` and `ssl_certificate_key` are `max_parents`, `sql_host`, `sql_username`, `ssl_port` and `ssl_private_key_file`. The import report lists each `include` line as a warning, since an uploaded file cannot name files on the server, and the directives SharpMUSH has no option for as one note; paste an included file's lines into the uploaded file to keep them.

**What comes across.** Objects keep their numbers, with their names, owners, parents, zones, locks, attributes and creation times. Flags and powers become SharpMUSH's where one matches: a wizard becomes the `wizard` role, a RhostMUSH councilor the `royalty` role and an architect the `builder` role, and a TinyMUX power such as `control_all` becomes the permission it stands for. A flag or power with no match is kept as a custom flag or power of its own, so `hasflag()` still sees it. Passwords keep working: the forms these servers store (DES and extended DES crypt, MD5-crypt, SHA-crypt, TinyMUX's SHA-1, and a PennMUSH hash TinyMUX carried over) are checked at login and rehashed the first time each player connects. Mail comes across, in RhostMUSH's folders when the folder dump is given; TinyMUX channels come across with their members, and each player's comsys aliases become `CHANALIAS` attributes.

**What the import changes in softcode.** Colour written as `%c` or `%x` codes (`%crRed%cn`) becomes `ansi()` (`ansi(r,Red)`), because in SharpMUSH `%c` is the last command and `%x` an attribute. A RhostMUSH import also rewrites its character codes and literal `timefmt()` formats. The import turns on the compatibility options the source's softcode expects: for RhostMUSH, `paren_groups`, `mux_logic`, `tiny_trim_fun`, `rhost_edit`, `rhost_lattr`, `rhost_printf`, `rhost_map_args`, `sidefx_flag`, `rhost_list`, `rhost_iter_separator` and `rhost_lrooms`; for TinyMUX, `paren_groups`, `mux_logic`, `mux_justify`, `mux_collation`, `rhost_list`, `innermost_tokens`, `tiny_trim_fun`, `tiny_math` and `null_eq_zero`, with `function_recursion_limit` raised to 500 and `max_attribute_value_length` to 32768.

**What is left behind.** Destroyed objects, money, RhostMUSH exits that lead HOME (left unlinked, as a PennMUSH import leaves them), flags and toggles SharpMUSH has nothing for, and attribute numbers the flatfile does not name. RhostMUSH channels are not imported: a stock RhostMUSH runs them in softcode, which comes across with the objects that hold it, and a hardcoded comsys's `CHANNELS` attribute comes across as a plain attribute. A bcrypt password cannot be checked, so those players need a new password from a wizard. TinyMUX compresses runs of spaces in evaluated text and SharpMUSH does not, so output that relied on it may show extra spaces. A TinyMUX flatfile that keeps its attributes in a separate database is refused, because it holds no attribute text.

**Where the report lists it.** Every choice above is a warning in the import report: the result panel of the import page lists them all when the import ends, and a start-up import writes the first ten to the server log. Each line names what was done and the objects it touched, such as `Flag SEETHRU imported as TRANSPARENT (3 time(s), on #5 #9 #12)`. Rewritten softcode is reported apart: one warning says how many attributes were rewritten and how many need checking by hand, and the import page lists every one of them under **Rewritten softcode**, one line each, such as `#9/DESCRIBE: %c/%x colour written as ansi()`. Open a line to see the attribute's code before and after the rewrite. A line the rewrite could not carry over exactly says `check by hand` with the reason. A colour code it could not convert at all, such as one RhostMUSH builds at run time (`%c0X[printf(...)]`), is removed rather than left for SharpMUSH to misread, and its line says `colour-code-removed`: the text it coloured is plain until you colour it again with ansi(). A start-up import writes the whole list to the server log, not only the first ten. Read these lines before promoting the world, and test the attributes they name.

**The import log.** The whole report, every count, error and warning and each rewritten attribute's code before and after, is also a plain text file. The import page's **Download import log** button saves it as `sharpmush-import-<date>-<time>.log`; a start-up import writes the same file beside the imported database and names it in the server log. Keep it with the cutover records, or attach it when you ask for help with an import.

## Exporting a native Rhost database offline

The standalone [RhostExport tool](../../tools/RhostExport/README.md) supports the pinned upstream Linux x64 legacy QDBM object-cache and MDBX attribute-cache profiles. It exports a private writable copy through the matching upstream `dbconvert`, validates the portable output, and publishes a new output directory only after validation. Upload its `world.flat` and any supplied portable mail companions using the Rhost private-preview import path. Both pinned native backends are tested through export, isolated worker conversion and restart promotion.

| Source | Supported route |
| --- | --- |
| Rhost portable flatfile | Existing portal/start-up import |
| Legacy QDBM native backup | Offline bridge with checkpoint plus `.db`, `.dir`, `.pag` companions |
| MDBX native backup | Offline bridge with checkpoint plus `.mdbx` companion |
| Native mail/folder stores | Supply matching `wmail/unload` portable dumps; native mail is not decoded |
| Other historical DBM, ABI or cache profiles | Unsupported; export using that source installation first |

Use a stopped source or a complete consistent backup. Preserve originals and record source object and attribute inventories independently. The pinned upstream exporter can exit successfully despite missing attribute records; strict syntax and relationship validation cannot prove universal attribute completeness. Reconcile the exported counts and representative attributes against a healthy source before import. Raw native files are not accepted by the portal: use the supported offline bridge. The bridge cannot certify every historical storage profile or completeness against an already damaged source; preserve the independent inventory. The subsequent SharpMUSH private-preview and explicit restart-promotion protocol is separate from exporting.

## 3. Validate the rehearsed world

Record pass/fail, the command or screen used, and any accepted difference for every row.

| Domain | Minimum evidence |
| --- | --- |
| Identity and passwords | God, wizard, ordinary player, guest, aliases, failed login, password reset, and account-to-character linking behave as expected. Imported password hashes should be replaced with new passwords over time. |
| Objects and dbrefs | Object counts and highest dbref are plausible; known rooms, exits, things, and players retain the dbrefs embedded in softcode. |
| Attributes and locks | Sample inherited, private, wizard, visual, and large attributes; evaluate representative lock types and failure messages. |
| Flags and powers | Custom and built-in definitions exist, permissions are correct, and representative objects retained assignments. |
| Channels and mail | Confirm the intended `maildb` and `chatdb` were supplied. Membership, aliases, history expectations, mail folders, send/read/delete, and privacy are checked with distinct accounts. |
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
2. Drain queued work, create the final source flatfile plus matching `maildb` and `chatdb`, archive source configuration and external state, and record checksums and counts for every input.
3. Repeat the rehearsed import into a clean, isolated destination on the intended SharpMUSH host. Keep the existing world stopped and separately backed up. Do not reuse a changed rehearsal database or import into the existing world.
4. Run the abbreviated acceptance suite: administrator and player login, known dbrefs, core softcode, mail/channel samples, telnet, portal/WebSocket, integrations, backup, storage, health, logs, and metrics.
5. Change DNS/proxy/client endpoints only after the named approver signs off. Keep PennMUSH stopped but immediately recoverable.
6. Observe error rates, queues, storage, authentication, connection churn, and player reports through the agreed window. Preserve the import report and cutover log.

## 5. Roll back safely

Rollback triggers should be objective: authentication failure, widespread data mismatch, critical softcode incompatibility, unsafe storage behavior, or an unrecoverable integration failure.

To return to PennMUSH, stop accepting SharpMUSH changes, restore the old endpoint, and start the preserved PennMUSH world from the pre-cutover state. Player changes made after SharpMUSH opened are not automatically transferable; record that data-loss boundary in the announcement.

A committed import retains `<world>.previous` and the matching `<key-ring>.previous`; neither is deleted automatically. From **Recent imports**, select the committed session and explicitly confirm **Restore previous world and restart**. A rollback also closes connections and requires fresh login. Keep your independent pre-cutover backup. **Discard previous world** on the import page deletes the retained pair, and with it the rollback for the import that replaced it. If an interrupted commit blocks startup, stop the server and run the recovery command with exactly the same world and key-ring configuration, then start normally:

```bash
dotnet SharpMUSH.Server.dll --import-rollback <session-id>
```

For Compose, stop only the server first, then run its image against the same volumes/environment (the socket service need not be changed):

```bash
docker compose stop sharpmush-server
docker compose run --rm --no-deps sharpmush-server --import-rollback <session-id>
docker compose up -d sharpmush-server
```

The shipped Compose recipe calls the engine `sharpmush-server`; preserve any deployment overlays and environment settings when running these commands. The recovery command holds the engine lease and refuses to run beside an active engine. It recognizes partially completed rollback moves and retains displaced imported data inside the session directory. Failure stays closed rather than serving a mismatched world/key ring. Do not remove retained directories until the restored world and credential access have been verified. Stop the SharpMUSH stack before moving LMDB directories; never copy or read a live `data.mdb` directly. For ongoing SharpMUSH recovery, restore a point-in-time world copy made by `@backup` or a Restic snapshot containing such a copy, following the [backup and restore procedures](../../deploy/README.md#backups-restic).

Keep the source archive and pre-cutover SharpMUSH backup until acceptance and the observation window are complete. Delete superseded data only after the operator has recorded the decision and verified a separate recoverable backup.


## Configured startup imports

`PENNMUSH_DATABASE_PATH` and its existing format/companion/config variables use the same private worker. A valid first start records promotion intent and exits; the second start installs it before resolving live stores. A content receipt prevents importing the same source/configuration on every restart, including recovery from a crash after the committed journal record but before receipt cleanup. Changing any declared source or configuration changes the receipt fingerprint and requests a new preview. A retained `.previous` pair blocks another promotion until it is discarded: archive it first if you want to keep it, then use **Discard previous world** on the import page.

Imports never follow configuration includes. Imported secret values remain encrypted in the private world with its private key ring; source/configuration copies are removed when the worker finishes. Current environment and file secret overrides retain precedence after promotion. Installed plugin code/state is copied privately for preview registration, and preview message publication is disabled. This isolates ordinary plugin services and state; administrator-installed plugins remain trusted executable code, not an operating-system sandbox.
