using Mediator;
using SharpMUSH.Library.Commands.Database;
using System.Text.Json;
using OneOf;
using OneOf.Types;
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

/// <summary>
/// Package install orchestration (decisions 20.2, 20.7, 20.13): gathers live
/// state for the pure plan engine, executes reviewed changesets, and records
/// baselines plus revision snapshots. All created objects are owned by the
/// Package Manager wizard (config <c>package_manager</c>, default #3).
/// </summary>
public class PackageInstallService(
	IObjectStore database,
	IAttributeStore attributeStore,
	IFlagAndPowerStore flags,
	INavigationStore navigation,
	IPackageRegistryService registry,
	IApplicationRegistryService applications,
	IPackagePlanService planner,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IPackageLifecycleRunner lifecycle,
	IManagedPackageInstaller managedInstaller,
	IMediator mediator) : IPackageInstallService
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

		var structureBaselines = DeserializeStructureBaselines(await registry.GetManagedStructuresAsync(manifest.Name));

		var wellKnown = await BuildWellKnownMapAsync(cancellationToken);
		var live = await GatherLiveStateAsync(
			manifest, installedObjects, baselines, wellKnown, configureAnswers, crossPackageObjids, cancellationToken);

		return new PackagePlanInputs(
			manifest, installed, installedObjects, baselines, allInstalled, otherManaged, live,
			wellKnown, configureAnswers, crossPackageObjids, structureBaselines);
	}

	/// <summary>
	/// Deserializes persisted structure baselines into the plan engine's input
	/// map, normalizing lock-type and attribute keys to case-insensitive so the
	/// three-way merge lines up regardless of stored casing.
	/// </summary>
	private static IReadOnlyDictionary<string, PackageStructureBaseline> DeserializeStructureBaselines(
		IReadOnlyList<ManagedStructureRecord> records)
	{
		var result = new Dictionary<string, PackageStructureBaseline>(StringComparer.Ordinal);
		foreach (var record in records)
		{
			var parsed = JsonSerializer.Deserialize<PackageStructureBaseline>(record.StructureJson, SnapshotJson);
			result[record.Objid] = parsed is null
				? PackageStructureBaseline.Empty
				: parsed with
				{
					// Fold rather than copy: a baseline written before lock names were canonical can
					// hold two spellings of one lock, which a case-insensitive copy would throw on.
					Locks = LockNames.Fold(parsed.Locks),
					AttributeFlags = parsed.AttributeFlags.ToDictionary(
						kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
				};
		}

		return result;
	}

	private async Task<LivePackageState> GatherLiveStateAsync(
		PackageManifest manifest,
		IReadOnlyList<PackageObjectRecord> installedObjects,
		IReadOnlyList<ManagedAttributeRecord> baselines,
		IReadOnlyDictionary<string, string> wellKnown,
		IReadOnlyDictionary<string, string> configureAnswers,
		IReadOnlyDictionary<string, string> crossPackageObjids,
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
		var attachObjids = new List<string>();
		foreach (var obj in manifest.Objects)
		{
			// Attach objects (decision 20.3): resolve the existing target and
			// read the attributes the manifest manages on it.
			if (obj.Target is not null)
			{
				var targetObjid = obj.Target switch
				{
					{ Kind: PackageRefKind.WellKnown } => wellKnown.GetValueOrDefault(obj.Target.Name),
					{ Kind: PackageRefKind.Configure } => configureAnswers.GetValueOrDefault(obj.Target.Name),
					{ Kind: PackageRefKind.Internal, Package: not null } =>
						crossPackageObjids.GetValueOrDefault($"{obj.Target.Package}/{obj.Target.Name}"),
					_ => null
				};
				if (targetObjid is null)
				{
					continue;
				}

				attachObjids.Add(targetObjid);
				foreach (var attrName in obj.Attributes.Keys)
				{
					Want(targetObjid, attrName);
				}

				foreach (var reference in PackageRefIndirection.RefsUsedIn(obj, obj.IsAttach ? manifest.Name : null))
				{
					Want(targetObjid, PackageRefIndirection.AttributeNameFor(reference, obj.IsAttach ? manifest.Name : null));
				}

				continue;
			}

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

			// Engine-managed ref attrs (decision 20.21) — the plan engine
			// three-way-compares them like any other managed attribute.
			foreach (var reference in PackageRefIndirection.RefsUsedIn(obj, obj.IsAttach ? manifest.Name : null))
			{
				Want(record.Objid, PackageRefIndirection.AttributeNameFor(reference, obj.IsAttach ? manifest.Name : null));
			}
		}

		var states = new Dictionary<string, LiveObjectState>(StringComparer.Ordinal);
		foreach (var objid in installedObjects.Select(o => o.Objid)
			.Concat(baselines.Select(b => b.Objid))
			.Concat(attachObjids)
			.Distinct())
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
		var attributeFlags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var attribute in attributes)
		{
			var leaf = await attributeStore
				.GetAttributeAsync(dbref.Value, attribute.Split('`'), cancellationToken)
				.LastOrDefaultAsync(cancellationToken);
			if (leaf is not null)
			{
				values[attribute] = leaf.Value.ToPlainText();
				attributeFlags[attribute] = leaf.Flags.Select(f => f.Name).ToList();
			}
		}

		var hasContents = checkContents
			&& await navigation.GetContentsAsync(dbref.Value, cancellationToken).AnyAsync(cancellationToken);

		// Full live object structure for the three-way structure merge.
		var sharpObject = known.Object();
		var flags = await sharpObject.Flags.Value.Select(f => f.Name).ToListAsync(cancellationToken);
		var powers = await sharpObject.Powers.Value.Select(p => p.Name).ToListAsync(cancellationToken);
		var locks = sharpObject.Locks.ToDictionary(
			kv => kv.Key, kv => kv.Value.LockString, StringComparer.OrdinalIgnoreCase);

		return new LiveObjectState(objid, true, sharpObject.Name, values, hasContents, flags, powers, locks, attributeFlags);
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

		// http_handler and event_handler are optional (nullable, no fixed fallback) — only
		// mapped when configured, so a package targeting {{$http_handler}} or
		// {{$event_handler}} on a game without one fails to resolve rather than guessing a
		// dbref.
		if (options.HttpHandler is > 0)
		{
			await AddAsync(WellKnownRefs.HttpHandler, options.HttpHandler, options.HttpHandler.Value);
		}

		if (options.EventHandler is > 0)
		{
			await AddAsync(WellKnownRefs.EventHandler, options.EventHandler, options.EventHandler.Value);
		}

		return map;
	}

	// ── Apply ────────────────────────────────────────────────────────────────

	public async Task<OneOf<PackageApplyResult, Error<string>>> ApplyAsync(
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
			var createdRef = await CreateObjectAsync(spec, pmWizard, Resolve, notes, cancellationToken);
			if (createdRef.IsT1)
			{
				return createdRef.AsT1;
			}

			objidByRef[change.Ref] = createdRef.AsT0;
			created[change.Ref] = createdRef.AsT0;
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
			var built = BuildRegisteredApplication(manifest.Application, manifest.Name, Resolve);
			if (built.IsT1)
			{
				return built.AsT1;
			}

			await applications.UpsertApplicationAsync(built.AsT0);
			notes.Add($"Registered application '{built.AsT0.Slug}' ({built.AsT0.Kind}) at /apps/{built.AsT0.Slug}.");
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
	private async Task<OneOf<PackageApplyResult, Error<string>>> ApplyManagedAsync(
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

		var deployResult = await managedInstaller.DeployAsync(manifest, request, binarySource, cancellationToken);
		if (deployResult.IsT1)
		{
			return deployResult.AsT1;
		}

		var deployed = deployResult.AsT0;

		var installedResult = await registry.GetInstalledPackageAsync(manifest.Name);
		var installed = installedResult.IsT0 ? installedResult.AsT0 : null;
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
	private static OneOf<RegisteredApplication, Error<string>> BuildRegisteredApplication(
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

	private async Task<OneOf<string, Error<string>>> CreateObjectAsync(
		PackageObjectSpec spec,
		SharpPlayer pmWizard,
		Func<PackageRef, string?> resolve,
		List<string> notes,
		CancellationToken cancellationToken)
	{
		var pmContainer = ToContainer(pmWizard);
		DBRef createdDbref;

		// Through the Mediator, not straight at the store: the create commands are what carry the
		// cache policy, and the one that matters here is the destination's ContentsTag. A package
		// whose object lands in the master room and is created behind the cache's back is inert —
		// command matching reads a contents list that predates it — until the entry expires or the
		// game restarts, which is exactly the "+wiki does nothing after install" report.
		//
		// ApplyDefaultFlags stays off: the manifest is the whole truth about a package object's
		// flags, and the stock thing_flags is no_command.
		switch (spec.Type)
		{
			case PackageObjectType.Room:
				createdDbref = await mediator.Send(new CreateRoomCommand(PrimaryName(spec.Name), pmWizard, ApplyDefaultFlags: false), cancellationToken);
				break;
			case PackageObjectType.Thing:
				{
					var location = await ResolveContainerAsync(spec.Location, resolve, cancellationToken) ?? pmContainer;
					createdDbref = await mediator.Send(
						new CreateThingCommand(PrimaryName(spec.Name), location, pmWizard, location, ApplyDefaultFlags: false), cancellationToken);
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
					createdDbref = await mediator.Send(
						new CreateExitCommand(parts[0], parts.Skip(1).ToArray(), location, pmWizard, ApplyDefaultFlags: false), cancellationToken);
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

			await mediator.Send(new LinkExitCommand(
				node.Match(_ => null!, _ => null!, exit => exit, _ => null!), destination), cancellationToken);
		}

		// Name updates for metadata drift.
		var metadataChange = changeset.Objects.FirstOrDefault(c =>
			c.Ref == spec.Ref && c.Action == PackageObjectAction.UpdateMetadata);
		if (metadataChange is not null)
		{
			// Through the Mediator for the same reason as the create commands above: the object
			// entry is the cache unit, and only the command declares the key that expires it.
			// PrimaryName, as the create path uses: a manifest name carries its aliases
			// ("Out;out;o"), and nothing on the write path splits them, so passing the whole string
			// here would rename an object the install itself created as "Out" to "Out;out;o" on the
			// next run.
			await mediator.Send(new SetNameCommand(node, MarkupText.Plain(PrimaryName(spec.Name))), cancellationToken);
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

			await mediator.Send(new SetObjectParentCommand(node, parentNode), cancellationToken);
		}

		// Object flags, powers, locks, and attribute flags are applied in the
		// dedicated structure pass (three-way merge), not here.
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
		var parsed = ParseObjid(objid);
		if (parsed is null)
		{
			return $"Internal error: invalid objid '{objid}'.";
		}

		var target = parsed.Value;
		var path = change.Attribute.Split('`');

		async Task WriteAsync(string value)
		{
			if (change.LiveValue is not null && change.PreviousAttribute is null)
			{
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue));
			}

			await mediator.Send(new SetAttributeCommand(target, path, MarkupText.Plain(value), pmWizard), cancellationToken);
		}

		async Task BaselineAsync(string packageValue, string? effectiveValue)
		{
			await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				manifest.Name, objid, change.Attribute.ToUpperInvariant(),
				packageValue, ContentHash.Sha256Hex(packageValue), manifest.Version.ToString()));
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
				if (change.PreviousAttribute is not null && change.LiveValue is not null)
				{
					await WriteAsync(change.LiveValue);
				}

				// The baseline still advances to the package's value
				// (dpkg semantics: local drift stays visible, no re-prompting).
				// A preserved local deletion (LiveValue null) stays deleted.
				await BaselineAsync(newValue!, change.LiveValue ?? (change.Action == PackageAttributeAction.KeepLocal ? null : newValue));
				return null;

			case PackageAttributeAction.Delete:
				preApply.Add(new PackageRevisionSnapshotAttribute(objid, change.Attribute, change.LiveValue!));
				await mediator.Send(new ClearAttributeCommand(target, path), cancellationToken);
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
							await mediator.Send(new ClearAttributeCommand(target, path), cancellationToken);
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

							if (change.PreviousAttribute is not null && change.LiveValue is not null)
							{
								await WriteAsync(change.LiveValue);
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

	// ── Object structure application (flags/powers/locks/attribute flags) ────

	/// <summary>Synthetic decision-attribute namespacing a lock conflict so it cannot collide with a real attribute name.</summary>
	private static string LockDecisionAttribute(string lockType) => $"@LOCK`{lockType}";

	/// <summary>The resolved structure a package declares on one object — the baseline it writes on apply.</summary>
	private static PackageStructureBaseline? BuildResolvedStructure(PackageObjectSpec spec, Func<PackageRef, string?> resolve)
	{
		// Canonical keys, matching the plan and the live object — see PackagePlanService.
		var locks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (lockType, raw) in LockNames.Fold(spec.Locks))
		{
			locks[lockType] = PackageRefSubstitution.Substitute(raw, resolve, out _);
		}

		var attributeFlags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var (attrName, attrSpec) in spec.Attributes)
		{
			if (attrSpec.Flags.Count > 0)
			{
				attributeFlags[attrName] = attrSpec.Flags;
			}
		}

		if (spec.Flags.Count == 0 && spec.Powers.Count == 0 && locks.Count == 0 && attributeFlags.Count == 0)
		{
			return null;
		}

		return new PackageStructureBaseline(spec.Flags, spec.Powers, locks, attributeFlags);
	}

	private async Task<string?> ApplyStructureChangeAsync(
		PackageStructureChange change, string objid,
		Dictionary<string, PackageConflictDecision> decisions, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return $"Internal error: object {objid} for structure '{change.Element}' vanished during apply.";
		}

		switch (change.Kind)
		{
			case PackageStructureKind.ObjectFlag:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var flag = await flags.GetObjectFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await flags.GetObjectFlagAsync(change.Element, cancellationToken);
					if (flag is null)
					{
						notes.Add($"{change.TargetRef}: unknown flag '{change.Element}' skipped.");
					}
					// Through the commands, not the database: the flag set is cached per object, and the
					// commands are what invalidate it.
					else if (change.Action == PackageStructureAction.Add)
					{
						await mediator.Send(new SetObjectFlagCommand(node, flag), cancellationToken);
					}
					else
					{
						await mediator.Send(new UnsetObjectFlagCommand(node, flag), cancellationToken);
					}
				}

				return null;

			case PackageStructureKind.ObjectPower:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var power = await flags.GetPowerAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await flags.GetPowerAsync(change.Element, cancellationToken);
					if (power is null)
					{
						notes.Add($"{change.TargetRef}: unknown power '{change.Element}' skipped.");
					}
					else if (change.Action == PackageStructureAction.Add)
					{
						await mediator.Send(new SetObjectPowerCommand(node, power), cancellationToken);
					}
					else
					{
						await mediator.Send(new UnsetObjectPowerCommand(node, power), cancellationToken);
					}
				}

				return null;

			case PackageStructureKind.AttributeFlag:
				if (change.Action is PackageStructureAction.Add or PackageStructureAction.Remove)
				{
					var flag = await attributeStore.GetAttributeFlagAsync(change.Element.ToUpperInvariant(), cancellationToken)
						?? await attributeStore.GetAttributeFlagAsync(change.Element, cancellationToken);
					var path = change.Attribute!.Split('`');
					if (flag is null)
					{
						notes.Add($"{change.TargetRef}/{change.Attribute}: unknown attribute flag '{change.Element}' skipped.");
					}
					else if (change.Action == PackageStructureAction.Add)
					{
						// Only flag an attribute that actually exists after apply — a
						// flag the package adds to an attribute the admin deleted locally
						// has nothing to land on.
						var leaf = await ResolveAttributeLeafAsync(objid, path, cancellationToken);
						if (leaf is not null)
						{
							// The flag commands carry the attribute cache keys and the
							// inheritance tag; the store call carried neither.
							await mediator.Send(new SetAttributeFlagCommand(ParseObjid(objid)!.Value, leaf, flag), cancellationToken);
						}
						else
						{
							notes.Add($"{change.TargetRef}/{change.Attribute}: flag '{change.Element}' skipped (attribute not present).");
						}
					}
					else
					{
						// An attribute that no longer resolves has no flag to remove; the store
						// call was a no-op in that case too.
						var leaf = await ResolveAttributeLeafAsync(objid, path, cancellationToken);
						if (leaf is not null)
						{
							await mediator.Send(new UnsetAttributeFlagCommand(ParseObjid(objid)!.Value, leaf, flag), cancellationToken);
						}
					}
				}

				return null;

			case PackageStructureKind.Lock:
				return await ApplyLockChangeAsync(node, change, decisions, cancellationToken);

			default:
				return null;
		}
	}

	private async Task<string?> ApplyLockChangeAsync(
		AnySharpObject node, PackageStructureChange change,
		Dictionary<string, PackageConflictDecision> decisions, CancellationToken cancellationToken)
	{
		// Both through the Mediator: locks load with the object node, so a direct store write
		// leaves object:#N stale, and only the commands drop the compiled boolean expression the
		// lock parser cached for the text being replaced.
		async Task SetAsync(string value) =>
			await mediator.Send(new SetLockCommand(node.Object(), change.Element, value), cancellationToken);

		switch (change.Action)
		{
			case PackageStructureAction.Add:
				await SetAsync(change.NewValue ?? "");
				return null;

			case PackageStructureAction.Remove:
				await mediator.Send(new UnsetLockCommand(node.Object(), change.Element), cancellationToken);
				return null;

			case PackageStructureAction.Conflict:
				{
					var decision = decisions[DecisionKey(change.TargetRef, LockDecisionAttribute(change.Element))];
					switch (decision.Resolution)
					{
						case PackageConflictResolution.TakeTheirs when change.Conflict == PackageConflictKind.ModifyDelete:
							await mediator.Send(new UnsetLockCommand(node.Object(), change.Element), cancellationToken);
							return null;
						case PackageConflictResolution.TakeTheirs:
							await SetAsync(change.NewValue ?? "");
							return null;
						case PackageConflictResolution.UseCustom when decision.CustomValue is not null:
							await SetAsync(decision.CustomValue);
							return null;
						case PackageConflictResolution.UseCustom:
							return $"Lock conflict {change.TargetRef}/{change.Element}: UseCustom requires a value.";
						default: // KeepMine — leave the live lock untouched.
							return null;
					}
				}

			default: // Adopt / NoChange / KeepLocal / RemoveBaseline — no write.
				return null;
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

		// Managed packages (Phase 4): no game objects/attributes — remove the
		// deposited plugins/<id>/ directory (and unload the plugin if it is loaded
		// and unloadable), then drop the registry records.
		if (installed.AsT0.DeployedFiles is { Count: > 0 })
		{
			var removed = await managedInstaller.RemoveAsync(
				packageId, installed.AsT0.DeployedFiles, cancellationToken);
			if (removed.IsT1)
			{
				return removed.AsT1;
			}

			await registry.RemoveInstalledPackageAsync(packageId);
			return new Success();
		}

		var notes = new List<string>();
		var ownObjects = await registry.GetPackageObjectsAsync(packageId);
		var ownObjids = ownObjects.Select(o => o.Objid).ToHashSet(StringComparer.Ordinal);

		// Attachment guard (decision 20.3): another package may manage
		// attributes on one of THIS package's objects (cross-package attach).
		// Destroying the object would orphan those attributes, so block while
		// any attachment exists — unless forced.
		if (ownObjids.Count > 0 && !force)
		{
			var attachers = new SortedSet<string>(StringComparer.Ordinal);
			foreach (var objid in ownObjids)
			{
				foreach (var managed in await registry.GetManagedAttributesForObjectAsync(objid))
				{
					if (managed.PackageId != packageId)
					{
						attachers.Add(managed.PackageId);
					}
				}
			}

			if (attachers.Count > 0)
			{
				return new Error<string>(
					$"Cannot uninstall '{packageId}': {string.Join(", ", attachers)} {(attachers.Count == 1 ? "is" : "are")} attached to its object(s). Uninstall them first or force-remove.");
			}
		}

		// Managed attrs on objects this package does NOT own (cross-package): clear them.
		foreach (var group in (await registry.GetManagedAttributesAsync(packageId))
			.Where(m => !ownObjids.Contains(m.Objid)).GroupBy(m => m.Objid))
		{
			var otherRefs = (await registry.GetManagedAttributesForObjectAsync(group.Key))
				.Where(a => a.PackageId != packageId && PackageRefIndirection.IsRefAttribute(a.Attribute))
				.Select(a => a.Attribute).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var dbref = ParseObjid(group.Key);
			if (dbref is null)
			{
				continue;
			}

			foreach (var managed in group.Where(a => !otherRefs.Contains(a.Attribute)))
			{
				await mediator.Send(new ClearAttributeCommand(dbref.Value, managed.Attribute.Split('`')), cancellationToken);
			}
		}

		foreach (var record in ownObjects)
		{
			await MarkGoingAsync(record.Objid, notes, cancellationToken);
		}

		// Application packages own portal registrations rather than objects;
		// reclaim every application this package installed.
		foreach (var app in (await applications.GetApplicationsAsync())
			.Where(a => string.Equals(a.OwningPackage, packageId, StringComparison.Ordinal)))
		{
			await applications.RemoveApplicationAsync(app.Slug);
			notes.Add($"Removed application '{app.Slug}'.");
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

		var managedAttributes = await registry.GetManagedAttributesAsync(packageId);
		var sharedRefs = new HashSet<(string Objid, string Attribute)>();
		foreach (var objid in snapshot.Attributes.Select(a => a.Objid)
			.Concat(managedAttributes.Select(a => a.Objid)).Distinct(StringComparer.Ordinal))
		{
			foreach (var shared in (await registry.GetManagedAttributesForObjectAsync(objid))
				.Where(a => a.PackageId != packageId && PackageRefIndirection.IsRefAttribute(a.Attribute)))
			{
				sharedRefs.Add((objid, shared.Attribute.ToUpperInvariant()));
			}
		}

		// Validate shared legacy restores before writing even the first snapshot attribute.
		// A rollback must not redirect another package's code to our historical resolution.
		foreach (var attribute in snapshot.Attributes.Where(a => sharedRefs.Contains((a.Objid, a.Attribute.ToUpperInvariant()))))
		{
			var dbref = ParseObjid(attribute.Objid);
			if (dbref is null || (await database.GetObjectNodeAsync(dbref.Value, cancellationToken)).IsNone())
			{
				continue;
			}
			var live = await attributeStore.GetAttributeAsync(dbref.Value, attribute.Attribute.Split('`'), cancellationToken)
				.LastOrDefaultAsync(cancellationToken);
			if (live?.Value.ToPlainText() != attribute.Value)
			{
				return new Error<string>($"Cannot roll back '{packageId}': {attribute.Objid}/{attribute.Attribute} is shared with another package and differs from revision {revision}.");
			}
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

			await mediator.Send(new SetAttributeCommand(
				dbref.Value, attribute.Attribute.Split('`'), MarkupText.Plain(attribute.Value), pmWizard), cancellationToken);
			await registry.UpsertManagedAttributeAsync(new ManagedAttributeRecord(
				packageId, attribute.Objid, attribute.Attribute.ToUpperInvariant(),
				attribute.Value, ContentHash.Sha256Hex(attribute.Value), snapshot.Version));
			restoredKeys.Add((attribute.Objid, attribute.Attribute.ToUpperInvariant()));
		}

		// Attributes managed now but absent from the snapshot: remove to match the old state.
		foreach (var managed in managedAttributes
			.Where(m => !restoredKeys.Contains((m.Objid, m.Attribute.ToUpperInvariant()))))
		{
			var shared = sharedRefs.Contains((managed.Objid, managed.Attribute.ToUpperInvariant()));
			var dbref = ParseObjid(managed.Objid);
			if (dbref is not null && !shared)
			{
				await mediator.Send(new ClearAttributeCommand(dbref.Value, managed.Attribute.Split('`')), cancellationToken);
			}

			await registry.RemoveManagedAttributeAsync(packageId, managed.Objid, managed.Attribute);
			notes.Add(shared
				? $"Released ownership of shared ref {managed.Objid}/{managed.Attribute}; its value was retained."
				: $"Removed {managed.Objid}/{managed.Attribute} (not present in revision {revision}).");
		}

		await RestoreStructureAsync(packageId, snapshot, notes, cancellationToken);

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

	/// <summary>
	/// Reconciles each object's package-managed structure to a revision snapshot
	/// (rollback): sets the snapshot's flags/powers/locks/attribute flags and
	/// unsets package-managed elements absent from it. Scoped to elements the
	/// package set, so admin-added extras are never disturbed.
	/// </summary>
	private async Task RestoreStructureAsync(
		string packageId, PackageRevisionSnapshot snapshot, List<string> notes, CancellationToken cancellationToken)
	{
		var noDecisions = new Dictionary<string, PackageConflictDecision>(StringComparer.Ordinal);
		// Fold once, here, so the whole rollback speaks canonical: a revision written before lock
		// names were canonical spells a lock the way that world did. Left raw, its `Teleport` add and
		// the folded baseline's `tport` removal both canonicalise to Teleport in the provider, the
		// removal is applied last, and the rollback deletes the lock the revision asked it to keep.
		var target = (snapshot.Structure ?? [])
			.ToDictionary(s => s.Objid, s => s with { Locks = LockNames.Fold(s.Locks) }, StringComparer.Ordinal);
		var current = DeserializeStructureBaselines(await registry.GetManagedStructuresAsync(packageId));

		foreach (var objid in target.Keys.Union(current.Keys, StringComparer.Ordinal))
		{
			if (await GetKnownAsync(objid, cancellationToken) is null)
			{
				notes.Add($"Skipped structure for {objid}: object no longer exists.");
				continue;
			}

			var want = target.GetValueOrDefault(objid);
			var have = current.GetValueOrDefault(objid) ?? PackageStructureBaseline.Empty;

			foreach (var change in StructureRollbackChanges(objid, have, want))
			{
				var error = await ApplyStructureChangeAsync(change, objid, noDecisions, notes, cancellationToken);
				if (error is not null)
				{
					notes.Add(error);
				}
			}

			if (want is null)
			{
				await registry.RemoveManagedStructureAsync(packageId, objid);
			}
			else
			{
				var baseline = new PackageStructureBaseline(want.Flags, want.Powers, want.Locks, want.AttributeFlags);
				await registry.UpsertManagedStructureAsync(new ManagedStructureRecord(
					packageId, objid, JsonSerializer.Serialize(baseline, SnapshotJson), snapshot.Version));
			}
		}
	}

	/// <summary>Synthesizes the add/remove structure changes that move <paramref name="have"/> to <paramref name="want"/>.</summary>
	private static IEnumerable<PackageStructureChange> StructureRollbackChanges(
		string objid, PackageStructureBaseline have, PackageRevisionSnapshotStructure? want)
	{
		var wantFlags = want?.Flags ?? Array.Empty<string>();
		var wantPowers = want?.Powers ?? Array.Empty<string>();
		var wantLocks = want?.Locks ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var wantAttrFlags = want?.AttributeFlags ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

		IEnumerable<PackageStructureChange> Set(PackageStructureKind kind, string? attribute,
			IReadOnlyList<string> haveSet, IReadOnlyList<string> wantSet)
		{
			var wantHash = new HashSet<string>(wantSet, StringComparer.OrdinalIgnoreCase);
			foreach (var element in wantSet)
			{
				yield return new PackageStructureChange(objid, objid, kind, element, PackageStructureAction.Add, attribute);
			}

			foreach (var element in haveSet.Where(e => !wantHash.Contains(e)))
			{
				yield return new PackageStructureChange(objid, objid, kind, element, PackageStructureAction.Remove, attribute);
			}
		}

		foreach (var change in Set(PackageStructureKind.ObjectFlag, null, have.Flags, wantFlags))
		{
			yield return change;
		}

		foreach (var change in Set(PackageStructureKind.ObjectPower, null, have.Powers, wantPowers))
		{
			yield return change;
		}

		var attrNames = new HashSet<string>(have.AttributeFlags.Keys, StringComparer.OrdinalIgnoreCase);
		attrNames.UnionWith(wantAttrFlags.Keys);
		foreach (var attr in attrNames)
		{
			var changes = Set(PackageStructureKind.AttributeFlag, attr,
				have.AttributeFlags.GetValueOrDefault(attr) ?? [], wantAttrFlags.GetValueOrDefault(attr) ?? []);
			foreach (var change in changes)
			{
				yield return change;
			}
		}

		foreach (var (lockType, value) in wantLocks)
		{
			yield return new PackageStructureChange(
				objid, objid, PackageStructureKind.Lock, lockType, PackageStructureAction.Add, NewValue: value);
		}

		foreach (var lockType in have.Locks.Keys.Where(k => !wantLocks.ContainsKey(k)))
		{
			yield return new PackageStructureChange(
				objid, objid, PackageStructureKind.Lock, lockType, PackageStructureAction.Remove);
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private async Task MarkGoingAsync(string objid, List<string> notes, CancellationToken cancellationToken)
	{
		var node = await GetKnownAsync(objid, cancellationToken);
		if (node is null)
		{
			return;
		}

		var going = await flags.GetObjectFlagAsync("GOING", cancellationToken);
		if (going is null)
		{
			notes.Add($"{objid}: GOING flag unavailable; object left in place.");
			return;
		}

		await mediator.Send(new SetObjectFlagCommand(node, going), cancellationToken);
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

	/// <summary>
	/// The leaf attribute at <paramref name="path"/> on <paramref name="objid"/>, or null when the
	/// path does not fully resolve. The attribute-flag commands are keyed on a
	/// <see cref="SharpAttribute"/>, so this is both the existence check and the value they need.
	/// </summary>
	private async Task<SharpAttribute?> ResolveAttributeLeafAsync(
		string objid, string[] path, CancellationToken cancellationToken)
	{
		var dbref = ParseObjid(objid);
		if (dbref is null)
		{
			return null;
		}

		var chain = await mediator.CreateStream(new GetAttributeQuery(dbref.Value, path))
			.ToArrayAsync(cancellationToken);

		// A partial chain is not a hit: the leaf named by the last segment does not exist.
		return chain.Length == path.Length ? chain[^1] : null;
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

		var body = objid.AsSpan(1);
		Span<System.Range> fields = stackalloc System.Range[2];
		var fieldCount = body.Split(fields, ':');
		if (!int.TryParse(body[fields[0]], out var number))
		{
			return null;
		}

		return fieldCount == 2 && long.TryParse(body[fields[1]], out var milliseconds)
			? new DBRef(number, milliseconds)
			: new DBRef(number);
	}


	private static string PrimaryName(string name)
	{
		Span<System.Range> aliases = stackalloc System.Range[2];
		name.AsSpan().Split(aliases, ';', StringSplitOptions.TrimEntries);
		return name[aliases[0]];
	}

	private static string DecisionKey(string targetRef, string attribute) =>
		$"{targetRef}\0{attribute.ToUpperInvariant()}";
}
