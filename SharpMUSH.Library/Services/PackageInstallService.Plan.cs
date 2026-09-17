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

/// <summary>Plan: gathers the live state the pure plan engine diffs against.</summary>
public partial class PackageInstallService
{
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
		var installed = await registry.GetInstalledPackageAsync(manifest.Name) is InstalledPackageRecord found ? found : null;
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
		if (HelperFunctions.ParseDbRef(objid) is not DBRef dbref)
		{
			return new LiveObjectState(objid, false, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}

		if (await database.GetObjectNodeAsync(dbref, cancellationToken) is not AnySharpObject known)
		{
			return new LiveObjectState(objid, false, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}

		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var attributeFlags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (var attribute in attributes)
		{
			var leaf = await attributeStore
				.GetAttributeAsync(dbref, attribute.Split('`'), cancellationToken)
				.LastOrDefaultAsync(cancellationToken);
			if (leaf is not null)
			{
				values[attribute] = leaf.Value.ToPlainText();
				attributeFlags[attribute] = leaf.Flags.Select(f => f.Name).ToList();
			}
		}

		var hasContents = checkContents
			&& await navigation.GetContentsAsync(dbref, cancellationToken).AnyAsync(cancellationToken);

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
			if (await database.GetObjectNodeAsync(new DBRef((int)(number ?? fallback)), cancellationToken) is AnySharpObject node)
			{
				map[name] = node.Object().DBRef.ToString();
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
}
