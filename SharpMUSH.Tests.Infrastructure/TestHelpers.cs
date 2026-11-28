using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

/// <summary>
/// Shared helper methods for unit tests.
/// </summary>
public static class TestHelpers
{
	/// <summary>
	/// Checks if a <see cref="SharpMessage"/> contains the expected text
	/// when rendered as an ANSI string (escape codes included).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageContains(SharpMessage msg, string expected) =>
		msg switch
		{
			MString ms => ms.Render(MarkupFormat.Ansi).Contains(expected),
			string s => s.Contains(expected)
		};

	/// <summary>
	/// Checks if the plain-text content of a <see cref="SharpMessage"/> contains
	/// the expected text, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextContains(SharpMessage msg, string expected) =>
		msg switch
		{
			MString ms => ms.ToPlainText().Contains(expected),
			string s => s.Contains(expected)
		};

	/// <summary>
	/// Checks if a <see cref="SharpMessage"/> equals the expected text.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageEquals(SharpMessage msg, string expected) =>
		msg switch
		{
			MString ms => ms.ToString() == expected,
			string s => s == expected
		};

	/// <summary>
	/// Checks if the plain-text content of a <see cref="SharpMessage"/> equals
	/// the expected text, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextEquals(SharpMessage msg, string expected) =>
		msg switch
		{
			MString ms => ms.ToPlainText() == expected,
			string s => s == expected
		};

	/// <summary>
	/// Checks if the plain-text content of a <see cref="SharpMessage"/> starts with
	/// the expected prefix, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextStartsWith(SharpMessage msg, string expectedPrefix) =>
		msg switch
		{
			MString ms => ms.ToPlainText().StartsWith(expectedPrefix),
			string s => s.StartsWith(expectedPrefix)
		};

	/// <summary>
	/// Checks that a <see cref="SharpMessage"/> was sent as the plain string <paramref name="expected"/>,
	/// not as markup. NSubstitute matchers are expression trees, which cannot hold a pattern.
	/// </summary>
	public static bool MessageIsString(SharpMessage msg, string expected) => msg is string s && s == expected;

	/// <summary>
	/// Checks that a <see cref="SharpMessage"/> was sent as markup whose text is <paramref name="expected"/>.
	/// </summary>
	public static bool MessageIsMarkup(SharpMessage msg, string expected) => msg is MString ms && ms.Text == expected;

	/// <summary>
	/// Matches a notification by its TEXT, whichever form it arrived in.
	///
	/// <para>Passing a bare string to a <c>Received().Notify(...)</c> assertion pins more than the
	/// test means to: it converts to the <c>string</c> case of <see cref="SharpMessage"/> and can therefore
	/// only ever match a command that sends a plain string. Commands that carry colour send the MString case
	/// because rendering it to ANSI escapes at the call site is what left browsers printing
	/// <c>[31m</c> as text. Use this where the message content is the subject and its representation
	/// is not.</para>
	/// </summary>
	public static SharpMessage MatchingMessage(string expected) =>
		Arg.Is<SharpMessage>(m => MessagePlainTextEquals(m, expected));

	/// <summary>
	/// Returns an NSubstitute argument matcher for <see cref="AnySharpObject"/> that matches
	/// any object whose DBRef equals <paramref name="dbRef"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static AnySharpObject MatchingObject(DBRef dbRef) =>
		Arg.Is<AnySharpObject>(o => o.Object().DBRef == dbRef);

	/// <summary>
	/// Every notification the substitute was asked to deliver, bucketed by full recipient DBRef.
	///
	/// <para>The test factories share one substitute across parallel tests. Recipient buckets isolate
	/// assertions from unrelated calls in its shared history. Clearing that history would delete calls
	/// other tests are about to assert on.</para>
	///
	/// <para>Recording happens inside the delivery callback, on the thread that made the call, into a
	/// <see cref="ConcurrentQueue{T}"/> keyed by recipient. Enumeration never touches NSubstitute state,
	/// and <see cref="ConcurrentQueue{T}.ToArray"/> is a point-in-time snapshot that cannot throw while
	/// another thread enqueues. Recipient bucketing gives isolation on top of that: a test that reads its
	/// own uniquely-created player's bucket cannot see another test's notifications at all, even when
	/// parallel tests recycle the same object number with a different creation stamp.</para>
	/// </summary>
	public sealed class NotificationRecorder
	{
		public sealed record Delivery(
			DBRef Recipient,
			string Message,
			DBRef? Sender,
			INotifyService.NotificationType Type)
		{
			/// <summary>The message as the caller passed it: an MString keeps its markup.</summary>
			public SharpMessage Raw { get; init; } = Message;

			/// <summary>Whether a <c>Notify</c> call sent it, rather than a localized notification or <c>NotifyAndReturn</c>.</summary>
			public bool FromNotify { get; init; }
		}

		private readonly ConcurrentDictionary<DBRef, ConcurrentQueue<SharpMessage>> _rawByRecipient = new();
		private readonly ConcurrentDictionary<DBRef, ConcurrentQueue<Delivery>> _deliveriesByRecipient = new();
		private readonly ConcurrentQueue<Delivery> _deliveries = new();
		private readonly ConcurrentDictionary<DBRef, ConcurrentQueue<Delivery>> _promptDeliveriesByRecipient = new();
		private readonly ConcurrentDictionary<long, ConcurrentQueue<HandleDelivery>> _handleDeliveries = new();

		/// <summary>A notification sent to a connection handle, with the sender and type it was sent with.</summary>
		public sealed record HandleDelivery(long Handle, string Message, DBRef? Sender, INotifyService.NotificationType Type);
		private readonly ConcurrentDictionary<string, ConcurrentQueue<LocalizedCall>> _localizedByKey = new();

		/// <summary>
		/// A <c>NotifyLocalized</c> or <c>NotifyLocalizedMarkup</c> call: the resource key, who it was for
		/// (an object, or a connection handle), the sender when the call named one, and the format arguments
		/// (a markup argument as its plain text).
		/// </summary>
		public sealed record LocalizedCall(DBRef? Recipient, long? Handle, string Key, DBRef? Sender, object[] Args);

		/// <summary>A <c>NotifyAndReturn</c> call: whom it named, the error it returned and the message it would send.</summary>
		public sealed record ReturnCall(DBRef Target, string ErrorReturn, string NotifyMessage, bool ShouldNotify);

		private readonly ConcurrentDictionary<DBRef, ConcurrentQueue<ReturnCall>> _returnsByTarget = new();

		internal void RecordReturn(ReturnCall call)
			=> _returnsByTarget.GetOrAdd(call.Target, _ => new ConcurrentQueue<ReturnCall>()).Enqueue(call);

		/// <summary>Every <c>NotifyAndReturn</c> naming <paramref name="target"/>, in order.</summary>
		public IEnumerable<ReturnCall> ReturnsFor(DBRef target)
			=> _returnsByTarget.TryGetValue(target, out var queue) ? queue : [];

		internal void RecordLocalized(LocalizedCall call)
			=> _localizedByKey.GetOrAdd(call.Key, _ => new ConcurrentQueue<LocalizedCall>()).Enqueue(call);

		/// <summary>Every localized notification sent with resource <paramref name="key"/>, in order.</summary>
		public IEnumerable<LocalizedCall> Localized(string key)
			=> _localizedByKey.TryGetValue(key, out var queue) ? queue : [];

		/// <summary>
		/// Keeps the message exactly as the caller passed it, before it was flattened to text.
		///
		/// <para>Whether a notification arrived as an MString or as a plain string is itself the subject
		/// of some tests — rendering colour to escape characters at the call site is what left browsers
		/// printing <c>[31m</c> as literal text — and <see cref="For(DBRef)"/> cannot answer that,
		/// because by then both look the same.</para>
		/// </summary>
		internal void RecordRaw(DBRef recipient, SharpMessage message)
			=> _rawByRecipient.GetOrAdd(recipient, _ => new ConcurrentQueue<SharpMessage>()).Enqueue(message);

		internal void RecordDelivery(
			DBRef recipient,
			string message,
			AnySharpObject? sender,
			INotifyService.NotificationType type,
			SharpMessage? raw = null)
		{
			var delivery = new Delivery(recipient, message, sender?.Object().DBRef, type) { Raw = raw ?? message, FromNotify = raw is not null };
			_deliveriesByRecipient.GetOrAdd(recipient, _ => new ConcurrentQueue<Delivery>()).Enqueue(delivery);
			_deliveries.Enqueue(delivery);
		}

		/// <summary>
		/// Records output aimed at a connection rather than at an object. Socket commands (INFO,
		/// MSSP-REQUEST, SOCKSET…) answer the descriptor that typed them, and at the connect screen
		/// there is no object to key on at all, so those notifications are invisible to
		/// <see cref="For(DBRef)"/> and need their own index.
		/// </summary>
		internal void RecordHandle(long handle, string message, AnySharpObject? sender = null,
			INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		{
			_handleDeliveries.GetOrAdd(handle, _ => new ConcurrentQueue<HandleDelivery>())
				.Enqueue(new HandleDelivery(handle, message, sender?.Object().DBRef, type));
		}

		/// <summary>
		/// Records a prompt (<c>@prompt</c>, <c>prompt()</c>): text sent with a telnet GOAHEAD, through
		/// <see cref="INotifyService.Prompt(DBRef, SharpMessage, AnySharpObject?, INotifyService.NotificationType)"/>
		/// rather than <c>Notify</c>, so it is kept apart from <see cref="For(DBRef)"/>.
		/// </summary>
		internal void RecordPrompt(DBRef recipient, string message, SharpMessage raw, AnySharpObject? sender = null,
			INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		{
			_promptDeliveriesByRecipient.GetOrAdd(recipient, _ => new ConcurrentQueue<Delivery>())
				.Enqueue(new Delivery(recipient, message, sender?.Object().DBRef, type) { Raw = raw });
		}

		/// <summary>Everything <paramref name="who"/> has been notified of so far, in order.</summary>
		public List<string> For(DBRef who)
			=> _deliveriesByRecipient.TryGetValue(who, out var queue) ? [.. queue.Select(delivery => delivery.Message)] : [];

		/// <summary>How many notifications <paramref name="who"/> has had, for windowing.</summary>
		public int CountFor(DBRef who)
			=> _deliveriesByRecipient.TryGetValue(who, out var queue) ? queue.Count : 0;

		/// <summary>
		/// Waits until <paramref name="who"/> receives a message containing
		/// <paramref name="containsText"/>, or fails after <paramref name="timeout"/>.
		/// </summary>
		public async Task WaitForAsync(
			DBRef who, string containsText, TimeSpan? timeout = null,
			CancellationToken cancellationToken = default, int startIndex = 0)
		{
			await Assert.That(() => For(who).Skip(startIndex))
				.WaitsFor(messages => messages.Contains((string message) => message.Contains(containsText, StringComparison.Ordinal)),
					timeout: timeout ?? TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50),
					cancellationToken: cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current?.Execution.CancellationToken ?? default);
		}

		/// <summary>
		/// Waits until <paramref name="who"/> receives a sender-aware delivery containing
		/// <paramref name="containsText"/> from <paramref name="sender"/>.
		/// </summary>
		public async Task WaitForDeliveryAsync(
			DBRef who,
			string containsText,
			DBRef sender,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default, int startIndex = 0)
		{
			await Assert.That(() => DeliveriesFor(who).Skip(startIndex))
				.WaitsFor(deliveries => deliveries.Contains((Delivery delivery) => delivery.Sender == sender
					&& delivery.Message.Contains(containsText, StringComparison.Ordinal)),
					timeout: timeout ?? TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50),
					cancellationToken: cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current?.Execution.CancellationToken ?? default);
		}

		/// <summary>Every message <paramref name="who"/> was sent, in the form it was sent in.</summary>
		public List<SharpMessage> RawFor(DBRef who)
			=> _rawByRecipient.TryGetValue(who, out var queue) ? [.. queue] : [];

		/// <summary>How many such messages <paramref name="who"/> has had, for windowing.</summary>
		public int RawCountFor(DBRef who)
			=> _rawByRecipient.TryGetValue(who, out var queue) ? queue.Count : 0;

		/// <summary>Every notification delivered to <paramref name="who"/>, including its sender.</summary>
		public List<Delivery> DeliveriesFor(DBRef who)
			=> _deliveriesByRecipient.TryGetValue(who, out var queue) ? [.. queue] : [];

		/// <summary>
		/// Every sender-aware delivery to anyone, so a test can assert what one of its own objects did
		/// not say without reading the session-shared substitute's call list.
		/// </summary>
		public IEnumerable<Delivery> AllDeliveries()
			=> _deliveriesByRecipient.Values.SelectMany(queue => queue);

		/// <summary>How many sender-aware deliveries there have been to anyone, for windowing.</summary>
		public int DeliveryCount => _deliveries.Count;

		/// <summary>Every sender-aware delivery to anyone after the first <paramref name="count"/>, in the order they were made.</summary>
		public List<Delivery> DeliveriesSince(int count) => [.. _deliveries.Skip(count)];

		/// <summary>How many sender-aware deliveries <paramref name="who"/> has had, for windowing.</summary>
		public int DeliveryCountFor(DBRef who)
			=> _deliveriesByRecipient.TryGetValue(who, out var queue) ? queue.Count : 0;

		/// <summary>Everything sent to connection <paramref name="handle"/> so far, in order.</summary>
		public List<string> ForHandle(long handle)
			=> _handleDeliveries.TryGetValue(handle, out var queue) ? [.. queue.Select(delivery => delivery.Message)] : [];

		/// <summary>How many notifications connection <paramref name="handle"/> has had, for windowing.</summary>
		public int CountForHandle(long handle)
			=> _handleDeliveries.TryGetValue(handle, out var queue) ? queue.Count : 0;

		/// <summary>Every prompt <paramref name="who"/> has been sent so far, in order.</summary>
		public List<string> PromptsFor(DBRef who)
			=> _promptDeliveriesByRecipient.TryGetValue(who, out var queue) ? [.. queue.Select(delivery => delivery.Message)] : [];

		/// <summary>Every prompt <paramref name="who"/> has been sent so far, with its sender and type.</summary>
		public List<Delivery> PromptDeliveriesFor(DBRef who)
			=> _promptDeliveriesByRecipient.TryGetValue(who, out var queue) ? [.. queue] : [];

		/// <summary>Everything sent to connection <paramref name="handle"/>, with its sender and type.</summary>
		public List<HandleDelivery> HandleDeliveriesFor(long handle)
			=> _handleDeliveries.TryGetValue(handle, out var queue) ? [.. queue] : [];

		/// <summary>How many prompts <paramref name="who"/> has had, for windowing.</summary>
		public int PromptCountFor(DBRef who)
			=> _promptDeliveriesByRecipient.TryGetValue(who, out var queue) ? queue.Count : 0;

		/// <summary>
		/// How many deliveries matched: to <paramref name="recipient"/> (anyone when null), whose message
		/// <paramref name="message"/> accepts, from <paramref name="sender"/> and of <paramref name="type"/>
		/// when those are given.
		/// </summary>
		public int Count(DBRef? recipient, Func<SharpMessage, bool> message, DBRef? sender = null,
			INotifyService.NotificationType? type = null)
			=> (recipient is { } who ? DeliveriesFor(who) : AllDeliveries())
				.Count(d => (sender is null || d.Sender == sender) && (type is null || d.Type == type) && message(d.Raw));

		/// <summary>How many prompts to <paramref name="recipient"/> matched, as <see cref="Count"/> does for notifications.</summary>
		public int CountPrompts(DBRef recipient, Func<SharpMessage, bool> message, DBRef? sender = null,
			INotifyService.NotificationType? type = null)
			=> PromptDeliveriesFor(recipient)
				.Count(d => (sender is null || d.Sender == sender) && (type is null || d.Type == type) && message(d.Raw));

		/// <summary>How many messages connection <paramref name="handle"/> (any connection when null) was sent that <paramref name="message"/> accepts.</summary>
		public int CountForHandle(long? handle, Func<SharpMessage, bool> message)
			=> (handle is { } h ? ForHandle(h) : _handleDeliveries.Values.SelectMany(queue => queue.Select(delivery => delivery.Message)))
				.Count(text => message(text));

		/// <summary>
		/// How many localized notifications with <paramref name="key"/> matched: to <paramref name="recipient"/>
		/// or <paramref name="handle"/> and from <paramref name="sender"/> when given, with format arguments
		/// <paramref name="args"/> accepts.
		/// </summary>
		public int CountLocalized(string key, DBRef? recipient = null, long? handle = null, DBRef? sender = null,
			Func<object[], bool>? args = null)
			=> Localized(key).Count(c => (recipient is null || c.Recipient == recipient)
				&& (handle is null || c.Handle == handle)
				&& (sender is null || c.Sender == sender)
				&& (args is null || args(c.Args)));
	}

	/// <summary>The recorder behind a test host's notifier.</summary>
	public static NotificationRecorder Recorded(this INotifyService notifier)
		=> notifier is RecordingNotifyService recording
			? recording.Recorder
			: throw new InvalidOperationException("This notifier is not a test host's RecordingNotifyService.");

	/// <summary>
	/// Creates an <see cref="INotifyService"/> substitute that delivers through a
	/// <see cref="RecordingNotifyService"/> (so <c>@pipe</c>, inbound HTTP and portal-command output are
	/// captured as the real service captures them) and also keeps NSubstitute's call history, for a
	/// short-lived host whose test asserts with <c>Received()</c>. The session host uses
	/// <see cref="RecordingNotifyService"/> directly: a substitute there would keep every call of the run.
	/// </summary>
	/// <param name="recorder">
	/// Optional sink that also receives every delivered message, so a test can read what was said without
	/// enumerating <c>ReceivedCalls()</c>. See <see cref="NotificationRecorder"/>.
	/// </param>
	/// <param name="pipeCapture">
	/// The pipe capture the host registers, when the test host's <c>@pipe</c> must reach this notifier;
	/// otherwise one of its own.
	/// </param>
	public static INotifyService CreateNotifyServiceSubstitute(NotificationRecorder? recorder = null,
		SharpMUSH.Library.Services.PipeOutputCapture? pipeCapture = null)
	{
		var delivery = new RecordingNotifyService(recorder ?? new NotificationRecorder(), pipeCapture);
		var notifier = Substitute.For<INotifyService>();

		notifier.When(x => x.Notify(Arg.Any<DBRef>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Notify(call.ArgAt<DBRef>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.Notify(Arg.Any<AnySharpObject>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Notify(call.ArgAt<AnySharpObject>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.Notify(Arg.Any<long>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Notify(call.ArgAt<long>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.Notify(Arg.Any<long[]>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Notify(call.ArgAt<long[]>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.Prompt(Arg.Any<DBRef>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Prompt(call.ArgAt<DBRef>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.Prompt(Arg.Any<AnySharpObject>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => delivery.Prompt(call.ArgAt<AnySharpObject>(0), call.ArgAt<SharpMessage>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<INotifyService.NotificationType>(3)));
		notifier.When(x => x.NotifyLocalized(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()))
			.Do(call => delivery.NotifyLocalized(call.ArgAt<DBRef>(0), call.ArgAt<string>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<object[]>(3)));
		notifier.When(x => x.NotifyLocalized(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()))
			.Do(call => delivery.NotifyLocalized(call.ArgAt<long>(0), call.ArgAt<string>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<object[]>(3)));
		notifier.When(x => x.NotifyLocalizedMarkup(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<MString[]>()))
			.Do(call => delivery.NotifyLocalizedMarkup(call.ArgAt<DBRef>(0), call.ArgAt<string>(1), call.ArgAt<AnySharpObject?>(2), call.ArgAt<MString[]>(3)));
		notifier.When(x => x.NotifyToBinding(Arg.Any<IConnectionService.ConnectionData>(), Arg.Any<string>(), Arg.Any<SharpMessage>()))
			.Do(call => delivery.NotifyToBinding(call.ArgAt<IConnectionService.ConnectionData>(0), call.ArgAt<string>(1), call.ArgAt<SharpMessage>(2)));
		notifier.When(x => x.NotifyToSession(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<SharpMessage>()))
			.Do(call => delivery.NotifyToSession(call.ArgAt<long>(0), call.ArgAt<string>(1), call.ArgAt<SharpMessage>(2)));
		notifier.When(x => x.NotifyLocalizedToSession(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>()))
			.Do(call => delivery.NotifyLocalizedToSession(call.ArgAt<long>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<object[]>(3)));
		// A substitute with no configuration for NotifyAndReturn answers default(CallState), a null the
		// visitor reads as "no command ran".
		notifier.NotifyAndReturn(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
			.Returns(call => delivery.NotifyAndReturn(call.ArgAt<DBRef>(0), call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<bool>(3)));

		return notifier;
	}

	/// <summary>
	/// Polls the attribute service until the specified attribute exists on the target object,
	/// or until <paramref name="timeoutMs"/> elapses.  This replaces fragile <c>Task.Delay</c>
	/// waits for asynchronously-queued operations like @wait callbacks that set attributes.
	/// </summary>
	public static async Task WaitForAttribute(
		IAttributeService attributeService,
		AnySharpObject target,
		string attributeName,
		int timeoutMs = 10000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline)
		{
			var attr = await attributeService.GetAttributeAsync(
				target, target, attributeName,
				IAttributeService.AttributeMode.Read, false);
			if (attr.IsAttribute) return;
			await Task.Delay(100);
		}
		// Timeout reached — let the caller's assertion produce the diagnostic message
	}

	/// <summary>
	/// Checks whether a <c>NotifyLocalized</c> call with the given resource <paramref name="key"/>
	/// was received on the mock.
	/// <para>
	/// Use <paramref name="receiverDbRef"/> to assert the object being notified.
	/// Use <paramref name="senderDbRef"/> to additionally verify the sender (only matches calls
	/// using the sender-bearing <c>NotifyLocalized(who, key, sender, args)</c> overload).
	/// </para>
	/// This helper bypasses NSubstitute's params-expansion issue by inspecting
	/// <see cref="ICallRouter.ReceivedCalls"/> directly.
	/// </summary>
	/// <param name="notifyService">The mocked <see cref="INotifyService"/> instance.</param>
	/// <param name="key">The resource key passed to <c>NotifyLocalized</c>.</param>
	/// <param name="receiverDbRef">
	///   When non-null, constrains the match to calls whose first argument (the <em>receiver</em>)
	///   resolves to this <see cref="DBRef"/>.
	/// </param>
	/// <param name="senderDbRef">
	///   When non-null, constrains the match to calls using the sender-bearing overload whose
	///   third argument (the <em>sender</em>) resolves to this <see cref="DBRef"/>.
	///   Pass <see langword="null"/> to match calls regardless of sender.
	/// </param>
	public static bool ReceivedNotifyLocalizedWithKey(
		INotifyService notifyService,
		string key,
		DBRef? receiverDbRef = null,
		DBRef? senderDbRef = null) =>
		notifyService is RecordingNotifyService recording
			? recording.Recorder.Localized(key).Any(c =>
				(receiverDbRef == null || c.Recipient == receiverDbRef) &&
				(senderDbRef == null || c.Sender == senderDbRef))
			: notifyService.ReceivedCalls()
			.Any(c =>
				// Both overloads: a command whose format ARGUMENTS carry markup (an emit echoing a
				// coloured message back to its sender) routes through NotifyLocalizedMarkup, and the
				// key it was given is the same either way.
				c.GetMethodInfo().Name is "NotifyLocalized" or "NotifyLocalizedMarkup" &&
				c.GetArguments().Length >= 2 &&
				c.GetArguments()[1] is string k && k == key &&
				(receiverDbRef == null ||
				 (c.GetArguments()[0] is AnySharpObject obj && obj.Object().DBRef == receiverDbRef) ||
				 (c.GetArguments()[0] is DBRef d && d == receiverDbRef)) &&
				(senderDbRef == null ||
				 (c.GetArguments().Length >= 3 &&
					((c.GetArguments()[2] is AnySharpObject sObj && sObj.Object().DBRef == senderDbRef) ||
					 (c.GetArguments()[2] is DBRef sd && sd == senderDbRef)))));

	/// <summary>
	/// Like <see cref="ReceivedNotifyLocalizedWithKey"/>, but also renders the message so the format
	/// arguments are checked. Asserting the key alone passes even when the command formats it with the
	/// wrong values — a recipient count, say.
	/// </summary>
	public static bool ReceivedNotifyLocalizedRendering(
		INotifyService notifyService,
		string key,
		string expectedText,
		DBRef? receiverDbRef = null)
	{
		var localization = new SharpMUSH.Library.Services.LocalizationService();

		if (notifyService is RecordingNotifyService recording)
			return recording.Recorder.Localized(key)
				.Where(c => receiverDbRef == null || c.Recipient == receiverDbRef)
				.Any(c => localization.Format(key, null, c.Args) == expectedText);

		return notifyService.ReceivedCalls()
			.Where(c =>
				// NotifyLocalizedMarkup too: a format argument that carries colour arrives as an
				// MString, and the rendered sentence is what this asserts on either way.
				c.GetMethodInfo().Name is "NotifyLocalized" or "NotifyLocalizedMarkup" &&
				c.GetArguments().Length >= 2 &&
				c.GetArguments()[1] is string k && k == key &&
				(receiverDbRef == null ||
				 (c.GetArguments()[0] is AnySharpObject obj && obj.Object().DBRef == receiverDbRef) ||
				 (c.GetArguments()[0] is DBRef d && d == receiverDbRef)))
			.Select(c => c.GetArguments()[^1] switch
			{
				// The markup overload's params array is MString[]; flatten each to its text so the
				// formatted sentence compares the same as the string overload's.
				MString[] markupArgs => markupArgs.Select(m => (object)m.ToPlainText()).ToArray(),
				object[] objectArgs => objectArgs,
				_ => []
			})
			.Any(args => localization.Format(key, null, args) == expectedText);
	}

	/// <summary>
	/// Gives a hand-built object the grants a role or override would: the named system roles
	/// (<c>wizard</c>, <c>royalty</c>, ...) and an Allow on each named power (<c>See_All</c>).
	/// </summary>
	public static void Grant(this AnySharpObject obj, string[]? roles = null, string[]? powers = null)
		=> obj.Object().Grants = GrantsFor(obj.Object().Key, obj.IsPlayer, roles, powers);

	/// <summary>
	/// The grants relation for a hand-built object. A power name that is not a built-in power is
	/// ignored: those are stored on the object, so they belong in its <c>Powers</c>.
	/// </summary>
	public static AsyncRelation<ObjectGrants> GrantsFor(int key, bool isPlayer, string[]? roles = null, string[]? powers = null)
	{
		var overrides = (powers ?? []).Select(GamePowers.Find).OfType<GamePowers.Power>()
			.DistinctBy(p => p.Scope).ToDictionary(p => p.Scope, _ => PermissionState.Allow);
		var grants = ObjectGrants.For(key, isPlayer, BuiltInRoles.All, roles ?? [], overrides, null, []);
		return new(_ => Task.FromResult(grants));
	}
}
