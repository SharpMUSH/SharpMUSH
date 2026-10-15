using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Raises <c>ATTRIBUTE`CHANGE</c> for each change to a <c>watched</c> attribute, unless the change was made
/// <see cref="AttributeChangeChain.MaxDepth"/> handlers deep.
/// </summary>
public class AttributeEventHandlers(
	IMediator mediator,
	IEventService eventService,
	ILogger<AttributeEventHandlers> logger)
	: INotificationHandler<AttributeChangedNotification>
{
	public async ValueTask Handle(AttributeChangedNotification notification, CancellationToken cancellationToken)
	{
		if (AttributeChangeChain.Depth >= AttributeChangeChain.MaxDepth)
		{
			logger.LogWarning(
				"{Event} not raised for {Target}/{Attribute}: {Depth} attribute changes deep, each made by the handler of the one before",
				SharpEvents.AttributeChange, notification.Target, notification.Attribute, AttributeChangeChain.Depth);
			return;
		}

		// The write may have named the object by number alone; the handler is given its objid, as other events are.
		var target = await mediator.Send(new GetObjectNodeQuery(notification.Target), ExecutionBudget.CurrentToken) switch
		{
			AnySharpObject found => found.Object().DBRef,
			_ => notification.Target
		};

		await eventService.TriggerEventAsync(SharpEvents.AttributeChange,
			notification.Enactor,
			[
				MarkupText.Plain(target.ToString()),
				MarkupText.Plain(notification.Attribute),
				notification.OldValue,
				notification.NewValue,
				MarkupText.Plain(notification.Enactor is { Number: >= 0 } enactor ? enactor.ToString() : new DBRef(1).ToString())
			]);
	}
}
