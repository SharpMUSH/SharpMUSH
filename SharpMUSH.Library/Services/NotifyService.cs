using MarkupString;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Text;

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
	/// Normalizes line endings by replacing all \n with \r\n and ensuring trailing \r\n
	/// </summary>
	private static string NormalizeLineEnding(string text)
	{
		text = text.Replace("\r\n", "\n");
		text = text.Replace("\n", "\r\n");
		text = text.TrimEnd('\r', '\n');
		return text;
	}

	private string ApplyOutputPrefixSuffix(long handle, string text)
	{
		var conn = connections.Get(handle);
		if (conn is null) return text;

		var hasPrefix = conn.Metadata.TryGetValue("OutputPrefix", out var prefix);
		var hasSuffix = conn.Metadata.TryGetValue("OutputSuffix", out var suffix);
		if (!hasPrefix && !hasSuffix) return text;

		var sb = new StringBuilder();
		if (hasPrefix && !string.IsNullOrEmpty(prefix)) { sb.Append(NormalizeLineEnding(prefix)); sb.Append("\r\n"); }
		sb.Append(text);
		if (hasSuffix && !string.IsNullOrEmpty(suffix)) { sb.Append("\r\n"); sb.Append(NormalizeLineEnding(suffix)); }
		return sb.ToString();
	}

	public async ValueTask Notify(DBRef who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;

		if (listenerRoutingService != null && mediator != null && sender != null)
		{
			try
			{
				var location = sender.Value switch
				{
					SharpPlayer p => (await p.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					SharpRoom r   => r.Object.DBRef,
					SharpExit e   => (await e.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					SharpThing t  => (await t.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
					_             => throw new InvalidOperationException()
				};
				var notificationContext = new NotificationContext(Target: who, Location: location, IsRoomBroadcast: false, ExcludedObjects: []);
				await listenerRoutingService.ProcessNotificationAsync(notificationContext, what, sender, type);
			}
			catch { }
		}

		var text = NormalizeLineEnding(what.ToString());
		await foreach (var conn in connections.Get(who))
		{
			var wrapped = ApplyOutputPrefixSuffix(conn.Handle, text);
			await publishEndpoint.HandlePublish(new TelnetOutputMessage(conn.Handle, Encoding.UTF8.GetBytes(wrapped)));
		}
	}

	public ValueTask Notify(AnySharpObject who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;
		var text = NormalizeLineEnding(ApplyOutputPrefixSuffix(handle, what.ToString()));
		await publishEndpoint.HandlePublish(new TelnetOutputMessage(handle, Encoding.UTF8.GetBytes(text)));
	}

	public async ValueTask Notify(long[] handles, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;
		var text = NormalizeLineEnding(what.ToString());
		foreach (var handle in handles)
		{
			var wrapped = ApplyOutputPrefixSuffix(handle, text);
			await publishEndpoint.HandlePublish(new TelnetOutputMessage(handle, Encoding.UTF8.GetBytes(wrapped)));
		}
	}

	public async ValueTask Prompt(DBRef who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;
		var bytes = Encoding.UTF8.GetBytes(what.ToString());
		await foreach (var handle in connections.Get(who).Select(x => x.Handle))
			await publishEndpoint.HandlePublish(new TelnetPromptMessage(handle, bytes));
	}

	public ValueTask Prompt(AnySharpObject who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public async ValueTask Prompt(long handle, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await Prompt([handle], what, sender, type);

	public async ValueTask Prompt(long[] handles, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;
		var bytes = Encoding.UTF8.GetBytes(what.ToString());
		foreach (var handle in handles)
			await publishEndpoint.HandlePublish(new TelnetPromptMessage(handle, bytes));
	}

	public async ValueTask NotifyExcept(DBRef who, SharpMessage what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (what.IsEmpty) return;
		var targetHandles = await connections.Get(who).Select(x => x.Handle).ToArrayAsync();
		var excludeHandles = await except.ToAsyncEnumerable()
			.SelectMany(dbRef => connections.Get(dbRef))
			.Select(conn => conn.Handle)
			.ToHashSetAsync();
		var notifyHandles = targetHandles.Where(h => !excludeHandles.Contains(h)).ToArray();
		if (notifyHandles.Length > 0) await Notify(notifyHandles, what, sender, type);
	}

	public ValueTask NotifyExcept(AnySharpObject who, SharpMessage what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public async ValueTask NotifyExcept(AnySharpObject who, SharpMessage what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await NotifyExcept(who.Object().DBRef, what, except.Select(x => x.Object().DBRef).ToArray(), sender, type);

	public async ValueTask<CallState> NotifyAndReturn(DBRef target, string errorReturn, string notifyMessage, bool shouldNotify)
	{
		if (shouldNotify) await Notify(target, notifyMessage, sender: null);
		return new CallState(errorReturn);
	}

	public async ValueTask NotifyLocalized(DBRef who, string key, params object[] args)
	{
		await foreach (var conn in connections.Get(who))
		{
			conn.Metadata.TryGetValue("Locale", out var locale);
			await Notify(conn.Handle, localizationService.Format(key, locale, args), sender: null);
		}
	}

	public ValueTask NotifyLocalized(AnySharpObject who, string key, params object[] args)
		=> NotifyLocalized(who.Object().DBRef, key, args);

	public async ValueTask NotifyLocalized(long handle, string key, params object[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		await Notify(handle, localizationService.Format(key, locale, args), sender: null);
	}

	public async ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args)
	{
		await foreach (var conn in connections.Get(who))
		{
			conn.Metadata.TryGetValue("Locale", out var locale);
			await Notify(conn.Handle, localizationService.Format(key, locale, args), sender: sender);
		}
	}

	public ValueTask NotifyLocalized(AnySharpObject who, string key, AnySharpObject? sender, params object[] args)
		=> NotifyLocalized(who.Object().DBRef, key, sender, args);

	public async ValueTask NotifyLocalized(long handle, string key, AnySharpObject? sender, params object[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		await Notify(handle, localizationService.Format(key, locale, args), sender: sender);
	}
}
