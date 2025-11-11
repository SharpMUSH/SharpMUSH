using MassTransit;
using MarkupString;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using System.Text;
using MString = MarkupString.MarkupStringModule.MarkupString;
using static MarkupString.MarkupStringModule;

namespace SharpMUSH.Server.Services;

/// <summary>
/// NotifyService implementation that publishes messages to the message queue
/// instead of directly calling connection functions.
/// </summary>
public class MessageQueueNotifyService(IPublishEndpoint publishEndpoint, IConnectionService connections) : IMessageQueueNotifyService
{
	public async ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		await ValueTask.CompletedTask;
		
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
		}
	}

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Notify([handle], what, sender, type);

	public async ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish output message to each handle
		foreach (var handle in handles)
		{
			await publishEndpoint.Publish(new TelnetOutputMessage(handle, bytes));
		}
	}

	public async ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.Publish(new TelnetPromptMessage(handle, bytes));
		}
	}

	public ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public async ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Prompt([handle], what, sender, type);

	public async ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MarkupStringModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish prompt message to each handle
		foreach (var handle in handles)
		{
			await publishEndpoint.Publish(new TelnetPromptMessage(handle, bytes));
		}
	}

	public async ValueTask NotifyExcept(DBRef who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		// TODO: Implement when we have DBRef to handle mapping
		await ValueTask.CompletedTask;
	}

	public ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public async ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await NotifyExcept(who.Object().DBRef, what, except.Select(x => x.Object().DBRef).ToArray(), sender, type);
}
