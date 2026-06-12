using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Package install orchestration (decisions 20.2, 20.7, 20.13): gathers live
/// state for the pure plan engine, executes reviewed changesets, and records
/// baselines plus revision snapshots. All created objects are owned by the
/// Package Manager wizard (config <c>package_manager</c>, default #3).
/// </summary>
public class PackageInstallService(
	ISharpDatabase database,
	IPackageRegistryService registry,
	IPackagePlanService planner,
	IOptionsWrapper<SharpMUSHOptions> configuration) : IPackageInstallService
{
	private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

	// ── Plan ─────────────────────────────────────────────────────────────────

	public async Task<PackageChangeset> PlanAsync(
		PackageManifest manifest,
		IReadOnlyDictionary<string, string>? configureAnswers = null,
		CancellationToken cancellationToken = default)
	{
		var inputs = await GatherInputsAsync(manifest, configureAnswers ?? new Dictionary<string, string>(), cancellationToken);
		return planner.ComputeChangeset(inputs);
	}

	private async Task<PackagePlanInputs> GatherInputsAsync(
		PackageManifest manifest,
		IReadOnlyDictionary<string, string> configureAnswers,
		CancellationToken cancellationToken)
	{
		var installedResult = await registry.GetInstalledPackageAsync(manifest.Name);
		var installed = installedResult.IsT0 ? installedResult.AsT0 : null;
		var installedObjects = await registry.GetPackageObjectsAsync(manifest.Name);
		var baselines = await registry.GetManagedAttributesAsync(manifest.Name);
		var allInstalled = await registry.GetInstalledPackagesAsync();

		var otherManaged = new List<ManagedAttributeRecord>();
		foreach (var package in allInstalled.Where(p => p.Id != manifest.Name))
		{
			otherManaged.AddRange(await registry.GetManagedAttributesAsync(package.Id));
		}

		var crossPackageObjids = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var dependency in manifest.Dependencies)
		{
			foreach (var record in await registry.GetPackageObjectsAsync(dependency.PackageId))
			{
				crossPackageObjids[$"{dependency.PackageId}/{record.Ref}"] = record.Objid;
			}
		}

		var live = await GatherLiveStateAsync(manifest, installedObjects, baselines, cancellationToken);

		return new PackagePlanInputs(
			manifest, installed, installedObjects, baselines, allInstalled, otherManaged, live,
			await BuildWellKnownMapAsync(cancellationToken), configureAnswers, crossPackageObjids);
	}

	private async Task<LivePackageState> GatherLiveStateAsync(
		PackageManifest manifest,
		IReadOnlyList<PackageObjectRecord> installedObjects,
		IReadOnlyList<ManagedAttributeRecord> baselines,
		CancellationToken cancellationToken)
	{
		// Attributes of interest per objid: everything this package manages
		// plus everything the manifest would set on already-installed objects.
		var attrsByObjid = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		void Want(string objid, string attribute)
		{
			if (!attrsByObjid.TryGetValue(objid, out var set))
			{
				attrsByObjid[objid] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			set.Add(attribute);
		}

		foreach (var baseline in baselines)
		{
			Want(baseline.Objid, baseline.Attribute);
		}

		var installedByRef = installedObjects.ToDictionary(o => o.Ref, StringComparer.Ordinal);
		var orphanObjids = installedObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);
		foreach (var obj in manifest.Objects)
		{
			var record = installedByRef.GetValueOrDefault(obj.Ref)
				?? obj.PreviousRefs.Select(r => installedByRef.GetValueOrDefault(r)).FirstOrDefault(r => r is not null);
			if (record is null)
			{
				continue;
			}

			orphanObjids.Remove(record.Objid);
			foreach (var attrName in obj.Attributes.Keys)
			{
				Want(record.Objid, attrName);
			}
		}

		var states = new Dictionary<string, LiveObjectState>(StringComparer.Ordinal);
		foreach (var objid in installedObjects.Select(o => o.Objid).Concat(baselines.Select(b => b.Objid)).Distinct())
		{
			states[objid] = await ReadLiveObjectAsync(
				objid,
				attrsByObjid.GetValueOrDefault(objid) ?? [],
				checkContents: orphanObjids.Contains(objid),
				cancellationToken);
		}

		return new LivePackageState(states);
	}

	private async Task<LiveObjectState> ReadLiveObjectAsync(
		string objid, IReadOnlyCollection<string> attributes, bool checkContents, CancellationToken cancellationToken)
	{
		var dbref = ParseObjid(objid);
		if (dbref is null)
		{
			return new LiveObjectState(objid, false, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}

		var node = await database.GetObjectNodeAsync(dbref.Value, cancellationToken);
		if (node.IsNone())
		{
			return new LiveObjectState(objid, false, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}

		var known = node.Known();
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var attribute in attributes)
		{
			var leaf = await database
				.GetAttributeAsync(dbref.Value, attribute.Split('`'), cancellationToken)
				.LastOrDefaultAsync(cancellationToken);
			if (leaf is not null)
			{
				values[attribute] = leaf.Value.ToPlainText();
			}
		}

		var hasContents = checkContents
			&& await database.GetContentsAsync(dbref.Value, cancellationToken).AnyAsync(cancellationToken);

		return new LiveObjectState(objid, true, known.Object().Name, values, hasContents);
	}

	private async Task<IReadOnlyDictionary<string, string>> BuildWellKnownMapAsync(CancellationToken cancellationToken)
	{
		var options = configuration.CurrentValue.Database;
		var map = new Dictionary<string, string>(StringComparer.Ordinal);

		async Task AddAsync(string name, uint? number, uint fallback)
		{
			var node = await database.GetObjectNodeAsync(new DBRef((int)(number ?? fallback)), cancellationToken);
			if (!node.IsNone())
			{
				map[name] = node.Known().Object().DBRef.ToString();
			}
		}

		await AddAsync(WellKnownRefs.RoomZero, 0, 0);
		await AddAsync(WellKnownRefs.God, 1, 1);
		await AddAsync(WellKnownRefs.MasterRoom, options.MasterRoom, 2);
		await AddAsync(WellKnownRefs.PlayerStart, options.PlayerStart, 0);
		await AddAsync(WellKnownRefs.PackageManager, options.PackageManager, 3);
		return map;
	}

	// ── Apply ────────────────────────────────────────────────────────────────

	public async Task<OneOf<PackageApplyResult, Error<string>>> ApplyAsync(
		PackageManifest manifest,
		PackageApplyRequest request,
		CancellationToken cancellationToken = default)
	{
		if (manifest.Objects.Any(o => o.Type == PackageObjectType.Player))
		{
			return new Error<string>("Packages containing player objects are not yet supported by the apply engine.");
		}

		var inputs = await GatherInputsAsync(manifest, request.ConfigureAnswers, cancellationToken);
		var changeset = planner.ComputeChangeset(inputs);

		if (changeset.IsBlocked)
		{
			var blockers = string.Join("; ", changeset.DependencyIssues.Select(i =>
				i.IsConflict
					? $"conflicts with installed {i.PackageId} {i.InstalledVersion}"
					: $"requires {i.PackageId} {i.Constraint}{(i.InstalledVersion is null ? " (not installed)" : $" (installed: {i.InstalledVersion})")}"));
			return new Error<string>($"Plan is blocked: {blockers}");
		}

		var decisions = request.ConflictDecisions.ToDictionary(
			d => DecisionKey(d.TargetRef, d.Attribute), d => d, StringComparer.Ordinal);
		var undecided = changeset.Attributes
			.Where(a => a.Action == PackageAttributeAction.Conflict
				&& !decisions.ContainsKey(DecisionKey(a.TargetRef, a.Attribute)))
			.ToList();
		if (undecided.Count > 0)
		{
			return new Error<string>(
				$"Unresolved conflicts: {string.Join(", ", undecided.Select(a => $"{a.TargetRef}/{a.Attribute}"))}");
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
			var createdRef = await CreateObjectAsync(spec, pmWizard, Resolve, notes, cancellationToken);
			if (createdRef.IsT1)
			{
				return createdRef.AsT1;
			}

			objidByRef[change.Ref] = createdRef.AsT0;
			created[change.Ref] = createdRef.AsT0;
		}

		// Pass 2: exits link, parents, names, flags, locks.
		foreach (var spec in manifest.Objects)
		{
			var error = await ApplyObjectWiringAsync(
				spec, objidByRef[spec.Ref], created.ContainsKey(spec.Ref), changeset, Resolve, notes, cancellationToken);
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
			var newValue = attrSpec is null
				? change.NewValue
				: SubstituteFully(attrSpec.Value, Resolve, change.TargetRef, change.Attribute, notes);

			var error = await ApplyAttributeChangeAsync(
				manifest, change, objid, newValue, decisions, pmWizard, preApply, finalValues, notes, cancellationToken);
			if (error is not null)
			{
				return new Error<string>(error);
			}

			if (attrSpec is not null && attrSpec.Flags.Count > 0
				&& change.Action is not (PackageAttributeAction.Delete or PackageAttributeAction.RemoveBaseline))
			{
				await ApplyAttributeFlagsAsync(objid, change.Attribute, attrSpec.Flags, notes, cancellationToken);
			}
		}

		// Pass 4: deletions (objects removed from the package) — @destroy convention.
		foreach (var change in changeset.Objects.Where(c => c.Action == PackageObjectAction.Delete))
		{
			await MarkGoingAsync(change.Objid!, notes, cancellationToken);
			await registry.RemovePackageObjectAsync(manifest.Name, change.Ref);
		}

		// Pass 5: registry — objects, renames, dependencies, package record, revision.
		foreach (var change in changeset.Objects.Where(c => c.Action == PackageObjectAction.Rename))
		{
			await registry.RemovePackageObjectAsync(manifest.Name, change.RenamedFromRef!);
		}

		foreach (var spec in manifest.Objects)
		{
			await registry.UpsertPackageObjectAsync(new PackageObjectRecord(
				manifest.Name, spec.Ref, objidByRef[spec.Ref], spec.Type.ToString().ToLowerInvariant()));
		}

		await registry.SetPackageDependenciesAsync(manifest.Name, manifest.Dependencies
			.Select(d => new PackageDependencyRecord(manifest.Name, d.PackageId, d.Constraint.ToString()))
			.ToList());

		var revision = (inputs.Installed?.CurrentRevision ?? 0) + 1;
		await registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			manifest.Name, manifest.Version.ToString(), request.Source.Repo, request.Source.Path,
			request.Source.Commit, request.Source.Branch, DateTimeOffset.UtcNow, revision));

		var snapshot = new PackageRevisionSnapshot(
			manifest.Version.ToString(),
			manifest.Objects
				.Select(o => new PackageRevisionSnapshotObject(o.Ref, objidByRef[o.Ref], o.Type.ToString().ToLowerInvariant()))
				.ToList(),
			finalValues.Select(kv => new PackageRevisionSnapshotAttribute(kv.Key.Objid, kv.Key.Attribute, kv.Value)).ToList());

		await registry.AddPackageRevisionAsync(new PackageRevisionRecord(
			manifest.Name, revision,
			inputs.Installed is null ? PackageRevisionKind.Install : PackageRevisionKind.Upgrade,
			manifest.Version.ToString(), request.Source.Commit,
			JsonSerializer.Serialize(snapshot, SnapshotJson),
			JsonSerializer.Serialize(request.ConfigureAnswers, SnapshotJson),
			JsonSerializer.Serialize(preApply, SnapshotJson),
			DateTimeOffset.UtcNow));
		await registry.PrunePackageRevisionsAsync(manifest.Name, request.KeepRevisions);

		return new PackageApplyResult(revision, created, notes);
	}

	private async Task<OneOf<string, Error<string>>> CreateObjectAsync(
		PackageObjectSpec spec,
		SharpPlayer pmWizard,
		Func<PackageRef, string?> resolve,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		var pmContainer = ToContainer(pmWizard);
		DBRef createdDbref;
		switch (spec.Type)
		{
			case PackageObjectType.Room:
				createdDbref = await database.CreateRoomAsync(PrimaryName(spec.Name), pmWizard, cancellationToken);
				break;
			case PackageObjectType.Thing:
			{
				var location = await ResolveContainerAsync(spec.Location, resolve, cancellationToken) ?? pmContainer;
				createdDbref = await database.CreateThingAsync(
					PrimaryName(spec.Name), location, pmWizard, location, cancellationToken);
				break;
			}
			case PackageObjectType.Exit:
			{
				var location = await ResolveContainerAsync(spec.Location, resolve, cancellationToken);
				if (location is null)
				{
					return new Error<string>($"Exit {{{{{spec.Ref}}}}}: source room is not resolvable.");
				}

				var parts = spec.Name.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				createdDbref = await database.CreateExitAsync(
					parts[0], parts.Skip(1).ToArray(), location, pmWizard, cancellationToken);
				break;
			}
			default:
				return new Error<string>($"Object type '{spec.Type}' is not supported by the apply engine.");
		}

		var node = await database.GetObjectNodeAsync(createdDbref, cancellationToken);
		var objid = node.Known().Object().DBRef.ToString();
		notes.Add($"Created {spec.Type.ToString().ToLowerInvariant()} {{{{{spec.Ref}}}}} as {objid}.");
		return objid;
	}

	private async Task<string?> ApplyObjectWiringAsync(
		PackageObjectSpec spec,
		string objid,
		bool isNew,
		PackageChangeset changeset,
		Func<PackageRef, string?> resolve,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return $"Internal error: object {{{{{spec.Ref}}}}} ({objid}) vanished during apply.";
		}

		// Exit destination.
		if (spec.Type == PackageObjectType.Exit && isNew)
		{
			var destination = await ResolveContainerAsync(spec.Destination, resolve, cancellationToken);
			if (destination is null)
			{
				return $"Exit {{{{{spec.Ref}}}}}: destination is not resolvable.";
			}

			await database.LinkExitAsync(node.Match(_ => null!, _ => null!, exit => exit, _ => null!),
				destination, cancellationToken);
		}

		// Name updates for metadata drift.
		var metadataChange = changeset.Objects.FirstOrDefault(c =>
			c.Ref == spec.Ref && c.Action == PackageObjectAction.UpdateMetadata);
		if (metadataChange is not null)
		{
			await database.SetObjectName(node, MModule.single(spec.Name), cancellationToken);
		}

		// Parent.
		if (spec.Parent is not null)
		{
			var parentObjid = resolve(spec.Parent);
			var parentNode = parentObjid is null ? null : await GetKnownAsync(parentObjid, cancellationToken);
			if (parentNode is null)
			{
				return $"Object {{{{{spec.Ref}}}}}: parent {spec.Parent} is not resolvable.";
			}

			await database.SetObjectParent(node, parentNode, cancellationToken);
		}

		// Flags.
		foreach (var flagName in spec.Flags)
		{
			var flag = await database.GetObjectFlagAsync(flagName.ToUpperInvariant(), cancellationToken)
				?? await database.GetObjectFlagAsync(flagName, cancellationToken);
			if (flag is null)
			{
				notes.Add($"Object {{{{{spec.Ref}}}}}: unknown flag '{flagName}' skipped.");
				continue;
			}

			await database.SetObjectFlagAsync(node, flag, cancellationToken);
		}

		// Locks (values may contain refs).
		foreach (var (lockName, lockValue) in spec.Locks)
		{
			var resolved = SubstituteFully(lockValue, resolve, spec.Ref, $"lock:{lockName}", notes);
			await database.SetLockAsync(node.Object(), lockName, new SharpLockData { LockString = resolved },
				cancellationToken);
		}

		return null;
	}

	private async Task<string?> ApplyAttributeChangeAsync(
		PackageManifest manifest,
		PackageAttributeChange change,
		string objid,
		string? newValue,
		Dictionary<string, PackageConflictDecision> decisions,
		SharpPlayer pmWizard,
		List<PackageRevisionSnapshotAttribute> preApply,
		Dictionary<(string Objid, string Attribute), string> finalValues,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		var dbref = ParseObjid(objid);
		if (dbref is null)
		{
			return $"Internal error: invalid objid '{objid}'.";
		}

		var path = change.Attribute.Split('`');

		async Task WriteAsync(string value)
		{
			if (change.LiveValue is not null)
			{
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue));
			}

			await database.SetAttributeAsync(dbref.Value, path, MModule.single(value), pmWizard, cancellationToken);
		}

		async Task BaselineAsync(string packageValue, string? effectiveValue)
		{
			await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				manifest.Name, objid, change.Attribute.ToUpperInvariant(),
				packageValue, Hash(packageValue), manifest.Version.ToString()));
			if (effectiveValue is not null)
			{
				// Null = the attribute does not exist live (a preserved local
				// deletion); it must not enter the rollback snapshot.
				finalValues[(objid, change.Attribute.ToUpperInvariant())] = effectiveValue;
			}
		}

		switch (change.Action)
		{
			case PackageAttributeAction.Create:
			case PackageAttributeAction.AutoUpgrade:
				await WriteAsync(newValue!);
				await BaselineAsync(newValue!, newValue!);
				return null;

			case PackageAttributeAction.NoChange:
			case PackageAttributeAction.Adopt:
			case PackageAttributeAction.KeepLocal:
				// No write; the baseline still advances to the package's value
				// (dpkg semantics: local drift stays visible, no re-prompting).
				// A preserved local deletion (LiveValue null) stays deleted.
				await BaselineAsync(newValue!, change.LiveValue ?? (change.Action == PackageAttributeAction.KeepLocal ? null : newValue));
				return null;

			case PackageAttributeAction.Delete:
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue!));
				await database.ClearAttributeAsync(dbref.Value, path, cancellationToken);
				await registry.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
				return null;

			case PackageAttributeAction.RemoveBaseline:
				await registry.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
				return null;

			case PackageAttributeAction.Conflict:
			{
				var decision = decisions[DecisionKey(change.TargetRef, change.Attribute)];
				switch (decision.Resolution)
				{
					case PackageConflictResolution.TakeTheirs when change.Conflict == PackageConflictKind.ModifyDelete:
						// "Theirs" is the deletion.
						preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue!));
						await database.ClearAttributeAsync(dbref.Value, path, cancellationToken);
						await registry.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
						return null;
					case PackageConflictResolution.TakeTheirs:
						await WriteAsync(newValue!);
						await BaselineAsync(newValue!, newValue!);
						return null;
					case PackageConflictResolution.UseCustom when decision.CustomValue is not null:
						await WriteAsync(decision.CustomValue);
						await BaselineAsync(newValue ?? decision.CustomValue, decision.CustomValue);
						return null;
					case PackageConflictResolution.UseCustom:
						return $"Conflict {change.TargetRef}/{change.Attribute}: UseCustom requires a value.";
					default: // KeepMine
						if (change.Conflict == PackageConflictKind.ModifyDelete)
						{
							// Keep the local value; the package no longer manages it.
							await registry.RemoveManagedAttributeAsync(manifest.Name, objid, change.Attribute.ToUpperInvariant());
							notes.Add($"{change.TargetRef}/{change.Attribute}: kept local value; no longer package-managed.");
							return null;
						}

						// DeleteModify + KeepMine keeps the deletion: baseline advances, nothing live.
						await BaselineAsync(newValue!, change.LiveValue);
						return null;
				}
			}

			default:
				return $"Internal error: unhandled attribute action {change.Action}.";
		}
	}

	private async Task ApplyAttributeFlagsAsync(
		string objid, string attribute, IReadOnlyList<string> flags, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return;
		}

		foreach (var flagName in flags)
		{
			var flag = await database.GetAttributeFlagAsync(flagName.ToUpperInvariant(), cancellationToken)
				?? await database.GetAttributeFlagAsync(flagName, cancellationToken);
			if (flag is null)
			{
				notes.Add($"{objid}/{attribute}: unknown attribute flag '{flagName}' skipped.");
				continue;
			}

			await database.SetAttributeFlagAsync(node.Object(), attribute.Split('`'), flag, cancellationToken);
		}
	}

	// ── Uninstall ────────────────────────────────────────────────────────────

	public async Task<OneOf<Success, Error<string>>> UninstallAsync(
		string packageId, bool force = false, CancellationToken cancellationToken = default)
	{
		var installed = await registry.GetInstalledPackageAsync(packageId);
		if (installed.IsT1)
		{
			return new Error<string>($"'{packageId}' is not installed.");
		}

		var dependents = await registry.GetPackageDependentsAsync(packageId);
		if (dependents.Count > 0 && !force)
		{
			return new Error<string>(
				$"Cannot uninstall '{packageId}': {string.Join(", ", dependents.Select(d => d.PackageId))} depend(s) on it. Uninstall them first or force-remove.");
		}

		var notes = new List<string>();
		var ownObjects = await registry.GetPackageObjectsAsync(packageId);
		var ownObjids = ownObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);

		// Managed attrs on objects this package does NOT own (cross-package): clear them.
		foreach (var managed in (await registry.GetManagedAttributesAsync(packageId))
			.Where(m => !ownObjids.Contains(m.Objid)))
		{
			var dbref = ParseObjid(managed.Objid);
			if (dbref is not null)
			{
				await database.ClearAttributeAsync(dbref.Value, managed.Attribute.Split('`'), cancellationToken);
			}
		}

		foreach (var record in ownObjects)
		{
			await MarkGoingAsync(record.Objid, notes, cancellationToken);
		}

		await registry.RemoveInstalledPackageAsync(packageId);
		return new Success();
	}

	// ── Rollback ─────────────────────────────────────────────────────────────

	public async Task<OneOf<PackageRollbackResult, Error<string>>> RollbackAsync(
		string packageId, int revision, CancellationToken cancellationToken = default)
	{
		var installedResult = await registry.GetInstalledPackageAsync(packageId);
		if (installedResult.IsT1)
		{
			return new Error<string>($"'{packageId}' is not installed.");
		}

		var revisionResult = await registry.GetPackageRevisionAsync(packageId, revision);
		if (revisionResult.IsT1)
		{
			return new Error<string>($"'{packageId}' has no revision {revision}.");
		}

		var record = revisionResult.AsT0;
		var snapshot = JsonSerializer.Deserialize<PackageRevisionSnapshot>(record.ManifestSnapshotJson, SnapshotJson);
		if (snapshot is null)
		{
			return new Error<string>($"Revision {revision} has no usable snapshot.");
		}

		var notes = new List<string>();
		var pmWizard = await GetPackageManagerWizardAsync(cancellationToken);
		var restoredKeys = new HashSet<(string, string)>();

		foreach (var attribute in snapshot.Attributes)
		{
			var dbref = ParseObjid(attribute.Objid);
			if (dbref is null || (await database.GetObjectNodeAsync(dbref.Value, cancellationToken)).IsNone())
			{
				notes.Add($"Skipped {attribute.Objid}/{attribute.Attribute}: object no longer exists.");
				continue;
			}

			await database.SetAttributeAsync(
				dbref.Value, attribute.Attribute.Split('`'), MModule.single(attribute.Value), pmWizard, cancellationToken);
			await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				packageId, attribute.Objid, attribute.Attribute.ToUpperInvariant(),
				attribute.Value, Hash(attribute.Value), snapshot.Version));
			restoredKeys.Add((attribute.Objid, attribute.Attribute.ToUpperInvariant()));
		}

		// Attributes managed now but absent from the snapshot: remove to match the old state.
		foreach (var managed in (await registry.GetManagedAttributesAsync(packageId))
			.Where(m => !restoredKeys.Contains((m.Objid, m.Attribute.ToUpperInvariant()))))
		{
			var dbref = ParseObjid(managed.Objid);
			if (dbref is not null)
			{
				await database.ClearAttributeAsync(dbref.Value, managed.Attribute.Split('`'), cancellationToken);
			}

			await registry.RemoveManagedAttributeAsync(packageId, managed.Objid, managed.Attribute);
			notes.Add($"Removed {managed.Objid}/{managed.Attribute} (not present in revision {revision}).");
		}

		var installed = installedResult.AsT0;
		var newRevision = installed.CurrentRevision + 1;
		await registry.UpsertInstalledPackageAsync(installed with
		{
			Version = snapshot.Version,
			CurrentRevision = newRevision,
			InstalledAt = DateTimeOffset.UtcNow
		});
		await registry.AddPackageRevisionAsync(record with
		{
			Revision = newRevision,
			Kind = PackageRevisionKind.Rollback,
			AppliedAt = DateTimeOffset.UtcNow
		});

		return new PackageRollbackResult(newRevision, revision, notes);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private async Task MarkGoingAsync(string objid, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return;
		}

		var going = await database.GetObjectFlagAsync("GOING", cancellationToken);
		if (going is null)
		{
			notes.Add($"{objid}: GOING flag unavailable; object left in place.");
			return;
		}

		await database.SetObjectFlagAsync(node, going, cancellationToken);
		notes.Add($"{objid}: marked GOING for garbage collection.");
	}

	private async Task<AnySharpObject?> GetKnownAsync(string objid, CancellationToken cancellationToken)
	{
		var dbref = ParseObjid(objid);
		if (dbref is null)
		{
			return null;
		}

		var node = await database.GetObjectNodeAsync(dbref.Value, cancellationToken);
		return node.IsNone() ? null : node.Known();
	}

	private async Task<AnySharpContainer?> ResolveContainerAsync(
		PackageRef? reference, Func<PackageRef, string?> resolve, CancellationToken cancellationToken)
	{
		if (reference is null)
		{
			return null;
		}

		var objid = resolve(reference);
		var node = objid is null ? null : await GetKnownAsync(objid, cancellationToken);
		return node?.Match<AnySharpContainer?>(
			player => player,
			room => room,
			_ => null,
			thing => thing);
	}

	private async Task<SharpPlayer> GetPackageManagerWizardAsync(CancellationToken cancellationToken)
	{
		var number = (int)(configuration.CurrentValue.Database.PackageManager ?? 3);
		var node = await database.GetObjectNodeAsync(new DBRef(number), cancellationToken);
		if (node.IsNone())
		{
			throw new InvalidOperationException($"Package Manager wizard #{number} does not exist.");
		}

		return node.Known().Match(
			player => player,
			_ => throw new InvalidOperationException($"#{number} is not a player."),
			_ => throw new InvalidOperationException($"#{number} is not a player."),
			_ => throw new InvalidOperationException($"#{number} is not a player."));
	}

	private static AnySharpContainer ToContainer(SharpPlayer player) => player;

	private static string SubstituteFully(
		string value, Func<PackageRef, string?> resolve, string targetRef, string context, List<string> notes)
	{
		var substituted = PackageRefSubstitution.Substitute(value, resolve, out var unresolved);
		foreach (var reference in unresolved)
		{
			notes.Add($"{targetRef}/{context}: ref {reference} could not be resolved; left verbatim.");
		}

		return substituted;
	}

	private static string? ResolveConfigure(
		PackageManifest manifest, IReadOnlyDictionary<string, string> answers, string key)
	{
		if (answers.TryGetValue(key, out var answer))
		{
			return answer;
		}

		return manifest.Configure.GetValueOrDefault(key)?.Default;
	}

	public static DBRef? ParseObjid(string objid)
	{
		if (objid.Length < 2 || objid[0] != '#')
		{
			return null;
		}

		var parts = objid[1..].Split(':', 2);
		if (!int.TryParse(parts[0], out var number))
		{
			return null;
		}

		return parts.Length == 2 && long.TryParse(parts[1], out var milliseconds)
			? new DBRef(number, milliseconds)
			: new DBRef(number);
	}

	private static string Hash(string value) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

	private static string PrimaryName(string name) =>
		name.Split(';', 2, StringSplitOptions.TrimEntries)[0];

	private static string DecisionKey(string targetRef, string attribute) =>
		$"{targetRef} {attribute.ToUpperInvariant()}";
}
