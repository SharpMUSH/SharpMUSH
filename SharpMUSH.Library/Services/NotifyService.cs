using MarkupString;
using Mediator;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data.
/// KafkaFlow handles batching automatically via producer LingerMs configuration.
/// </summary>
public class NotifyService(
	IMessageBus publishEndpoint,
	IConnectionService connections,
	ILocalizationService localizationService,
	IListenerRoutingService? listenerRoutingService = null,
	IMediator? mediator = null) : INotifyService
{
	/// <summary>
	/// Publishes output to a single connection as serialized markup. The ConnectionServer owns the
	/// wire format (ANSI/Pueblo/MXP for terminals, a markup envelope for portal/WebSocket clients),
	/// so the markup is kept as an <see cref="MString"/> here and only serialized for transport.
	/// </summary>
	private async ValueTask PublishMarkup(long handle, OneOf<MString, string> what)
	{
		var ms = what.Match(markup => markup, MModule.single);
		ms = ApplyOutputPrefixSuffix(handle, ms);
		await publishEndpoint.HandlePublish(new MarkupOutputMessage(handle, MModule.serialize(ms)));
	}

	/// <summary>
	/// Publishes prompt output to a single connection as serialized markup. Prompts are not wrapped
	/// with OUTPUTPREFIX/OUTPUTSUFFIX and carry no trailing newline.
	/// </summary>
	private async ValueTask PublishMarkupPrompt(long handle, OneOf<MString, string> what)
	{
		var ms = what.Match(markup => markup, MModule.single);
		await publishEndpoint.HandlePublish(new MarkupPromptMessage(handle, MModule.serialize(ms)));
	}

	/// <summary>
	/// Wraps markup with OUTPUTPREFIX / OUTPUTSUFFIX if set on the connection, keeping everything as
	/// an <see cref="MString"/>. Mirrors PennMUSH's per-command output wrapping (src/bsd.c): the
	/// prefix is emitted as a separate line before the output and the suffix as a separate line
	/// after. Line-ending normalization is the ConnectionServer's responsibility at render time.
	/// </summary>
	private MString ApplyOutputPrefixSuffix(long handle, MString text)
	{
		var conn = connections.Get(handle);
		if (conn is null)
		{
			return text;
		}

		var hasPrefix = conn.Metadata.TryGetValue("OutputPrefix", out var prefix);
		var hasSuffix = conn.Metadata.TryGetValue("OutputSuffix", out var suffix);

		if (!hasPrefix && !hasSuffix)
		{
			return text;
		}

		var parts = new List<MString>();
		if (hasPrefix && !string.IsNullOrEmpty(prefix))
		{
			parts.Add(MModule.single(prefix));
			parts.Add(MModule.single("\n"));
		}
		parts.Add(text);
		if (hasSuffix && !string.IsNullOrEmpty(suffix))
		{
			parts.Add(MModule.single("\n"));
			parts.Add(MModule.single(suffix));
		}
		return MModule.multiple(parts);
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

		// Publish markup per-connection; the ConnectionServer renders to the connection's wire format.
		await foreach (var conn in connections.Get(who))
		{
			await PublishMarkup(conn.Handle, what);
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

		await PublishMarkup(handle, what);
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

		foreach (var handle in handles)
		{
			await PublishMarkup(handle, what);
		}
	}

	public async ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		await foreach (var conn in connections.Get(who))
		{
			await PublishMarkupPrompt(conn.Handle, what);
		}
	}

	public ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public async ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Prompt([handle], what, sender, type);

	public async ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
		))
		{
			return;
		}

		// Publish prompt markup to each handle
		foreach (var handle in handles)
		{
			await PublishMarkupPrompt(handle, what);
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

		// Get all handles to exclude using async LINQ SelectMany over all except-DBRefs
		var excludeHandles = await except.ToAsyncEnumerable()
			.SelectMany(dbRef => connections.Get(dbRef))
			.Select(conn => conn.Handle)
			.ToHashSetAsync();

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

	public async ValueTask NotifyLocalized(DBRef who, string key, params object[] args)
	{
		await foreach (var conn in connections.Get(who))
		{
			conn.Metadata.TryGetValue("Locale", out var locale);
			var message = localizationService.Format(key, locale, args);
			await Notify(conn.Handle, message, sender: null);
		}
	}

	public ValueTask NotifyLocalized(AnySharpObject who, string key, params object[] args)
		=> NotifyLocalized(who.Object().DBRef, key, args);

	public async ValueTask NotifyLocalized(long handle, string key, params object[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		var message = localizationService.Format(key, locale, args);
		await Notify(handle, message, sender: null);
	}

	public async ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args)
	{
		await foreach (var conn in connections.Get(who))
		{
			conn.Metadata.TryGetValue("Locale", out var locale);
			var message = localizationService.Format(key, locale, args);
			await Notify(conn.Handle, message, sender: sender);
		}
	}

	public ValueTask NotifyLocalized(AnySharpObject who, string key, AnySharpObject? sender, params object[] args)
		=> NotifyLocalized(who.Object().DBRef, key, sender, args);

	public async ValueTask NotifyLocalized(long handle, string key, AnySharpObject? sender, params object[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		var message = localizationService.Format(key, locale, args);
		await Notify(handle, message, sender: sender);
	}

	public async ValueTask NotifyLocalizedMarkup(DBRef who, string key, AnySharpObject? sender, params MString[] args)
	{
		await foreach (var conn in connections.Get(who))
		{
			conn.Metadata.TryGetValue("Locale", out var locale);
			var template = localizationService.Get(key, locale);
			var message = MarkupTemplateFormatter.Format(template, args);
			await Notify(conn.Handle, message, sender);
		}
	}

	public ValueTask NotifyLocalizedMarkup(AnySharpObject who, string key, AnySharpObject? sender, params MString[] args)
		=> NotifyLocalizedMarkup(who.Object().DBRef, key, sender, args);

	public async ValueTask NotifyLocalizedMarkup(long handle, string key, AnySharpObject? sender, params MString[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		var template = localizationService.Get(key, locale);
		var message = MarkupTemplateFormatter.Format(template, args);
		await Notify(handle, message, sender);
	}
}
