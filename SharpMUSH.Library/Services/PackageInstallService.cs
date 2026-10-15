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
/// Package Manager wizard (config <c>package_manager</c>, default the seeded #7).
///
/// <para>Apply, uninstall and rollback each hold <see cref="IPackageOperationGate"/> from their first
/// registry read to their last write, so no two package operations interleave (#1484). An apply's
/// lifecycle hooks run after it lets the gate go.</para>
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
	IPluginPackageInstaller managedInstaller,
	IMediator mediator,
	IPackageOperationGate gate,
	ILockService locks,
	IPackageDeclarationService declarations,
	IPackageSettingService settings,
	IPackageWikiService wiki,
	Lazy<IRuntimeRegistrationService> registrations,
	Lazy<IPluginManager> plugins) : IPackageInstallService
{
	private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

	// ── Shared helpers ───────────────────────────────────────────────────────

	/// <summary>Which of the plugins <paramref name="manifest"/> names in <c>plugins:</c> are running now.</summary>
	private IReadOnlySet<string> RunningPlugins(PackageManifest manifest)
		=> manifest.RequiredPlugins.Where(id => plugins.Value.Registration(id) is not null)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>Opens the undo log one package operation writes through; see <see cref="PackageWriteTransaction"/>.</summary>
	private PackageWriteTransaction BeginWrites(SharpPlayer packageManager) =>
		new(mediator, database, attributeStore, flags, registry, applications, locks, packageManager);

	/// <summary>
	/// Lets go of an object the package no longer owns: GOING, so the purge frees it, and HALT, so it
	/// stops answering at once. GOING alone leaves it matching <c>$</c>-commands, <c>^</c>-listens and
	/// ACONNECT until the purge, as PennMUSH does for a <c>@destroy</c>ed object, and a reinstalled
	/// package would then answer every command twice.
	/// </summary>
	private async Task ReleaseObjectAsync(
		PackageWriteTransaction writes, string objid, List<string> notes, CancellationToken cancellationToken)
	{
		if (await GetKnownAsync(objid, cancellationToken) is not { } node)
		{
			return;
		}

		if (await flags.GetObjectFlagAsync("GOING", cancellationToken) is not SharpObjectFlag going
			|| await flags.GetObjectFlagAsync("HALT", cancellationToken) is not SharpObjectFlag halt)
		{
			notes.Add($"{objid}: GOING or HALT flag unavailable; object left in place.");
			return;
		}

		await writes.SetFlagAsync(node, going, cancellationToken);
		await writes.SetFlagAsync(node, halt, cancellationToken);
		notes.Add($"{objid}: halted and marked GOING for garbage collection.");
	}

	/// <summary>
	/// Takes back an object <see cref="ReleaseObjectAsync"/> let go of. HALT is cleared with GOING; a
	/// rollback then sets the flags its revision recorded, so a package that ships a HALT object keeps it.
	/// </summary>
	private async Task ReviveObjectAsync(
		PackageWriteTransaction writes, string objid, List<string> notes, CancellationToken cancellationToken)
	{
		if (await GetKnownAsync(objid, cancellationToken) is not { } node
			|| await flags.GetObjectFlagAsync("GOING", cancellationToken) is not SharpObjectFlag going
			|| !await node.Object().Flags.Value.AnyAsync(f => f.Name == going.Name, cancellationToken))
		{
			return;
		}

		await writes.UnsetFlagAsync(node, going, cancellationToken);
		if (await flags.GetObjectFlagAsync("HALT", cancellationToken) is SharpObjectFlag halt)
		{
			await writes.UnsetFlagAsync(node, halt, cancellationToken);
		}

		notes.Add($"{objid}: GOING and HALT cleared; restored to the package.");
	}

	private async Task<AnySharpObject?> GetKnownAsync(string objid, CancellationToken cancellationToken)
	{
		if (HelperFunctions.ParseDbRef(objid) is not DBRef dbref)
		{
			return null;
		}

		return await database.GetObjectNodeAsync(dbref, cancellationToken) is AnySharpObject node ? node : null;
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
		var number = (int)DatabaseOptions.PackageManagerOrSeeded(configuration.CurrentValue.Database.PackageManager);
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
