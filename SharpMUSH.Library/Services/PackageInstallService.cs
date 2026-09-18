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

/// <summary>
/// Package install orchestration (decisions 20.2, 20.7, 20.13): gathers live
/// state for the pure plan engine, executes reviewed changesets, and records
/// baselines plus revision snapshots. All created objects are owned by the
/// Package Manager wizard (config <c>package_manager</c>, default #3).
/// </summary>
public partial class PackageInstallService(
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

	// ── Shared helpers ───────────────────────────────────────────────────────

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

	private async Task ClearGoingAsync(string objid, List<string> notes, CancellationToken cancellationToken)
	{
		if (await GetKnownAsync(objid, cancellationToken) is not AnySharpObject node
			|| await flags.GetObjectFlagAsync("GOING", cancellationToken) is not SharpObjectFlag going
			|| !await node.Object().Flags.Value.AnyAsync(f => f.Name == going.Name, cancellationToken))
		{
			return;
		}

		await mediator.Send(new UnsetObjectFlagCommand(node, going), cancellationToken);
		notes.Add($"{objid}: GOING cleared; restored to the package.");
	}

	private async Task<AnySharpObject?> GetKnownAsync(string objid, CancellationToken cancellationToken)
	{
		if (HelperFunctions.ParseDbRef(objid) is not DBRef dbref)
		{
			return null;
		}

		return await database.GetObjectNodeAsync(dbref, cancellationToken) is AnySharpObject node ? node : null;
	}

	/// <summary>
	/// The leaf attribute at <paramref name="path"/> on <paramref name="objid"/>, or null when the
	/// path does not fully resolve. The attribute-flag commands are keyed on a
	/// <see cref="SharpAttribute"/>, so this is both the existence check and the value they need.
	/// </summary>
	private async Task<SharpAttribute?> ResolveAttributeLeafAsync(
		string objid, string[] path, CancellationToken cancellationToken)
	{
		if (HelperFunctions.ParseDbRef(objid) is not DBRef dbref)
		{
			return null;
		}

		var chain = await mediator.CreateStream(new GetAttributeQuery(dbref, path))
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
		return node switch
		{
			null or SharpExit => null,
			SharpPlayer player => player,
			SharpRoom room => room,
			SharpThing thing => thing
		};
	}

	private async Task<SharpPlayer> GetPackageManagerWizardAsync(CancellationToken cancellationToken)
	{
		var number = (int)(configuration.CurrentValue.Database.PackageManager ?? 3);
		var node = await database.GetObjectNodeAsync(new DBRef(number), cancellationToken);
		return node switch
		{
			AnySharpObject and SharpPlayer player => player,
			AnySharpObject => throw new InvalidOperationException($"#{number} is not a player."),
			None => throw new InvalidOperationException($"Package Manager wizard #{number} does not exist.")
		};
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

	private static string PrimaryName(string name)
	{
		Span<System.Range> aliases = stackalloc System.Range[2];
		name.AsSpan().Split(aliases, ';', StringSplitOptions.TrimEntries);
		return name[aliases[0]];
	}

	private static string DecisionKey(string targetRef, string attribute) =>
		$"{targetRef}\0{attribute.ToUpperInvariant()}";
}
