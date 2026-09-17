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
		AnySharpObject executor,
		MString name)
	{
		var defaultHome = new DBRef((int)configuration.CurrentValue.Database.DefaultHome);
		if (await mediator.Send(new GetObjectNodeQuery(defaultHome)) is not AnySharpObject home || home.IsExit)
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

		var thing = await mediator.Send(new CreateThingCommand(name.ToPlainText(),
			executor.AsContainer,
			await executor.Object().Owner.WithCancellation(CancellationToken.None),
			home.AsContainer));

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
}
