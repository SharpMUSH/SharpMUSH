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

		var pmWizard = await GetPackageManagerWizardAsync(cancellationToken);
		await using var writes = BeginWrites(pmWizard);
		var run = new ApplyRun(manifest, request, inputs, changeset, decisions, pmWizard, writes);

		foreach (var change in changeset.Objects.Where(c => c.Action
			is PackageObjectAction.NoChange or PackageObjectAction.UpdateMetadata or PackageObjectAction.Rename))
		{
			run.ObjidByRef[change.Ref] = change.Objid!;
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

			run.ObjidByRef[change.Ref] = change.Objid;
		}

		// Application packages (kind: application) register a portal app instead of creating
		// objects; its string fields resolve through the same ref map, so {{?configure}} settings
		// land here too. Built before the first write: an answer can still name no role or zone.
		RegisteredApplication? application = null;
		if (manifest is { Kind: PackageKind.Application, Application: not null })
		{
			switch (BuildRegisteredApplication(manifest.Application, manifest.Name, run.Resolve))
			{
				case RegisteredApplication built:
					application = built;
					break;
				case Error<string> error:
					return error;
			}
		}

		if (await FindForeseeableFailureAsync(manifest, changeset, decisions, run.Resolve, cancellationToken) is string foreseen)
		{
			return new Error<string>(foreseen);
		}

		return await ApplyChangesetAsync(run, application, cancellationToken) switch
		{
			PackageApplyResult applied => await AfterCommitAsync(run, applied, cancellationToken),
			Error<string> error => await writes.RevertAsync(error)
		};
	}

	/// <summary>
	/// What follows a committed apply. Neither step is part of it and neither is undone; the lifecycle
	/// runner swallows and logs failures, so a bad AINSTALL or AUPDATE never fails an install.
	/// </summary>
	private async Task<PackageApplyResult> AfterCommitAsync(ApplyRun run, PackageApplyResult applied, CancellationToken cancellationToken)
	{
		await registry.PrunePackageRevisionsAsync(run.Manifest.Name, run.Request.KeepRevisions);
		await lifecycle.RunLifecycleAsync(run.Changeset, run.Created, cancellationToken);
		return applied;
	}

	/// <summary>
	/// One softcode or application apply in progress: what it was asked to do, the objids its refs
	/// resolve to so far, and what it has created, written through <see cref="Writes"/>.
	/// </summary>
	private sealed record ApplyRun(
		PackageManifest Manifest,
		PackageApplyRequest Request,
		PackagePlanInputs Inputs,
		PackageChangeset Changeset,
		Dictionary<string, PackageConflictDecision> Decisions,
		SharpPlayer PackageManager,
		PackageWriteTransaction Writes)
	{
		public Dictionary<string, PackageObjectSpec> ManifestByRef { get; } =
			Manifest.Objects.ToDictionary(o => o.Ref, StringComparer.Ordinal);

		public Dictionary<string, string> ObjidByRef { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, string> Created { get; } = new(StringComparer.Ordinal);

		public List<string> Notes { get; } = [.. Changeset.Notes];

		/// <summary>Live values of attributes this apply overwrote, for the revision's pre-apply record.</summary>
		public List<PackageRevisionSnapshotAttribute> PreApply { get; } = [];

		/// <summary>Each managed attribute's value after this apply, for the revision snapshot.</summary>
		public Dictionary<(string Objid, string Attribute), string> FinalValues { get; } = [];

		public string? Resolve(PackageRef reference) => reference switch
		{
			{ Kind: PackageRefKind.Internal, Package: not null } =>
				Inputs.CrossPackageObjids.GetValueOrDefault($"{reference.Package}/{reference.Name}"),
			{ Kind: PackageRefKind.Internal } => ObjidByRef.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.WellKnown } => Inputs.WellKnownObjids.GetValueOrDefault(reference.Name),
			{ Kind: PackageRefKind.Configure } => ResolveConfigure(Manifest, Request.ConfigureAnswers, reference.Name),
			_ => null
		};
	}

	/// <summary>The passes of an apply, in order. The last write, the revision record, commits it.</summary>
	private async Task<Result<PackageApplyResult>> ApplyChangesetAsync(
		ApplyRun run, RegisteredApplication? application, CancellationToken cancellationToken)
	{
		if (await CreateObjectsAsync(run, cancellationToken) is string createError)
		{
			return new Error<string>(createError);
		}

		if (await WireObjectsAsync(run, cancellationToken) is string wiringError)
		{
			return new Error<string>(wiringError);
		}

		if (await ApplyAttributesAsync(run, cancellationToken) is string attributeError)
		{
			return new Error<string>(attributeError);
		}

		if (await ApplyStructureAsync(run, cancellationToken) is string structureError)
		{
			return new Error<string>(structureError);
		}

		await RetireRemovedObjectsAsync(run, cancellationToken);
		return await RecordApplyAsync(run, application);
	}

	/// <summary>Pass 1: creates objects, so every internal ref has an objid.</summary>
	private async Task<string?> CreateObjectsAsync(ApplyRun run, CancellationToken cancellationToken)
	{
		foreach (var change in run.Changeset.Objects.Where(c => c.Action
			is PackageObjectAction.Create or PackageObjectAction.RecreateMissing))
		{
			switch (await CreateObjectAsync(
				run.Writes, run.ManifestByRef[change.Ref], run.PackageManager, run.Resolve, run.Notes, cancellationToken))
			{
				case string objid:
					run.ObjidByRef[change.Ref] = objid;
					run.Created[change.Ref] = objid;
					break;
				case Error<string> error:
					return error.Value;
			}
		}

		return null;
	}

	/// <summary>Pass 2: exit links, parents and renames; flags, powers and locks come in pass 4.</summary>
	private async Task<string?> WireObjectsAsync(ApplyRun run, CancellationToken cancellationToken)
	{
		foreach (var spec in run.Manifest.Objects)
		{
			if (await ApplyObjectWiringAsync(run.Writes, spec, run.ObjidByRef[spec.Ref], run.Created.ContainsKey(spec.Ref),
				run.Changeset, run.Resolve, cancellationToken) is string error)
			{
				return error;
			}
		}

		return null;
	}

	/// <summary>Pass 3: attributes, per the changeset's decision for each.</summary>
	private async Task<string?> ApplyAttributesAsync(ApplyRun run, CancellationToken cancellationToken)
	{
		foreach (var change in run.Changeset.Attributes)
		{
			var objid = change.Objid ?? run.ObjidByRef.GetValueOrDefault(change.TargetRef);
			if (objid is null)
			{
				return $"Internal error: no objid for attribute target '{change.TargetRef}'.";
			}

			var spec = run.ManifestByRef.GetValueOrDefault(change.TargetRef);
			string? newValue;
			if (spec?.Attributes.GetValueOrDefault(change.Attribute) is { } attrSpec)
			{
				// Code: tokens become [v(PM`REFS`...)] recalls — never dbrefs (20.21).
				newValue = PackageRefIndirection.TransformCode(attrSpec.Value, spec.IsAttach ? run.Manifest.Name : null);
			}
			else if (change.NewValue is not null && PackageRefIndirection.IsRefAttribute(change.Attribute))
			{
				// Engine-managed ref attr: the value IS the resolution. FindForeseeableFailureAsync
				// has already refused a token nothing resolves.
				newValue = PackageRefSubstitution.Substitute(change.NewValue, run.Resolve, out var unresolved);
				if (unresolved.Count > 0)
				{
					return UnresolvedRef(change.TargetRef, change.Attribute, unresolved[0]);
				}
			}
			else
			{
				newValue = change.NewValue;
			}

			if (await ApplyAttributeChangeAsync(run.Writes, run.Manifest, change, objid, newValue, run.Decisions,
				run.PackageManager, run.PreApply, run.FinalValues, run.Notes, cancellationToken) is string error)
			{
				return error;
			}
		}

		return null;
	}

	/// <summary>
	/// Pass 4: object structure (flags, powers, locks, attribute flags) per the three-way merge —
	/// add, remove, keep-local or conflict, never additive.
	/// </summary>
	private async Task<string?> ApplyStructureAsync(ApplyRun run, CancellationToken cancellationToken)
	{
		foreach (var change in run.Changeset.Structure)
		{
			var objid = change.Objid ?? run.ObjidByRef.GetValueOrDefault(change.TargetRef);
			if (objid is null)
			{
				return $"Internal error: no objid for structure target '{change.TargetRef}'.";
			}

			// Lock values resolve at apply time (refs to objects created this apply
			// only get dbrefs now), exactly like attribute code — never trust the
			// plan-time NewValue for a lock write.
			var effective = change;
			if (change.Kind == PackageStructureKind.Lock
				&& change.Action is PackageStructureAction.Add or PackageStructureAction.Conflict
				&& run.ManifestByRef.TryGetValue(change.TargetRef, out var lockSpec)
				&& LockNames.Fold(lockSpec.Locks).TryGetValue(change.Element, out var rawLock))
			{
				var resolvedLock = PackageRefSubstitution.Substitute(rawLock, run.Resolve, out var unresolved);
				if (unresolved.Count > 0)
				{
					return UnresolvedRef(change.TargetRef, $"lock:{change.Element}", unresolved[0]);
				}

				effective = change with { NewValue = resolvedLock };
			}

			if (await ApplyStructureChangeAsync(run.Writes, effective, objid, run.Decisions, run.Notes, cancellationToken) is string error)
			{
				return error;
			}
		}

		return null;
	}

	/// <summary>Pass 5: objects removed from the package are marked GOING, the @destroy convention.</summary>
	private async Task RetireRemovedObjectsAsync(ApplyRun run, CancellationToken cancellationToken)
	{
		foreach (var change in run.Changeset.Objects.Where(c => c.Action == PackageObjectAction.Delete))
		{
			await MarkGoingAsync(run.Writes, change.Objid!, run.Notes, cancellationToken);
			await run.Writes.RemovePackageObjectAsync(run.Manifest.Name, change.Ref);
			await run.Writes.RemoveManagedStructureAsync(run.Manifest.Name, change.Objid!);
		}
	}

	/// <summary>
	/// Pass 6: the registry — objects, structure baselines, the package row, dependencies and the
	/// application registration — and then the revision record, which commits the apply.
	/// </summary>
	private async Task<PackageApplyResult> RecordApplyAsync(ApplyRun run, RegisteredApplication? application)
	{
		var manifest = run.Manifest;
		var writes = run.Writes;

		foreach (var change in run.Changeset.Objects.Where(c => c.Action == PackageObjectAction.Rename))
		{
			await writes.RemovePackageObjectAsync(manifest.Name, change.RenamedFromRef!);
		}

		// Attach objects are NOT recorded here — the package does not own them.
		// Their managed attributes live in sys_managed_attributes, which is what
		// uninstall clears (without destroying the object). Decision 20.3.
		foreach (var spec in manifest.Objects.Where(o => !o.IsAttach))
		{
			await writes.UpsertPackageObjectAsync(new PackageObjectRecord(
				manifest.Name, spec.Ref, run.ObjidByRef[spec.Ref], spec.Type.ToString().ToLowerInvariant()));
		}

		// Persist the object-structure baseline (= the resolved manifest structure)
		// for every managed object — including attach objects, which own attribute
		// flags — and capture it for the rollback snapshot. Objects with no managed
		// structure carry no row (their baseline is cleared if one lingered).
		var structureSnapshot = new List<PackageRevisionSnapshotStructure>();
		foreach (var spec in manifest.Objects)
		{
			var structObjid = run.ObjidByRef[spec.Ref];
			if (BuildResolvedStructure(spec, run.Resolve) is not { } resolved)
			{
				await writes.RemoveManagedStructureAsync(manifest.Name, structObjid);
				continue;
			}

			await writes.UpsertManagedStructureAsync(new ManagedStructureRecord(
				manifest.Name, structObjid, JsonSerializer.Serialize(resolved, SnapshotJson), manifest.Version.ToString()));
			structureSnapshot.Add(new PackageRevisionSnapshotStructure(
				structObjid, resolved.Flags, resolved.Powers, resolved.Locks, resolved.AttributeFlags));
		}

		// The package record must exist BEFORE its dependency edges: graph
		// DEPENDS_ON relationship, so a fresh install would otherwise lose
		// its edges silently.
		var revision = (run.Inputs.Installed?.CurrentRevision ?? 0) + 1;
		var source = run.Request.Source;
		await writes.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			manifest.Name, manifest.Version.ToString(), source.Repo, source.Path,
			source.Commit, source.Branch, DateTimeOffset.UtcNow, revision));

		await writes.SetPackageDependenciesAsync(manifest.Name, manifest.Dependencies
			.Select(d => new PackageDependencyRecord(manifest.Name, d.PackageId, d.Constraint.ToString()))
			.ToList());

		if (application is not null)
		{
			await writes.UpsertApplicationAsync(application);
			run.Notes.Add($"Registered application '{application.Slug}' ({application.Kind}) at /apps/{application.Slug}.");
		}

		var snapshot = new PackageRevisionSnapshot(
			manifest.Version.ToString(),
			manifest.Objects
				.Select(o => new PackageRevisionSnapshotObject(o.Ref, run.ObjidByRef[o.Ref], o.Type.ToString().ToLowerInvariant(),
					o.IsAttach ? PackageObjectRelation.Attached : PackageObjectRelation.Owned))
				.ToList(),
			run.FinalValues.Select(kv => new PackageRevisionSnapshotAttribute(kv.Key.Objid, kv.Key.Attribute, kv.Value)).ToList(),
			structureSnapshot);

		await registry.AddPackageRevisionAsync(new PackageRevisionRecord(
			manifest.Name, revision,
			run.Inputs.Installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			manifest.Version.ToString(), source.Commit,
			JsonSerializer.Serialize(snapshot, SnapshotJson),
			JsonSerializer.Serialize(run.Request.ConfigureAnswers, SnapshotJson),
			JsonSerializer.Serialize(run.PreApply, SnapshotJson),
			DateTimeOffset.UtcNow));
		writes.Commit();

		return new PackageApplyResult(revision, run.Created, run.Notes);
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
		// Enum.TryParse accepts any integer, so "99" would otherwise register a role that does not exist.
		if (!Enum.TryParse<PortalRole>(roleText, ignoreCase: true, out var role) || !Enum.IsDefined(role))
		{
			return new Error<string>(
				$"Application '{spec.Slug}': minimum_role '{roleText}' is not a valid role — answer its configure prompt or fix the manifest.");
		}

		var zones = new List<WidgetZone>();
		foreach (var zoneText in spec.Zones.Select(Sub).Where(z => !string.IsNullOrWhiteSpace(z)))
		{
			if (!Enum.TryParse<WidgetZone>(zoneText, ignoreCase: true, out var zone) || !Enum.IsDefined(zone))
			{
				return new Error<string>(
					$"Application '{spec.Slug}': zone '{zoneText}' is not a valid widget zone — answer its configure prompt or fix the manifest.");
			}

			zones.Add(zone);
		}

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
