using System.Text;
using MarkupString;
using SharpMUSH.Messaging.Abstractions;
using Mediator;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data.
/// KafkaFlow handles batching automatically via producer LingerMs configuration.
/// </summary>
public class NotifyService(
	IMessageBus publishEndpoint, 
	IConnectionService connections,
	IListenerRoutingService? listenerRoutingService = null,
	IMediator? mediator = null) : INotifyService
{
	/// <summary>
	/// Normalizes line endings by replacing all \n with \r\n and ensuring trailing \r\n
	/// </summary>
	private static string NormalizeLineEnding(string text)
	{
		// Replace all standalone \n with \r\n (but don't double-up existing \r\n)
		text = text.Replace("\r\n", "\n"); // First normalize everything to \n
		text = text.Replace("\n", "\r\n");  // Then convert all to \r\n
		
		// Ensure it ends with exactly one \r\n
		text = text.TrimEnd('\r', '\n');
		return text + "\r\n";
	}

	public async ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		// Route to listeners if service is available and we have location context
		if (listenerRoutingService != null && mediator != null && sender != null)
		{
			try
			{
				// Determine the location for listener routing
				var location = await sender.Match<ValueTask<DBRef>>(
					async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					room => ValueTask.FromResult(room.Object.DBRef),
					async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef
				);
				
				var notificationContext = new NotificationContext(
					Target: who,
					Location: location,
					IsRoomBroadcast: false,
					ExcludedObjects: []
				);
				
				// Fire and forget - don't await to avoid blocking notification
				await listenerRoutingService.ProcessNotificationAsync(notificationContext, what, sender, type);
			}
			catch
			{
				// Silently ignore errors in listener routing to not block notifications
			}
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		text = NormalizeLineEnding(text);
		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish directly to Kafka - batching is handled by KafkaFlow producer
		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.HandlePublish(new TelnetOutputMessage(handle, bytes));
		}
	}

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		text = NormalizeLineEnding(text);
		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish directly to Kafka - batching is handled by KafkaFlow producer
		await publishEndpoint.HandlePublish(new TelnetOutputMessage(handle, bytes));
	}

	public async ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		var text = what.Match(
			markupString => markupString.ToString(),
			str => str
		);

		text = NormalizeLineEnding(text);
		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish directly to Kafka - batching is handled by KafkaFlow producer
		foreach (var handle in handles)
		{
			await publishEndpoint.HandlePublish(new TelnetOutputMessage(handle, bytes));
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

		// Prompts typically don't need newlines, but ensure consistency
		// (Prompts are usually things like "> " without line breaks)
		var bytes = Encoding.UTF8.GetBytes(text);

		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
		{
			await publishEndpoint.HandlePublish(new TelnetPromptMessage(handle, bytes));
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

		// Prompts typically don't need newlines
		var bytes = Encoding.UTF8.GetBytes(text);

		// Publish prompt message to each handle
		foreach (var handle in handles)
		{
			await publishEndpoint.HandlePublish(new TelnetPromptMessage(handle, bytes));
		}
	}

	public async ValueTask NotifyExcept(DBRef who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		// Get all handles for the target location/object
		var targetHandles = await connections.Get(who).Select(x => x.Handle).ToArrayAsync();
		
		// Get all handles to exclude
		var excludeHandles = new HashSet<long>();
		foreach (var exceptDbRef in except)
		{
			await foreach (var conn in connections.Get(exceptDbRef))
			{
				excludeHandles.Add(conn.Handle);
			}
		}
		
		// Filter out excluded handles and notify the rest
		var notifyHandles = targetHandles.Where(h => !excludeHandles.Contains(h)).ToArray();
		
		if (notifyHandles.Length > 0)
		{
			await Notify(notifyHandles, what, sender, type);
		}
	}

	public ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public async ValueTask NotifyExcept(AnySharpObject who, OneOf<MString, string> what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await NotifyExcept(who.Object().DBRef, what, except.Select(x => x.Object().DBRef).ToArray(), sender, type);

	/// <summary>
	/// Unified error handling: optionally notify user, then return error.
	/// The notify message and error return are SEPARATE and can be different strings.
	/// Callers choose which error and notification to use via ErrorMessages constants.
	/// </summary>
	/// <param name="target">Object to notify (DBRef)</param>
	/// <param name="errorReturn">Error string for return value (e.g., "#-1 PERMISSION DENIED")</param>
	/// <param name="notifyMessage">Message to show user (e.g., "You don't have permission to do that.")</param>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	/// <returns>CallState with error return string</returns>
	public async ValueTask<CallState> NotifyAndReturn(
		DBRef target,
		string errorReturn,
		string notifyMessage,
		bool shouldNotify)
	{
		if (shouldNotify)
		{
			await Notify(target, notifyMessage, sender: null);
		}
		
		return new CallState(errorReturn);
	}
}
