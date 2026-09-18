using Mediator;
using SharpMUSH.Library.Commands.Database;
using System.Text.Json;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;


namespace SharpMUSH.Library.Services;

/// <summary>Apply: executes a reviewed changeset, including the application and managed-package kinds.</summary>
public partial class PackageInstallService
{
	public async Task<Result<PackageApplyResult>> ApplyAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		CancellationToken cancellationToken = default,
		IManagedPackageBinarySource? binarySource = null)
	{
		// Managed packages (Phase 4) carry a compiled C# plugin DLL rather than
		// softcode: there is no plan/changeset to compute. Verify + trust-gate +
		// deposit the binaries, then record the install (with the deployed file
		// list) and return. The plugin loads on the next boot.
		if (manifest.Kind == PackageKind.Managed)
		{
			return await ApplyManagedAsync(manifest, request, binarySource, cancellationToken);
		}

		if (manifest.Objects.Any(o => o.Type == PackageObjectType.Player))
		{
			return new Error<string>("Packages containing player objects are not yet supported by the apply engine.");
		}

		var inputs = await GatherInputsAsync(manifest, request.ConfigureAnswers, cancellationToken);
		var changeset = planner.ComputeChangeset(inputs);

		if (changeset.IsBlocked)
		{
			return Blocked(changeset.DependencyIssues);
		}

		var decisions = request.ConflictDecisions.ToDictionary(
			d => DecisionKey(d.TargetRef, d.Attribute), d => d, StringComparer.Ordinal);
		var undecided = changeset.Attributes
			.Where(a => a.Action == PackageAttributeAction.Conflict
				&& !decisions.ContainsKey(DecisionKey(a.TargetRef, a.Attribute)))
			.Select(a => $"{a.TargetRef}/{a.Attribute}")
			.Concat(changeset.Structure
				.Where(s => s.Action == PackageStructureAction.Conflict
					&& !decisions.ContainsKey(DecisionKey(s.TargetRef, LockDecisionAttribute(s.Element))))
				.Select(s => $"{s.TargetRef}/lock:{s.Element}"))
			.ToList();
		if (undecided.Count > 0)
		{
			return new Error<string>($"Unresolved conflicts: {string.Join(", ", undecided)}");
		}

		var notes = new List<string>(changeset.Notes);
		var pmWizard = await GetPackageManagerWizardAsync(cancellationToken);

		// Pass 1: create objects so every internal ref has a dbref.
		var objidByRef = new Dictionary<string, string>(StringComparer.Ordinal);
		var created = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var change in changeset.Objects.Where(c => c.Action
			is PackageObjectAction.NoChange or PackageObjectAction.UpdateMetadata or PackageObjectAction.Rename))
		{
			objidByRef[change.Ref] = change.Objid!;
		}

		// Attach objects (decision 20.3): the target must already exist; we
		// never create it, so an unresolved target is a hard apply error.
		foreach (var change in changeset.Objects.Where(c => c.Action == PackageObjectAction.Attach))
		{
			if (change.Objid is null || await GetKnownAsync(change.Objid, cancellationToken) is null)
			{
				return new Error<string>(
					$"Attach target for {{{{{change.Ref}}}}} ({change.Name}) does not resolve to an existing object; "
					+ "configure it or check the http_handler setting before applying.");
			}

			objidByRef[change.Ref] = change.Objid;
		}

		string? Resolve(PackageRef reference) => reference switch
		{
			{ Kind: PackageRefKind.Internal, Package: not null } =>
				inputs.CrossPackageObjids.GetValueOrDefault($"{reference.Package}/{reference.Name}"),
			{ Kind: PackageRefKind.Internal } => objidByRef.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.WellKnown } => inputs.WellKnownObjids.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.Configure } => ResolveConfigure(manifest, request.ConfigureAnswers, reference.Name),
			_ => null
		};

		var manifestByRef = manifest.Objects.ToDictionary(o => o.Ref, StringComparer.Ordinal);
		foreach (var change in changeset.Objects.Where(c => c.Action
			is PackageObjectAction.Create or PackageObjectAction.RecreateMissing))
		{
			var spec = manifestByRef[change.Ref];
			switch (await CreateObjectAsync(spec, pmWizard, Resolve, notes, cancellationToken))
			{
				case string createdObjid:
					objidByRef[change.Ref] = createdObjid;
					created[change.Ref] = createdObjid;
					break;
				case Error<string> error:
					return error;
			}
		}

		// Pass 2: exits link, parents, name updates (flags/locks/powers come later).
		foreach (var spec in manifest.Objects)
		{
			var error = await ApplyObjectWiringAsync(
				spec, objidByRef[spec.Ref], created.ContainsKey(spec.Ref), changeset, Resolve, cancellationToken);
			if (error is not null)
			{
				return new Error<string>(error);
			}
		}

		// Pass 3: attributes per changeset decision.
		var preApply = new List<PackageRevisionSnapshotAttribute>();
		var finalValues = new Dictionary<(string Objid, string Attribute), string>();
		foreach (var change in changeset.Attributes)
		{
			var objid = change.Objid ?? objidByRef.GetValueOrDefault(change.TargetRef);
			if (objid is null)
			{
				return new Error<string>($"Internal error: no objid for attribute target '{change.TargetRef}'.");
			}

			var spec = manifestByRef.GetValueOrDefault(change.TargetRef);
			var attrSpec = spec?.Attributes.GetValueOrDefault(change.Attribute);
			string? newValue;
			if (attrSpec is not null)
			{
				// Code: tokens become [v(PM`REFS`...)] recalls — never dbrefs (20.21).
				newValue = PackageRefIndirection.TransformCode(attrSpec.Value, spec!.IsAttach ? manifest.Name : null);
			}
			else if (change.NewValue is not null && PackageRefIndirection.IsRefAttribute(change.Attribute))
			{
				// Engine-managed ref attr: the value IS the resolution. A token
				// that still cannot resolve here (unanswered configure) is fatal.
				newValue = PackageRefSubstitution.Substitute(change.NewValue, Resolve, out var stillUnresolved);
				if (stillUnresolved.Count > 0)
				{
					return new Error<string>(
						$"{change.TargetRef}/{change.Attribute}: ref {stillUnresolved[0]} is unresolved — answer its configure prompt before applying.");
				}
			}
			else
			{
				newValue = change.NewValue;
			}

			var error = await ApplyAttributeChangeAsync(
				manifest, change, objid, newValue, decisions, pmWizard, preApply, finalValues, notes, cancellationToken);
			if (error is not null)
			{
				return new Error<string>(error);
			}
		}

		// Pass 3.5: object structure (flags, powers, locks, attribute flags) per
		// the three-way merge — add/remove/keep-local/conflict, never additive.
		foreach (var change in changeset.Structure)
		{
			var objid = change.Objid ?? objidByRef.GetValueOrDefault(change.TargetRef);
			if (objid is null)
			{
				return new Error<string>($"Internal error: no objid for structure target '{change.TargetRef}'.");
			}

			// Lock values resolve at apply time (refs to objects created this apply
			// only get dbrefs now), exactly like attribute code — never trust the
			// plan-time NewValue for a lock write.
			var effective = change;
			if (change.Kind == PackageStructureKind.Lock
				&& change.Action is PackageStructureAction.Add or PackageStructureAction.Conflict
				&& manifestByRef.TryGetValue(change.TargetRef, out var lockSpec)
				&& LockNames.Fold(lockSpec.Locks).TryGetValue(change.Element, out var rawLock))
			{
				var resolvedLock = PackageRefSubstitution.Substitute(rawLock, Resolve, out var unresolved);
				if (unresolved.Count > 0)
				{
					return new Error<string>(
						$"{change.TargetRef}/lock:{change.Element}: ref {unresolved[0]} is unresolved — answer its configure prompt before applying.");
				}

				effective = change with { NewValue = resolvedLock };
			}

			var error = await ApplyStructureChangeAsync(effective, objid, decisions, notes, cancellationToken);
			if (error is not null)
			{
				return new Error<string>(error);
			}
		}

		// Pass 4: deletions (objects removed from the package) — @destroy convention.
		foreach (var change in changeset.Objects.Where(c => c.Action == PackageObjectAction.Delete))
		{
			await MarkGoingAsync(change.Objid!, notes, cancellationToken);
			await registry.RemovePackageObjectAsync(manifest.Name, change.Ref);
			await registry.RemoveManagedStructureAsync(manifest.Name, change.Objid!);
		}

		// Pass 5: registry — objects, renames, dependencies, package record, revision.
		foreach (var change in changeset.Objects.Where(c => c.Action == PackageObjectAction.Rename))
		{
			await registry.RemovePackageObjectAsync(manifest.Name, change.RenamedFromRef!);
		}

		// Attach objects are NOT recorded here — the package does not own them.
		// Their managed attributes live in sys_managed_attributes, which is what
		// uninstall clears (without destroying the object). Decision 20.3.
		foreach (var spec in manifest.Objects.Where(o => !o.IsAttach))
		{
			await registry.UpsertPackageObjectAsync(new PackageObjectRecord(
				manifest.Name, spec.Ref, objidByRef[spec.Ref], spec.Type.ToString().ToLowerInvariant()));
		}

		// Persist the object-structure baseline (= the resolved manifest structure)
		// for every managed object — including attach objects, which own attribute
		// flags — and capture it for the rollback snapshot. Objects with no managed
		// structure carry no row (their baseline is cleared if one lingered).
		var structureSnapshot = new List<PackageRevisionSnapshotStructure>();
		foreach (var spec in manifest.Objects)
		{
			var structObjid = objidByRef[spec.Ref];
			var resolved = BuildResolvedStructure(spec, Resolve);
			if (resolved is null)
			{
				await registry.RemoveManagedStructureAsync(manifest.Name, structObjid);
				continue;
			}

			await registry.UpsertManagedStructureAsync(new ManagedStructureRecord(
				manifest.Name, structObjid, JsonSerializer.Serialize(resolved, SnapshotJson), manifest.Version.ToString()));
			structureSnapshot.Add(new PackageRevisionSnapshotStructure(
				structObjid, resolved.Flags, resolved.Powers, resolved.Locks, resolved.AttributeFlags));
		}

		// The package record must exist BEFORE its dependency edges: graph
		// DEPENDS_ON relationship, so a fresh install would otherwise lose
		// its edges silently.
		var revision = (inputs.Installed?.CurrentRevision ?? 0) + 1;
		await registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			manifest.Name, manifest.Version.ToString(), request.Source.Repo, request.Source.Path,
			request.Source.Commit, request.Source.Branch, DateTimeOffset.UtcNow, revision));

		await registry.SetPackageDependenciesAsync(manifest.Name, manifest.Dependencies
			.Select(d => new PackageDependencyRecord(manifest.Name, d.PackageId, d.Constraint.ToString()))
			.ToList());

		var snapshot = new PackageRevisionSnapshot(
			manifest.Version.ToString(),
			manifest.Objects
				.Select(o => new PackageRevisionSnapshotObject(o.Ref, objidByRef[o.Ref], o.Type.ToString().ToLowerInvariant()))
				.ToList(),
			finalValues.Select(kv => new PackageRevisionSnapshotAttribute(kv.Key.Objid, kv.Key.Attribute, kv.Value)).ToList(),
			structureSnapshot);

		await registry.AddPackageRevisionAsync(new PackageRevisionRecord(
			manifest.Name, revision,
			inputs.Installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			manifest.Version.ToString(), request.Source.Commit,
			JsonSerializer.Serialize(snapshot, SnapshotJson),
			JsonSerializer.Serialize(request.ConfigureAnswers, SnapshotJson),
			JsonSerializer.Serialize(preApply, SnapshotJson),
			DateTimeOffset.UtcNow));
		await registry.PrunePackageRevisionsAsync(manifest.Name, request.KeepRevisions);

		// Application packages (kind: application) register a portal app instead
		// of creating objects; its string fields resolve through the same ref map
		// as objects/attributes, so {{?configure}} settings land here too.
		if (manifest is { Kind: PackageKind.Application, Application: not null })
		{
			switch (BuildRegisteredApplication(manifest.Application, manifest.Name, Resolve))
			{
				case RegisteredApplication application:
					await applications.UpsertApplicationAsync(application);
					notes.Add($"Registered application '{application.Slug}' ({application.Kind}) at /apps/{application.Slug}.");
					break;
				case Error<string> error:
					return error;
			}
		}

		// Lifecycle hooks (decision 20.x): after a successful apply, run AINSTALL on
		// a first install and AUPDATE on an upgrade so the package can self-configure
		// (@function/@hook registration, etc.). Failures are swallowed/logged by the
		// runner so a bad lifecycle script never fails the install.
		await lifecycle.RunLifecycleAsync(changeset, created, cancellationToken);

		return new PackageApplyResult(revision, created, notes);
	}

	/// <summary>
	/// Applies a <see cref="PackageKind.Managed"/> package (Phase 4): delegates to
	/// <see cref="IManagedPackageInstaller"/> to verify + trust-gate + deposit the
	/// carried plugin DLL(s), then records the install with the deployed file list
	/// and a revision. No game objects, attributes, or plan are involved. The
	/// plugin loads on the next server boot.
	/// </summary>
	private async Task<Result<PackageApplyResult>> ApplyManagedAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IManagedPackageBinarySource? binarySource,
		CancellationToken cancellationToken)
	{
		if (binarySource is null)
		{
			return new Error<string>(
				$"Managed package '{manifest.Name}' cannot be installed without a binary source to read its DLL(s) from.");
		}

		// The same dependency/conflict gate a softcode plan enforces, and it must run here: once
		// DeployAsync has written plugins/<id>/, the loader runs that code on the next boot.
		var issues = PackagePlanService.CheckDependenciesAndConflicts(manifest, await registry.GetInstalledPackagesAsync());
		if (issues.Count > 0)
		{
			return Blocked(issues);
		}

		return await managedInstaller.DeployAsync(manifest, request, binarySource, cancellationToken) switch
		{
			IReadOnlyList<string> deployed => await RecordManagedDeploymentAsync(manifest, request, deployed),
			Error<string> error => error,
		};
	}

	private static Error<string> Blocked(IEnumerable<PackageDependencyIssue> issues) =>
		new($"Plan is blocked: {string.Join("; ", issues.Select(i =>
			i.IsConflict
				? $"conflicts with installed {i.PackageId} {i.InstalledVersion}"
				: $"requires {i.PackageId} {i.Constraint}{(i.InstalledVersion is null ? " (not installed)" : $" (installed: {i.InstalledVersion})")}"))}");

	/// <summary>
	/// Records a managed package whose binaries <see cref="IManagedPackageInstaller"/> has deposited: the
	/// install row with the deployed file list, its dependencies, and a revision.
	/// </summary>
	private async Task<PackageApplyResult> RecordManagedDeploymentAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		IReadOnlyList<string> deployed)
	{
		var installed = await registry.GetInstalledPackageAsync(manifest.Name) is InstalledPackageRecord found ? found : null;
		var revision = (installed?.CurrentRevision ?? 0) + 1;

		await registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			manifest.Name, manifest.Version.ToString(), request.Source.Repo, request.Source.Path,
			request.Source.Commit, request.Source.Branch, DateTimeOffset.UtcNow, revision, deployed));

		await registry.SetPackageDependenciesAsync(manifest.Name, manifest.Dependencies
			.Select(d => new PackageDependencyRecord(manifest.Name, d.PackageId, d.Constraint.ToString()))
			.ToList());

		// A minimal revision snapshot: a managed package has no objects/attributes,
		// so history records only the version + commit it deposited.
		var snapshot = new PackageRevisionSnapshot(
			manifest.Version.ToString(), [], [], []);
		await registry.AddPackageRevisionAsync(new PackageRevisionRecord(
			manifest.Name, revision,
			installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			manifest.Version.ToString(), request.Source.Commit,
			JsonSerializer.Serialize(snapshot, SnapshotJson),
			JsonSerializer.Serialize(request.ConfigureAnswers, SnapshotJson),
			"[]",
			DateTimeOffset.UtcNow));
		await registry.PrunePackageRevisionsAsync(manifest.Name, request.KeepRevisions);

		var notes = new List<string>
		{
			$"Deposited {deployed.Count} verified binary file(s) into plugins/{manifest.Name}/. "
			+ "The plugin loads on the next server boot."
		};
		return new PackageApplyResult(revision, new Dictionary<string, string>(StringComparer.Ordinal), notes);
	}

	/// <summary>
	/// Resolves an application package's <c>application:</c> block into a
	/// <see cref="RegisteredApplication"/>, substituting refs in its string
	/// fields and parsing the role/kind/zone enums. Provenance is stamped with
	/// the package id so uninstall can reclaim it.
	/// </summary>
	private static Result<RegisteredApplication> BuildRegisteredApplication(
		PackageApplicationSpec spec, string packageId, Func<PackageRef, string?> resolve)
	{
		string? Sub(string? value) =>
			value is null ? null : PackageRefSubstitution.Substitute(value, resolve, out _);

		var roleText = Sub(spec.MinimumRole) ?? nameof(PortalRole.Player);
		if (!Enum.TryParse<PortalRole>(roleText, ignoreCase: true, out var role))
		{
			return new Error<string>(
				$"Application '{spec.Slug}': minimum_role '{roleText}' is not a valid role — answer its configure prompt or fix the manifest.");
		}

		var zones = spec.Zones
			.Select(z => Sub(z))
			.Where(z => !string.IsNullOrWhiteSpace(z))
			.Select(z => Enum.TryParse<WidgetZone>(z, ignoreCase: true, out var zone) ? zone : (WidgetZone?)null)
			.Where(z => z is not null)
			.Select(z => z!.Value)
			.ToList();

		var application = new RegisteredApplication(
			spec.Slug,
			Sub(spec.DisplayName) ?? spec.Slug,
			Sub(spec.Icon),
			Enum.Parse<ApplicationKind>(spec.Kind.ToString()),
			Sub(spec.SchemaUrl) ?? spec.SchemaUrl,
			Sub(spec.DataUrl),
			Sub(spec.SubmitRoute),
			role,
			Sub(spec.NavPlacement),
			zones.Count == 0 ? null : zones,
			spec.Order,
			packageId);

		return application;
	}
}
