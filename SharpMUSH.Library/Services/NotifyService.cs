using MarkupString;
using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data.
/// </summary>
public class NotifyService(
	IMessageBus publishEndpoint,
	IConnectionService connections,
	ILocalizationService localizationService,
	IRealityPolicy reality,
	IListenerRoutingService? listenerRoutingService = null,
	IMediator? mediator = null,
	IHttpOutputCapture? httpOutputCapture = null,
	IOptionsWrapper<SharpMUSHOptions>? configuration = null) : INotifyService, IContextualNotifyService
{
	public NotifyService(IMessageBus publishEndpoint, IConnectionService connections, ILocalizationService localizationService,
		IRealityPolicy reality, IListenerRoutingService? listenerRoutingService, IMediator? mediator, IHttpOutputCapture? httpOutputCapture)
		: this(publishEndpoint, connections, localizationService, reality, listenerRoutingService, mediator, httpOutputCapture, null) { }

	/// <summary>Retains the published constructor for legacy callers, with reality filtering disabled.</summary>
	public NotifyService(IMessageBus publishEndpoint, IConnectionService connections,
		ILocalizationService localizationService, IListenerRoutingService? listenerRoutingService = null,
		IMediator? mediator = null, IHttpOutputCapture? httpOutputCapture = null)
		: this(publishEndpoint, connections, localizationService, DisabledRealityPolicy.Instance,
			listenerRoutingService, mediator, httpOutputCapture)
	{ }

	// A notification caches only perception results, never a connection binding.
	private async ValueTask<bool> CanReceiveBound(long handle, DBRef intended, AnySharpObject? sender, Dictionary<DBRef, bool> perceptions)
	{
		if (connections.Get(handle)?.Ref is not { } current || !current.Equals(intended)) return false;
		if (!perceptions.TryGetValue(current, out var allowed))
			perceptions[current] = allowed = await CanReceive(current, sender);
		return allowed && connections.Get(handle)?.Ref is { } latest && latest.Equals(intended);
	}

	private async ValueTask<bool> CanReceiveHandle(long handle, AnySharpObject? sender)
	{
		var initial = connections.Get(handle);
		var reference = initial?.Ref;
		var session = initial?.Metadata.GetValueOrDefault("SessionId");
		if (!await CanReceive(reference, sender)) return false;
		var latest = connections.Get(handle);
		return Nullable.Equals(reference, latest?.Ref)
			&& string.Equals(session, latest?.Metadata.GetValueOrDefault("SessionId"), StringComparison.Ordinal);
	}

	private async ValueTask<bool> CanReceive(DBRef? receiver, AnySharpObject? sender)
	{
		if (sender is null) return true;
		if (receiver is null) return !await reality.IsEnabledAsync(ExecutionBudget.CurrentToken);
		return await reality.CanPerceiveAsync(receiver.Value, sender.Object().DBRef, ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// One message on its way out. The serialized form is computed once and shared by every
	/// recipient whose connection wraps nothing around it, so a message to a player with several
	/// connections, or to a whole room, is serialized once rather than once per handle.
	/// </summary>
	private sealed class Outgoing(MString text)
	{
		private string? _serialized;

		public MString Text => text;

		public string Serialized => _serialized ??= MarkupTextSerializer.Serialize(text);
	}

	private static Outgoing Prepare(SharpMessage what)
		=> new(what switch
		{
			MString markup => markup,
			string str => MarkupText.Plain(str)
		});

	private async ValueTask<Outgoing> PrepareRecipient(Outgoing outgoing, DBRef? recipient, AnySharpObject? sender, INotifyService.NotificationType type)
	{
		if (outgoing.Text.Length == 0 || sender is null || recipient is null || mediator is null
			|| type is INotifyService.NotificationType.Announce or INotifyService.NotificationType.NSAnnounce
				or INotifyService.NotificationType.NSEmit or INotifyService.NotificationType.NSPrivateEmit) return outgoing;
		if (await mediator.Send(new GetObjectNodeQuery(recipient.Value), ExecutionBudget.CurrentToken) is not AnySharpObject obj) return outgoing;
		var header = await NameFormatter.HeaderAsync(obj, sender, type, configuration?.CurrentValue.Command.FullInvisibility ?? false);
		return header.Length == 0 ? outgoing : new Outgoing(MString.Concat(header, outgoing.Text));
	}

	private async ValueTask PublishToHandle(long handle, Outgoing raw, AnySharpObject? sender, INotifyService.NotificationType type, bool prompt,
		Dictionary<DBRef, Outgoing>? prepared = null)
	{
		var initial = connections.Get(handle);
		var reference = initial?.Ref;
		var session = initial?.Metadata.GetValueOrDefault("SessionId");
		Outgoing outgoing;
		if (reference is { } recipient && prepared is not null)
		{
			if (!prepared.TryGetValue(recipient, out var cached))
				prepared[recipient] = cached = await PrepareRecipient(raw, recipient, sender, type);
			outgoing = cached;
		}
		else outgoing = await PrepareRecipient(raw, reference, sender, type);
		if (!await CanReceiveHandle(handle, sender)) return;
		var current = connections.Get(handle);
		if (!Nullable.Equals(reference, current?.Ref)
			|| !string.Equals(session, current?.Metadata.GetValueOrDefault("SessionId"), StringComparison.Ordinal)) return;
		if (prompt) await PublishMarkupPrompt(handle, outgoing, session);
		else await PublishMarkup(handle, outgoing, session);
	}

	private static bool IsEmpty(SharpMessage what)
		=> what switch
		{
			MString markup => markup.Length == 0,
			string str => str.Length == 0
		};

	/// <summary>
	/// Publishes output to a single connection as serialized markup. The ConnectionServer owns the
	/// wire format (ANSI/Pueblo/MXP for terminals, a markup envelope for portal/WebSocket clients),
	/// so the markup is kept as an <see cref="MString"/> here and only serialized for transport.
	/// </summary>
	private ValueTask PublishMarkup(long handle, Outgoing outgoing, string? sessionId = null)
	{
		var wrapped = ApplyOutputPrefixSuffix(handle, outgoing.Text);
		var serialized = ReferenceEquals(wrapped, outgoing.Text)
			? outgoing.Serialized
			: MarkupTextSerializer.Serialize(wrapped);
		return new ValueTask(publishEndpoint.HandlePublish(new MarkupOutputMessage(handle, serialized) { SessionId = sessionId }, ExecutionBudget.CurrentToken));
	}

	/// <summary>
	/// Publishes prompt output to a single connection as serialized markup. Prompts are not wrapped
	/// with OUTPUTPREFIX/OUTPUTSUFFIX and carry no trailing newline.
	/// </summary>
	private ValueTask PublishMarkupPrompt(long handle, Outgoing outgoing, string? sessionId = null)
		=> new(publishEndpoint.HandlePublish(new MarkupPromptMessage(handle, outgoing.Serialized) { SessionId = sessionId }, ExecutionBudget.CurrentToken));

	/// <summary>
	/// Wraps markup with OUTPUTPREFIX / OUTPUTSUFFIX if set on the connection, keeping everything as
	/// an <see cref="MString"/>. Mirrors PennMUSH's per-command output wrapping (src/bsd.c): the
	/// prefix is emitted as a separate line before the output and the suffix as a separate line
	/// after. Line-ending normalization is the ConnectionServer's responsibility at render time.
	/// Returns <paramref name="text"/> itself when the connection wraps nothing.
	/// </summary>
	private MString ApplyOutputPrefixSuffix(long handle, MString text)
	{
		var conn = connections.Get(handle);
		if (conn is null)
		{
			return text;
		}

		var hasPrefix = conn.Metadata.TryGetValue("OutputPrefix", out var prefix) && !string.IsNullOrEmpty(prefix);
		var hasSuffix = conn.Metadata.TryGetValue("OutputSuffix", out var suffix) && !string.IsNullOrEmpty(suffix);

		if (!hasPrefix && !hasSuffix)
		{
			return text;
		}

		var parts = new List<MString>(5);
		if (hasPrefix)
		{
			parts.Add(MarkupText.Plain(prefix!));
			parts.Add(MarkupText.Plain("\n"));
		}
		parts.Add(text);
		if (hasSuffix)
		{
			parts.Add(MarkupText.Plain("\n"));
			parts.Add(MarkupText.Plain(suffix!));
		}
		return MarkupText.Concat(parts);
	}

	private async ValueTask<bool> PrepareObjectNotification(DBRef who, SharpMessage what, AnySharpObject? sender,
		INotifyService.NotificationType type, bool prompt, NotificationContext? context = null)
	{
		if (context?.Exclusions.Contains(who) == true) return false;
		if (!await CanReceive(who, sender)) return false;
		if (!prompt && IsEmpty(what) && (context is null
			|| context.Relay == NotificationRelay.Initial && context.Prefix.Length == 0))
		{
			return false;
		}

		// Inbound HTTP: while the http_handler's <METHOD> attribute runs, everything emitted to
		// the handler becomes the HTTP response body instead of going to a (nonexistent)
		// connection — PennMUSH's CONN_HTTP_BUFFER hijack (src/notify.c queue_newwrite).
		if (!prompt && httpOutputCapture?.TryCapture(who.Number,
				context is not null ? MString.Concat(context.Prefix, AsMarkup(what)).ToPlainText() : what switch
				{
					MString markupString => markupString.ToPlainText(),
					string str => str
				}) == true)
		{
			return false;
		}

		if (listenerRoutingService != null && mediator != null && sender != null)
		{
			try
			{
				var location = context?.Location ?? (sender switch
				{
					SharpPlayer player => (await player.Location.WithCancellation(ExecutionBudget.CurrentToken)).Object().DBRef,
					SharpRoom room => room.Object.DBRef,
					SharpExit exit => (await exit.Location.WithCancellation(ExecutionBudget.CurrentToken)).Object().DBRef,
					SharpThing thing => (await thing.Location.WithCancellation(ExecutionBudget.CurrentToken)).Object().DBRef
				});

				var notificationContext = context is not null ? context with { Location = location } : new NotificationContext(
					Target: who,
					Location: location,
					ExcludedObjects: []
				);

				await listenerRoutingService.ProcessNotificationAsync(notificationContext, what, sender, type);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// A listener that fails must not stop the message reaching the people it was sent to,
				// which is why this is swallowed at all. A cancellation is not that: it means the caller
				// is gone, and reading it as "routing failed, carry on delivering" hides a shutdown or an
				// abandoned request behind a message nobody is waiting for.
			}
		}

		return true;
	}

	public async ValueTask Notify(DBRef who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await DeliverObjectAsync(who, AsMarkup(what), sender, type, false);

	private static MString AsMarkup(SharpMessage message) => message switch
	{
		MString markup => markup,
		string text => MString.Plain(text)
	};

	public ValueTask NotifyContextAsync(NotificationContext context, MString body, AnySharpObject? speaker,
		INotifyService.NotificationType type, bool prompt = false)
		=> DeliverObjectAsync(context.Target, body, speaker, type, prompt, context);

	private async ValueTask DeliverObjectAsync(DBRef who, MString body, AnySharpObject? sender,
		INotifyService.NotificationType type, bool prompt, NotificationContext? context = null)
	{
		if (!await PrepareObjectNotification(who, body, sender, type, prompt, context)) return;
		var delivered = context is null ? body : MString.Concat(context.Prefix, body);
		var outgoing = await PrepareRecipient(Prepare(delivered), who, sender, type);
		var perceptions = new Dictionary<DBRef, bool> { [who] = true };
		await foreach (var conn in connections.Get(who))
		{
			if (!await CanReceiveBound(conn.Handle, who, sender, perceptions)) continue;
			if (prompt) await PublishMarkupPrompt(conn.Handle, outgoing);
			else await PublishMarkup(conn.Handle, outgoing);
		}
	}

	public ValueTask Notify(AnySharpObject who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (IsEmpty(what))
		{
			return;
		}

		await PublishToHandle(handle, Prepare(what), sender, type, prompt: false);
	}

	public async ValueTask Notify(long[] handles, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (IsEmpty(what))
		{
			return;
		}

		var raw = Prepare(what);
		var prepared = new Dictionary<DBRef, Outgoing>();
		foreach (var handle in handles)
		{
			await PublishToHandle(handle, raw, sender, type, prompt: false, prepared);
		}
	}

	public async ValueTask Prompt(DBRef who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> await DeliverObjectAsync(who, AsMarkup(what), sender, type, true);

	public ValueTask Prompt(AnySharpObject who, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public ValueTask PromptToSession(long handle, string sessionId, SharpMessage what)
		=> PublishMarkupPrompt(handle, Prepare(what), sessionId);

	public async ValueTask Prompt(long handle, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		await PublishToHandle(handle, Prepare(what), sender, type, prompt: true);
	}


	public async ValueTask Prompt(long[] handles, SharpMessage what, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		var raw = Prepare(what);
		var prepared = new Dictionary<DBRef, Outgoing>();
		foreach (var handle in handles)
		{
			await PublishToHandle(handle, raw, sender, type, prompt: true, prepared);
		}
	}

	public async ValueTask NotifyExcept(DBRef who, SharpMessage what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (IsEmpty(what))
		{
			return;
		}

		var excludeHandles = await except.ToAsyncEnumerable()
			.SelectMany(dbRef => connections.Get(dbRef))
			.Select(conn => conn.Handle)
			.ToHashSetAsync();

		if (!await CanReceive(who, sender)) return;
		var outgoing = await PrepareRecipient(Prepare(what), who, sender, type);
		var perceptions = new Dictionary<DBRef, bool> { [who] = true };
		await foreach (var conn in connections.Get(who))
		{
			if (!excludeHandles.Contains(conn.Handle) && await CanReceiveBound(conn.Handle, who, sender, perceptions))
				await PublishMarkup(conn.Handle, outgoing);
		}
	}

	public ValueTask NotifyExcept(AnySharpObject who, SharpMessage what, DBRef[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, except, sender, type);

	public ValueTask NotifyExcept(AnySharpObject who, SharpMessage what, AnySharpObject[] except, AnySharpObject? sender, INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> NotifyExcept(who.Object().DBRef, what, Array.ConvertAll(except, x => x.Object().DBRef), sender, type);

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

	/// <summary>
	/// HTTP output capture for localized notifications: these resolve per-connection (locale),
	/// so without this check a localized message to a connectionless http_handler would silently
	/// vanish instead of joining the response body (e.g. @include's "No such attribute: …").
	/// Captured text uses the neutral locale.
	/// </summary>
	private bool TryCaptureLocalized(DBRef who, string key, object[] args)
		=> httpOutputCapture?.TryCapture(who.Number, localizationService.Format(key, null, args)) == true;

	public ValueTask NotifyLocalized(DBRef who, string key, params object[] args)
		=> NotifyLocalized(who, key, sender: null, args: args);

	public ValueTask NotifyLocalized(AnySharpObject who, string key, params object[] args)
		=> NotifyLocalized(who.Object().DBRef, key, args);

	public async ValueTask NotifyLocalized(long handle, string key, params object[] args)
	{
		var conn = connections.Get(handle);
		var locale = conn is not null && conn.Metadata.TryGetValue("Locale", out var l) ? l : null;
		var message = localizationService.Format(key, locale, args);
		await Notify(handle, message, sender: null);
	}

	public async ValueTask NotifyLocalizedToSession(long handle, string sessionId, string key, params object[] args)
	{
		var connection = connections.Get(handle);
		if (connection is null || (connection.Metadata.GetValueOrDefault("SessionId") ?? "") != sessionId) return;
		var locale = connection.Metadata.GetValueOrDefault("Locale");
		var message = localizationService.Format(key, locale, args);
		await PublishMarkup(handle, Prepare(message), sessionId);
	}

	public async ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args)
	{
		if (!await CanReceive(who, sender)) return;
		if (TryCaptureLocalized(who, key, args))
		{
			return;
		}

		var perceptions = new Dictionary<DBRef, bool> { [who] = true };
		await foreach (var conn in connections.Get(who))
		{
			if (!await CanReceiveBound(conn.Handle, who, sender, perceptions)) continue;
			conn.Metadata.TryGetValue("Locale", out var locale);
			var message = localizationService.Format(key, locale, args);
			if (message.Length > 0) await PublishMarkup(conn.Handle, Prepare(message));
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
		if (!await CanReceive(who, sender)) return;
		if (httpOutputCapture is not null)
		{
			var neutral = MarkupTemplateFormatter.Format(localizationService.Get(key, null), args);
			if (httpOutputCapture.TryCapture(who.Number, neutral.ToPlainText()))
			{
				return;
			}
		}

		var perceptions = new Dictionary<DBRef, bool> { [who] = true };
		await foreach (var conn in connections.Get(who))
		{
			if (!await CanReceiveBound(conn.Handle, who, sender, perceptions)) continue;
			conn.Metadata.TryGetValue("Locale", out var locale);
			var template = localizationService.Get(key, locale);
			var message = MarkupTemplateFormatter.Format(template, args);
			if (message.Length > 0) await PublishMarkup(conn.Handle, Prepare(message));
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
