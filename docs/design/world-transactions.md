# World Transactions

**Status:** Package operations are atomic by compensation (`PackageWriteTransaction`), and that is
the permanent contract. A *world transaction*, where every write of an operation lands in one
provider commit or none, is not built and not planned. This document states the contract, what a
crash leaves behind, and how an operator recovers. It does not amend `engine-data-trunk.md`.

## 0. Decision: compensation is the contract

### Why not a world transaction

- **It buys little for what it costs.** Package apply, rollback and uninstall are rare, wizard-only
  operations. They run from the portal (`PackagesController`), at startup
  (`DefaultPackagesBootstrapService`) and inside a PennMUSH import (`PennMUSHDatabaseConverter`),
  never from softcode, and each runs for as long as its package's writes take. The crash window
  is that run. Against it, a world transaction needs a world gate enforced on every
  write, a writer-thread session in `LightningWriter`, cache deferral in the Mediator behaviours,
  an outbox in `INotifyService`, the NATS publisher and `ITaskScheduler`, and amendments to
  `engine-data-trunk.md` §1 and §8, which are binding on every lane that writes.
- **No PennMUSH behaviour depends on it.** Penn has no package manager, so there is no Penn
  behaviour for a package operation to diverge from.
- **The cheap half of the value is available without it.** A package operation not interleaving
  with a queue entry needs only that the operation run as a queue entry, as HTTP handler commands
  do. A crash-time restore point needs only `@backup`, a consistent LMDB hot copy
  (`LightningWorldBackupService`).

Reopen this only on new evidence: a package operation long enough that the crash window matters
in practice, a need for crash atomicity, or a second writer path that compensation cannot cover.

### The contract

This covers softcode and application packages. Plugin packages (plugin binaries) are outside it;
see *Plugin packages* below.

An operation's writes go through `PackageWriteTransaction`, each recorded with its inverse. A
transaction comes only from the world's `IPackageWriteTransactionFactory` (plugin contract 3.0.0
removed its public constructor). The factory retains the world before it looks up the Package
Manager and holds it until the transaction closes. The installer and each package helper check that
their own services are the ones that world registered, before they read or call back, so a service
from another world is refused. The
operation commits with its last write, made straight to the registry: the revision record for an
apply or rollback, the removal of the package's rows for an uninstall. Then:

- **A failure the process survives** (an error result or an exception) replays the inverses newest
  first, which puts the world back where it started unless an inverse itself fails. For an error
  result, the error names every write that could not be reverted alongside the original failure.
  For an exception, failed compensation throws `PackageCompensationException` from disposal,
  carrying the unrestored steps; this can supersede the original unwinding exception. Successful
  compensation leaves the original exception intact. Recovery uses its own 30-second ambient
  execution budget, independent of cancelled request/entry tokens. This is a dispatch/cooperative
  policy: expiry prevents starting further inverses and reports them as unattempted, but every
  already-started inverse is awaited even beyond expiry. It is not a hard native completion SLA,
  and a noncooperative inverse can still hold the queue/gate while it settles.
- **A transaction accepts sequential writes until commit or recovery begins.** Await each forward
  write before starting another; a concurrent write or a commit while one is pending is rejected.
  Disposal stops new admissions and waits for an admitted write to finish registering its inverse
  before recovery. Every closer joins the same recovery, without replaying inverses. An implicit
  recovery failure remains observable on repeated disposal; explicit rollback returns its combined
  error and later disposal only joins. A write or inverse callback cannot await closure of its own
  transaction: that self-join is rejected rather than leaving recovery waiting indefinitely. These
  rules cover writes through the transaction; external helpers still need ownership spanning their
  source write through `Track`. They do not supply native isolation or durable settlement.
- **Package operations are serialized with each other.** Apply, uninstall, rollback and the
  profile-handler reset hold one process-wide gate (`IPackageOperationGate`) from their
  first registry read to their last write.
- **Softcode is isolated from a portal operation, other code is not.** A portal apply, rollback
  or uninstall runs as a queue entry (`IPackageOperationRunner`), so no queue entry or HTTP
  handler command runs beside it. A portal request that writes the world directly, and an
  operation run at startup or by a PennMUSH import, still can see and write over its
  intermediate state.
- **A crash part way through keeps every write made so far, and replays nothing.** Every write is
  durable on its own, and the inverses are in memory only.

### What a crash leaves behind

The writes happen in a fixed order, so the residue depends on how far the operation got:

| Operation | Order of writes | Left behind by a crash before the commit write |
|---|---|---|
| **Apply** (install or upgrade) | create objects → link exits, names, parents → attributes, with their managed-attribute rows → locks and flags → retire objects the package dropped → package-object, structure, installed-package, dependency and application rows → revision record | Objects the apply created, owned by the package-manager wizard. Until the package-object rows are written the registry does not know them, so applying again creates a second copy. Attributes on existing and attached objects hold a mix of old and new values. For an upgrade, the installed-package row may already name the new version with no revision record behind it. |
| **Rollback** | package-object rows → attributes and structure → release objects the revision did not own → installed-package row → revision record | A mix of the two revisions' values, and possibly an installed-package row naming a revision number that was never recorded. |
| **Uninstall** | clear this package's attributes on other packages' objects → halt its own objects and mark them `GOING` → remove its applications → remove its rows | The package is still listed as installed, with some of its objects `GOING` (and purged by the usual `@destroy` cycle) and some applications gone. |

An apply's lifecycle hooks (`AINSTALL`, `AUPDATE`) run only after its commit, so a crash never
runs them for a half-applied one. From the portal each runs as a queue entry of its own after the
operation's entry, never inside it.

### How an operator recovers

1. **Before a package operation on a live game, have a backup.** The portal takes one into
   `<backup path>/pre-package/` before each apply, rollback and uninstall, with its own
   retention (`SHARPMUSH_BACKUP_PACKAGE_KEEP`, default 2), so it never evicts the scheduled
   copies; it refuses the operation if the copy fails. It is a point-in-time copy that does not
   stop the game, and it is the only restore point that is exact. Elsewhere, take `@backup`.
2. **After a crash during a package operation, restore that backup.** Stop the server and put the
   backup directory back as the Lightning data directory (`SHARPMUSH_LIGHTNING_PATH`); the
   deployment steps are in `deploy/README.md`, *To restore for real*. Everything written after the
   backup is lost with the half-applied operation, exactly as a Penn crash loses everything after
   its last dump.
3. **Without a backup, repair by hand, then repeat the operation.** Repeating it on its own is not
   guaranteed to converge: a repeated install creates a second copy of the objects the first one
   created. Use the residue table above: `@destroy` the orphaned objects the apply created (owned
   by the package-manager wizard, not listed by the package), then re-run the apply, rollback or
   uninstall (`force` for an uninstall whose dependents or attachments block it).

### Plugin packages

A plugin package's apply and uninstall do not go through `PackageWriteTransaction`, and nothing
reverts them. They change files first and the registry second:

- **Apply** (`PluginPackageInstaller.DeployAsync`) writes the verified binaries into a
  `.incoming-<id>-<guid>` staging folder beside the installed plugins, replaces
  `<installed plugins>/<id>/` with it once every file is written, and then records the
  installed-package row, dependencies and revision.
- **Uninstall** deletes `<installed plugins>/<id>/` and then removes the package's rows.

A failure or crash between the two leaves the folder and the registry disagreeing: a new set of
binaries under an old registry row, or a registry row whose folder is gone. A crash while writing
leaves only a staging folder, which the loader never reads (it skips folders starting with `.`).
The plugin loader reads `<installed plugins>/<id>/` on the next start. `@backup` copies the world
only, not the plugins folder, so restoring a backup does not repair this. Both steps are safe to
repeat, since deploy replaces the whole folder and removal checks it exists. To recover, re-run
the install of the version you want, or re-run the uninstall, before relying on the plugin again.

### Isolation and restore points

- **Portal package operations run as queue entries.** The lifecycle hooks are held back while the
  operation's entry runs and then each runs as an entry of its own; nothing in the operation's
  entry waits on the queue.
- **An automatic backup precedes a portal package operation**, into its own directory with its own
  retention, so it never evicts the scheduled copies.
