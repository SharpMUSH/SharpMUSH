# Dependency-aware package changes: proposal for #2008

Status: root-reviewed implementation approved. The user selected improving the existing install/uninstall workflow. Retained-content deactivation is outside this feature.

## What the current code actually does

At main `d4cdcabf4cac779a6b00a37c871d2dfec0563a99`:

- `InstalledPackageRecord` has installation/version/revision data, not an enabled flag. `IPackageInstallService` implements plan/apply/uninstall/rollback, not suspension. `GameFeatureService` treats a bundled feature as on when its package is installed and required plugins run; its selection workflow installs/removes packages.
- `PluginState` and `IPluginAdministration` provide a separate persisted on/off choice. `PluginStatusDto` distinguishes the last boot, configured state and pending restart. Plugins with DI, migrations or other captured contributions cannot hot-unload.
- `PluginAdministrationService.SetEnabledAsync(false)` currently **uninstalls dependent packages**, through `RemoveDependentsAsync`. Enabling also clears declined bundled-package choices, allowing packages to return at boot. Neither side effect may occur outside a reviewed batch.
- `BundledPackagePlan.For` already expands bundled prerequisites and orders install/removal, but assumes catalogue order and silently keeps required packages. `PackagePlanService.CheckDependenciesAndConflicts` owns version/conflict/plugin checks. Registry dependency edges and manifest `plugins:` are authoritative; `PluginCandidate.Dependencies` comes from the existing loader discovery.
- `IPackageOperationRunner` enters the engine queue, holds `IPackageOperationGate`, and takes the existing pre-operation backup. Installer methods reenter that gate. Lifecycle hooks are queued after the operation; a batch must preserve that contract rather than wait for queued hooks inside the single consumer.

Sources: `SharpMUSH.Contracts/Models/Packages/{PackageRegistryRecords,PluginStatus}.cs`; `SharpMUSH.Server/Services/{GameFeatureService,PluginAdministrationService,BundledPackagePlan}.cs`; `SharpMUSH.Library/Services/{PackagePlanService,PackageInstallService.Apply,PackageInstallService.Lifecycle}.cs`; `SharpMUSH.Library/Services/Interfaces/IPackageOperationRunner.cs`.

## Approved experience

Use truthful actions: **Install**, **Uninstall**, **Turn plugin on/off**. Never rename uninstall to disable. Existing objects/attributes, application registrations, wiki ownership and package settings follow the existing uninstall rules; preview describes those effects. Scene/account/game data is not newly deleted by this feature, and no generic retained-data guarantee is invented.

Add a change queue shared by the installed, browse and plugin views. Each row shows current state, requested action, source/version, whether explicitly requested or proposed as a prerequisite, and why. Changing a row replaces that row's previous draft request; the server rejects contradictory duplicates. Removing a prerequisite from the draft exposes its dependent's blocker rather than silently re-adding it.

A simple plugin-off action without dependents or other blockers executes directly with the existing running/pending-restart wording. Uninstall retains its existing destructive confirmation, without an additional empty dependency screen. With blockers, show the affected packages/plugins and offer **Add these changes to the queue**; never silently cascade. Installing prerequisites is likewise proposed as an explicit reviewed group. There is no force-remove option in the batch.

Review shows the entire ordered effect, ordinary package review links/configure prompts/conflict decisions, code-trust and maximum-version confirmations, and any restart boundary. A failed step leaves completed steps visible and later steps unrun. Retry means a fresh plan against actual state, not replaying an old command list.

## Smallest safe server contract

Introduce a package batch coordinator around existing operations, not another install engine or dependency database. Proposed endpoints under `api/packages/batches`, all `PackagesAdmin` protected:

- `POST plan`: normalized typed install/uninstall/plugin-state requests; returns proposed steps, prerequisite reasons, blockers, source commits, current revisions/states and an opaque review token.
- `POST apply`: the reviewed token plus per-step existing decisions/confirmations; revalidates inside the operation gate before the first write. Returns a run id for polling per-step progress.
- `GET {id}`: initiator/admin-visible completed, failed, unrun and awaiting-restart statuses. A process-owned bounded run registry is sufficient initially; expired or lost runs explicitly require refresh/replan and never imply success. This is not a durable scheduled-job system.

Resolve source manifests through the current catalogue/upload/remote paths. Bind review to exact resolved commits and uploaded-file identities. Refetch and compare before execution; a moved tag, remote trust change, revision change, dependency/version change, plugin discovery/state change or permission loss invalidates review. Do not persist secret configure answers in browser storage or public progress payloads.

One dependency projection combines the existing installed records/edges, fetched manifests and discovered plugin metadata. Extend/extract the current bundled ordering logic for shared deterministic closure/order and cycle diagnostics; use the existing version-constraint/check functions. Do not maintain a parallel dependency registry or copy version semantics into the UI. Reject explicit install-A/uninstall-B when A requires B. Enable prerequisites first; remove dependents first. Cycles, missing sources, incompatible versions and unavailable handlers are actionable blockers. Unrelated existing graph problems should not prevent a simple independent removal.

Apply one reviewed batch through the package queue/gate and existing backup runner. Refactor plugin state changes so they cannot nest a second queued operation or uninstall unapproved dependents. Legacy single-plugin calls must refuse such dependents and direct callers to review; plugin-on must not clear declined package choices outside the approved plan. Existing installer/trust/permission/uninstall guards remain authoritative.

## Limits that review must not conceal

Dependency projection is not a simulated game database. A dependent's cross-package object references may not exist until a prerequisite installs. Reuse current per-step planning/apply checks: if a later step needs new conflict/configuration decisions, stop with its review link and retain earlier committed results. Do not invent future dbrefs, auto-select conflict decisions, or claim every content preflight completed before any write.

A plugin turned on may only start after restart. Do not then attempt packages whose required plugin is not running: mark remaining steps **Awaiting restart**, retain/report their intended actions, and require fresh review after restart. Restart is a separate `ServerOperate` action with its existing confirmation. Unloadable plugin removal can stop immediately; load-once plugins remain **running, off next restart**. File failures and unload refusal report actual configured/running state, not a synthetic all-or-nothing result.

## Unsupported alternative

If the user chooses **keep content installed while disabling behavior**, this proposal is insufficient and #2008 must not be claimed complete. There is no safe existing generic suspension primitive: packages can attach attributes to shared handlers, register runtime hooks/functions, install declarations/settings/wiki pages, and have arbitrary lifecycle effects. That alternative needs a separate explicit activation contract and package capability policy. Adding an `Enabled` bit or HALTing every owned object alone would not implement it.

The functional choice and root preflight review are complete. Implementation follows the approved existing-lifecycle scope.

## Reviewed execution contract

Plan requests include explicit intents and per-step configuration, conflict and consent reviews. A server step identity binds action, target kind, exact source and affected state; it excludes answers. The actor-bound opaque token owns the reviewed order and choices. Apply accepts only that token and is idempotent for the same actor/token. Accepted runs own a DI scope and host lifetime rather than an HTTP disconnect token. Active runs are not evicted. Failures inside the batch return partial results so already-collected lifecycle hooks drain after the single queue entry; committed steps remain committed.
