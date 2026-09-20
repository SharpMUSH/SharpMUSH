using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The building work <c>@create</c> and <c>create()</c> share.
/// </summary>
/// <remarks>
/// PennMUSH's <c>fun_create</c> is a one-line call to <c>do_create</c> (<c>src/fundb.c</c>), so
/// every step of the command is a step of the function: the default-home check, the name
/// validation, the object landing in the creator's inventory, the zone inherited from the creator,
/// the report, the <c>OBJECT`CREATE</c> event and the C# object-lifecycle hook. Written out twice,
/// the function had none of the last four and put the new object in the creator's <em>room</em>.
/// </remarks>
public static class BuildingHelpers
{
	/// <inheritdoc cref="BuildingHelpers"/>
	public static async ValueTask<Result<DBRef>> CreateThingAsync(
		IMUSHCodeParser parser,
		IMediator mediator,
		IObjectStore database,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IValidateService validateService,
		INotifyService notifyService,
		IEventService eventService,
		IPermissionService permissionService,
		AnySharpObject executor,
		MString name)
	{
		if (await HomeForNewObjectAsync(mediator, configuration, permissionService, executor) is not AnySharpContainer home)
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.DefaultHomeLocationInvalid), executor);
			return new Error<string>(ErrorMessages.Returns.NotARoom);
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, new None()))
		{
			await notifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.InvalidNameThing), executor);
			return new Error<string>(ErrorMessages.Returns.BadObjectName);
		}

		// PennMUSH do_create hands the new object to the executor (src/create.c). An exit cannot
		// hold anything — AnySharpObject.AsContainer throws for one — so code owned by an exit
		// builds into the room the exit is in. @CREATE threw outright in that case; create() had
		// the fallback and lost it when the two were merged.
		var into = executor.IsContainer ? executor.AsContainer : await executor.Where();

		var thing = await mediator.Send(new CreateThingCommand(name.ToPlainText(),
			into,
			await executor.Object().Owner.WithCancellation(CancellationToken.None),
			home));

		// A new object inherits its creator's zone, once the cycle guard allows it.
		if (await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone &&
			await mediator.Send(new GetObjectNodeQuery(thing)) is AnySharpObject created &&
			await HelperFunctions.SafeToAddZone(mediator, database, created, zone))
		{
			await mediator.Send(new SetObjectZoneCommand(created, zone));
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Created), executor, name, thing);

		await eventService.TriggerEventAsync(parser, "OBJECT`CREATE", executor.Object().DBRef,
			thing.ToString(),
			""); // null for cloned-from (not a clone)

		// Phase 2b: C# object-lifecycle hooks fire alongside the softcode OBJECT`CREATE event.
		if (parser.ServiceProvider.GetService<IPluginHookDispatcher>() is { } createHooks)
		{
			await createHooks.ObjectCreatedAsync(thing, executor.Object().DBRef);
		}

		return thing;
	}

	/// <summary>
	/// PennMUSH <c>do_create</c> (<c>src/create.c:589-597</c>) derives the new object's home from the
	/// creator and never from a configured constant:
	/// <code>
	/// if ((loc = Location(player)) != NOTHING &amp;&amp; (controls(player, loc) || Abode(loc)))
	///   Home(thing) = loc;
	/// else
	///   Home(thing) = Home(player);
	/// </code>
	/// <c>Database.DefaultHome</c> survives only as the last resort, for a creator whose own home is
	/// unset — a room without a drop-to, or an exit that has never been linked. Penn cannot reach that
	/// branch, because every one of its objects always carries a <c>home</c>.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> HomeForNewObjectAsync(
		IMediator mediator,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPermissionService permissionService,
		AnySharpObject executor)
	{
		var where = await executor.Where();
		if (await permissionService.Controls(executor, where.WithExitOption()) || await where.Object().HasFlag("ABODE"))
		{
			return new AnyOptionalSharpContainer(where);
		}

		if (await CreatorHomeAsync(executor) is AnySharpContainer own)
		{
			return new AnyOptionalSharpContainer(own);
		}

		var configured = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		return await mediator.Send(new GetObjectNodeQuery(configured)) is AnySharpObject { IsContainer: true } fallback
			? new AnyOptionalSharpContainer(fallback.AsContainer)
			: new AnyOptionalSharpContainer(new None());
	}

	/// <summary>
	/// PennMUSH's <c>home</c> field is one slot read differently per type, and <c>Home(player)</c> at
	/// create.c:595 reads whichever applies to the creator: a player's or thing's home, an exit's
	/// destination (<c>src/db.h</c> aliases <c>Destination</c> to it), or a room's drop-to.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpContainer> CreatorHomeAsync(AnySharpObject executor) => executor switch
	{
		SharpPlayer player => new AnyOptionalSharpContainer(
			await player.Home.WithCancellation(CancellationToken.None)),
		SharpThing thing => new AnyOptionalSharpContainer(
			await thing.Home.WithCancellation(CancellationToken.None)),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None),
		SharpRoom room => await room.Location.WithCancellation(CancellationToken.None)
	};
}
