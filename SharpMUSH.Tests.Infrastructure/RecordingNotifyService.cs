using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests;

/// <summary>
/// The test hosts' notifier: it hands each notification to the captures the real
/// <see cref="NotifyService"/> consults (<c>@pipe</c>, inbound HTTP, the portal command route) and
/// writes it to a <see cref="TestHelpers.NotificationRecorder"/>, and keeps nothing else.
///
/// <para>The session host's notifier hears every test in the run. An NSubstitute substitute kept every
/// one of those calls, millions by the end of a full run, and each <c>Received()</c> check copied the
/// whole list; the collections that cost took several seconds, long enough for a NATS request in another
/// test to time out (#2785). Read what was said from the recorder instead.</para>
/// </summary>
public sealed class RecordingNotifyService(
	TestHelpers.NotificationRecorder recorder,
	IPipeOutputCapture? pipeCapture = null) : INotifyService
{
	private readonly HttpOutputCapture _httpCapture = new();
	// The portal command route's copy of a character's output (POST api/commands), also mirrored.
	private readonly CommandOutputCapture _commandCapture = new();
	// A piped command's output (`look ;| say %|`), taken for the next command and delivered to no one.
	private readonly IPipeOutputCapture _pipeCapture = pipeCapture ?? new PipeOutputCapture();
	private readonly LocalizationService _localization = new();

	public TestHelpers.NotificationRecorder Recorder => recorder;

	public ValueTask Notify(DBRef who, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		DeliverRaw(who, what, sender, type);
		return ValueTask.CompletedTask;
	}

	// The real service's AnySharpObject overload delegates to the DBRef overload.
	public ValueTask Notify(AnySharpObject who, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	// Handle-addressed output has no DBRef to capture against (a connect-screen socket has no object
	// behind it), so it is recorded by handle instead.
	public ValueTask Notify(long handle, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		recorder.RecordHandle(handle, PlainText(what), sender, type);
		return ValueTask.CompletedTask;
	}

	public ValueTask Notify(long[] handles, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		var text = PlainText(what);
		foreach (var handle in handles) recorder.RecordHandle(handle, text, sender, type);
		return ValueTask.CompletedTask;
	}

	public ValueTask NotifyToSession(long handle, string sessionId, SharpMessage what)
	{
		recorder.RecordHandle(handle, PlainText(what));
		return ValueTask.CompletedTask;
	}

	public ValueTask NotifyToBinding(IConnectionService.ConnectionData binding, string sessionId, SharpMessage what)
	{
		recorder.RecordHandle(binding.Handle, PlainText(what));
		return ValueTask.CompletedTask;
	}

	public ValueTask Prompt(DBRef who, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		recorder.RecordPrompt(who, PlainText(what), sender, type);
		return ValueTask.CompletedTask;
	}

	public ValueTask Prompt(AnySharpObject who, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Prompt(who.Object().DBRef, what, sender, type);

	public ValueTask PromptToSession(long handle, string sessionId, SharpMessage what, Guid? inputSession = null)
		=> ValueTask.CompletedTask;

	public ValueTask Prompt(long handle, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> ValueTask.CompletedTask;

	public ValueTask Prompt(long[] handles, SharpMessage what, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> ValueTask.CompletedTask;

	// Delivers, then hands back the error string, as the real service does: the visitor reads the
	// returned state as the command's result.
	public ValueTask<CallState> NotifyAndReturn(DBRef target, string errorReturn, string notifyMessage, bool shouldNotify)
	{
		recorder.RecordReturn(new(target, errorReturn, notifyMessage, shouldNotify));
		if (shouldNotify) Deliver(target, notifyMessage);
		return ValueTask.FromResult(new CallState(errorReturn));
	}

	public ValueTask NotifyLocalizedToSession(long handle, string sessionId, string key, params object[] args)
	{
		recorder.RecordHandle(handle, _localization.Format(key, null, args));
		return ValueTask.CompletedTask;
	}

	// Localized notifications (@include's "No such attribute: …") are formatted with the neutral locale,
	// as the real service does for a recipient without one.
	public ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args)
	{
		recorder.RecordLocalized(new(who, null, key, sender?.Object().DBRef, args));
		Deliver(who, _localization.Format(key, null, args), sender);
		return ValueTask.CompletedTask;
	}

	public ValueTask NotifyLocalized(long handle, string key, AnySharpObject? sender, params object[] args)
	{
		recorder.RecordLocalized(new(null, handle, key, sender?.Object().DBRef, args));
		recorder.RecordHandle(handle, _localization.Format(key, null, args));
		return ValueTask.CompletedTask;
	}

	// The markup-argument variant formats an MString template instead of a composite-format string
	// (@scan's match lines, for one).
	public ValueTask NotifyLocalizedMarkup(DBRef who, string key, AnySharpObject? sender, params MString[] args)
	{
		recorder.RecordLocalized(new(who, null, key, sender?.Object().DBRef,
			args.Select(arg => (object)arg.ToPlainText()).ToArray()));
		Deliver(who, MarkupTemplateFormatter.Format(_localization.Get(key, null), args).ToPlainText(), sender);
		return ValueTask.CompletedTask;
	}

	public ValueTask NotifyLocalizedMarkup(long handle, string key, AnySharpObject? sender, params MString[] args)
	{
		recorder.RecordLocalized(new(null, handle, key, sender?.Object().DBRef,
			args.Select(arg => (object)arg.ToPlainText()).ToArray()));
		return ValueTask.CompletedTask;
	}

	private void Deliver(DBRef recipient, string message, AnySharpObject? sender = null,
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
	{
		if (_pipeCapture.TryCapture(recipient.Number, MString.Plain(message))) return;
		_commandCapture.Offer(recipient.Number, message);
		_httpCapture.TryCapture(recipient.Number, message);
		recorder.RecordDelivery(recipient, message, sender, type);
	}

	// Only the Notify overloads carry the union; NotifyLocalized formats a string before it ever
	// reaches here, so there is no unflattened form of those to keep.
	private void DeliverRaw(DBRef recipient, SharpMessage message, AnySharpObject? sender,
		INotifyService.NotificationType type)
	{
		if (_pipeCapture.TryCapture(recipient.Number, message switch { MString markup => markup, string plain => MString.Plain(plain) })) return;
		var text = PlainText(message);
		_commandCapture.Offer(recipient.Number, text);
		_httpCapture.TryCapture(recipient.Number, text);
		recorder.RecordRaw(recipient, message);
		recorder.RecordDelivery(recipient, text, sender, type, message);
	}

	private static string PlainText(SharpMessage message) =>
		message switch
		{
			MString markup => markup.ToPlainText(),
			string plain => plain
		};
}
