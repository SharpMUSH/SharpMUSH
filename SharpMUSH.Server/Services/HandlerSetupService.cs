using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The game's <c>http_handler</c> and <c>event_handler</c> as the setup wizard sets them: an object the game
/// already has (a PennMUSH game's own, named by its <c>mush.cnf</c>), a new one, or none. The bundled packages
/// that attach to a handler move with it, so changing the handler builds them onto the new object instead of
/// leaving them on the old one.
/// </summary>
public class HandlerSetupService(
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> options,
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IBundledPackageBootstrap bundled,
	IPackageManifestService manifests,
	IConfigOptionWriter config,
	ILogger<HandlerSetupService> logger)
{
	private static readonly string[] Kinds = [HandlerKinds.Http, HandlerKinds.Event];

	/// <summary>Both handlers: the configured object, whether it is WIZARD, and the packages built on it.</summary>
	public async Task<IReadOnlyList<HandlerState>> HandlersAsync(CancellationToken cancellationToken)
	{
		var states = new List<HandlerState>(Kinds.Length);
		foreach (var kind in Kinds)
		{
			var number = Configured(kind) is { } configured ? (int?)configured : null;
			var handler = number is { } n
				? await mediator.Send(new GetObjectNodeQuery(new DBRef(n)), cancellationToken)
				: default;
			states.Add(handler is AnySharpObject found
				? new HandlerState(kind, number, found.Object().Name, await found.IsWizard(cancellationToken),
					await AttachedAsync(kind))
				: new HandlerState(kind, number, null, false, await AttachedAsync(kind)));
		}

		return states;
	}

	/// <summary>
	/// The attributes the bundled packages that build on a <paramref name="kind"/> handler write to object
	/// <paramref name="number"/> where it holds something other than what the package ships: its own attribute of
	/// that name, or a value an earlier install kept in place of the package's. Installing keeps the object's value,
	/// so those parts of the packages run the game's code instead of their own; the wizard shows them before the
	/// object is made, or kept as, the handler.
	/// </summary>
	public async Task<Result<IReadOnlyList<HandlerClash>>> ClashesAsync(string kind, int number,
		CancellationToken cancellationToken)
	{
		if (!Kinds.Contains(kind))
		{
			return new Error<string>($"Unknown handler: {kind}.");
		}

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), cancellationToken) is not AnySharpObject found)
		{
			return new Error<string>($"#{number} does not exist.");
		}

		var target = found.Object().DBRef;
		var managed = await registry.GetManagedAttributesForObjectAsync(target.ToString());
		var wellKnown = kind == HandlerKinds.Http ? WellKnownRefs.HttpHandler : WellKnownRefs.EventHandler;
		var clashes = new List<HandlerClash>();
		foreach (var package in BundledPackages.All.Where(p => GameFeatureService.HandlerKind(p.Requires) == kind))
		{
			if (manifests.ParseManifest(BundledPackages.ManifestYaml(package.PackageId)) is not ParsedPackageManifest parsed)
			{
				continue;
			}

			var written = parsed.Manifest.Objects
				.Where(o => o.Target is { Kind: PackageRefKind.WellKnown } t && t.Name == wellKnown)
				.SelectMany(o => o.Attributes.Keys);
			foreach (var attribute in written)
			{
				// GetAttributeQuery yields the path's nodes, the leaf last, only when the leaf exists.
				var live = await mediator.CreateStream(new GetAttributeQuery(target, attribute.Split('`')), cancellationToken)
					.LastOrDefaultAsync(cancellationToken);
				if (live is null)
				{
					continue;
				}

				// Installed already: the package recorded what it ships as the baseline, including where an install
				// kept the object's own value instead, so a live value that differs is one the package is not running.
				// Not installed: anything already there is the object's own.
				var baseline = managed.FirstOrDefault(m => m.PackageId == package.PackageId
					&& string.Equals(m.Attribute, attribute, StringComparison.OrdinalIgnoreCase));
				if (baseline is null || baseline.BaselineValue != live.Value.ToPlainText())
				{
					clashes.Add(new HandlerClash(package.PackageId, attribute));
				}
			}
		}

		return clashes;
	}

	/// <summary>
	/// Sets the <paramref name="kind"/> handler as <paramref name="request"/> says. The bundled packages built on
	/// the previous handler are removed from it and installed onto the new one; with no handler they stay removed.
	/// </summary>
	public async Task<Result<Success>> SetAsync(string kind, SetHandlerRequest request, CancellationToken cancellationToken)
	{
		if (!Kinds.Contains(kind))
		{
			return new Error<string>($"Unknown handler: {kind}.");
		}

		return request.Mode switch
		{
			HandlerModes.Use when request.Dbref is { } number => await UseAsync(kind, number, cancellationToken),
			HandlerModes.Use => new Error<string>("Name the object to use."),
			HandlerModes.Create => await CreateAsync(kind, cancellationToken),
			HandlerModes.None => await MoveToAsync(kind, null, cancellationToken),
			_ => new Error<string>($"Unknown mode: {request.Mode}.")
		};
	}

	private async Task<Result<Success>> UseAsync(string kind, int number, CancellationToken cancellationToken)
	{
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), cancellationToken) is not AnySharpObject found)
		{
			return new Error<string>($"#{number} does not exist.");
		}

		if (found is SharpExit)
		{
			return new Error<string>($"#{number} is an exit; a handler has to be a thing, room or player.");
		}

		return await MoveToAsync(kind, (uint)number, cancellationToken);
	}

	/// <summary>
	/// A new handler, as the seed makes #8 and #9: a WIZARD thing owned by God in the master room.
	/// </summary>
	private async Task<Result<Success>> CreateAsync(string kind, CancellationToken cancellationToken)
	{
		// Refused before the object is made, so a package that blocks the move leaves no orphaned handler behind.
		var attached = await AttachedAsync(kind);
		if (await BlockingAsync(attached) is Error<string> blocked)
		{
			return blocked;
		}

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), cancellationToken) is not (AnySharpObject and SharpPlayer owner))
		{
			return new Error<string>("God (#1) is not a player, so there is no one to own a new handler.");
		}

		var masterRoom = options.CurrentValue.Database.MasterRoom;
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef((int)masterRoom)), cancellationToken)
				is not (AnySharpObject and SharpRoom room))
		{
			return new Error<string>($"The master room #{masterRoom} is not a room.");
		}

		var created = await mediator.Send(new CreateThingCommand(
			kind == HandlerKinds.Http ? "HTTP Handler" : "Event Handler", room, owner, room, ApplyDefaultFlags: false),
			cancellationToken);

		if (await mediator.Send(new GetObjectFlagQuery("WIZARD"), cancellationToken) is { } wizard
				&& await mediator.Send(new GetObjectNodeQuery(created), cancellationToken) is AnySharpObject node)
		{
			await mediator.Send(new SetObjectFlagCommand(node, wizard), cancellationToken);
		}

		logger.LogInformation("Setup wizard created {Kind} handler #{Number}.", LogSanitizer.Sanitize(kind), created.Number);
		return await RepointAsync(kind, (uint)created.Number, attached, cancellationToken);
	}

	/// <summary>
	/// Points <paramref name="kind"/> at <paramref name="target"/>, taking the bundled packages built on the old
	/// handler along. Refused before anything changes when a package that is not bundled depends on one of them.
	/// </summary>
	private async Task<Result<Success>> MoveToAsync(string kind, uint? target, CancellationToken cancellationToken)
	{
		var current = Configured(kind);
		if (current == target)
		{
			return new Success();
		}

		var attached = await AttachedAsync(kind);
		return await BlockingAsync(attached) is Error<string> blocked
			? blocked
			: await RepointAsync(kind, target, attached, cancellationToken);
	}

	/// <summary>
	/// Refuses a move when a package that is not bundled depends on one of the <paramref name="attached"/> ones,
	/// which would have to be removed from the current handler.
	/// </summary>
	private async Task<Result<Success>> BlockingAsync(IReadOnlyList<string> attached)
	{
		foreach (var id in attached)
		{
			var blocking = (await registry.GetPackageDependentsAsync(id))
				.Select(d => d.PackageId)
				.Where(dependent => !attached.Contains(dependent))
				.ToList();
			if (blocking.Count > 0)
			{
				return new Error<string>(
					$"{string.Join(", ", blocking)} depends on {id}, which is built on the current handler; remove it first.");
			}
		}

		return new Success();
	}

	/// <summary>Removes the <paramref name="attached"/> packages, sets the handler, and builds them onto the new one.</summary>
	private async Task<Result<Success>> RepointAsync(string kind, uint? target, IReadOnlyList<string> attached,
		CancellationToken cancellationToken)
	{
		var removed = new List<string>(attached.Count);
		foreach (var id in attached.Reverse())
		{
			if (await installer.UninstallAsync(id, cancellationToken: cancellationToken) is Error<string> error)
			{
				// The handler is unchanged, so what came off it goes back on rather than leaving the game without it.
				var restored = removed.Count == 0 ? [] : await bundled.InstallBundledAsync(removed, cancellationToken);
				var lost = removed.Except(restored).ToList();
				return new Error<string>($"{id} could not be removed from the current handler: {error.Value}" +
					(lost.Count == 0 ? string.Empty : $" {string.Join(", ", lost)} could not be put back; the server log says why."));
			}

			removed.Add(id);
		}

		await config.UpdateAsync(settings => settings with
		{
			Database = kind == HandlerKinds.Http
				? settings.Database with { HttpHandler = target }
				: settings.Database with { EventHandler = target }
		});
		logger.LogInformation("Setup wizard set the {Kind} handler to {Target}.", LogSanitizer.Sanitize(kind),
			target is { } t ? $"#{t}" : "none");

		if (target is null || attached.Count == 0)
		{
			return new Success();
		}

		var reinstalled = await bundled.InstallBundledAsync(attached, cancellationToken);
		var missing = attached.Except(reinstalled).ToList();
		return missing.Count == 0
			? new Success()
			: new Error<string>($"The handler is set, but {string.Join(", ", missing)} could not be built onto it; " +
				"the server log says why.");
	}

	/// <summary>The installed bundled packages that attach to <paramref name="kind"/>, in install order.</summary>
	private async Task<IReadOnlyList<string>> AttachedAsync(string kind)
	{
		return await BundledPackages.All
			.Where(p => GameFeatureService.HandlerKind(p.Requires) == kind)
			.ToAsyncEnumerable()
			.Where(async (p, _) => await registry.GetInstalledPackageAsync(p.PackageId) is InstalledPackageRecord)
			.Select(p => p.PackageId)
			.ToListAsync();
	}

	private uint? Configured(string kind)
	{
		var database = options.CurrentValue.Database;
		var number = kind == HandlerKinds.Http ? database.HttpHandler : database.EventHandler;
		return number is 0 ? null : number;
	}
}
