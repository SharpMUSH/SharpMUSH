using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

/// <summary>
/// Shared helper methods for unit tests.
/// </summary>
public static class TestHelpers
{
	/// <summary>
	/// Checks if a OneOf&lt;MString, string&gt; message contains the expected text
	/// when rendered as an ANSI string (escape codes included).
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToString().Contains(expected),
			s => s.Contains(expected));

	/// <summary>
	/// Checks if the plain-text content of a OneOf&lt;MString, string&gt; message contains
	/// the expected text, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText().Contains(expected),
			s => s.Contains(expected));

	/// <summary>
	/// Checks if a OneOf&lt;MString, string&gt; message equals the expected text.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageEquals(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToString() == expected,
			s => s == expected);

	/// <summary>
	/// Checks if the plain-text content of a OneOf&lt;MString, string&gt; message equals
	/// the expected text, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextEquals(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText() == expected,
			s => s == expected);

	/// <summary>
	/// Checks if the plain-text content of a OneOf&lt;MString, string&gt; message starts with
	/// the expected prefix, ignoring any ANSI escape sequences.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessagePlainTextStartsWith(OneOf<MString, string> msg, string expectedPrefix) =>
		msg.Match(
			ms => ms.ToPlainText().StartsWith(expectedPrefix),
			s => s.StartsWith(expectedPrefix));

	/// <summary>
	/// Returns an NSubstitute argument matcher for <see cref="AnySharpObject"/> that matches
	/// any object whose DBRef equals <paramref name="dbRef"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	/// <summary>
	/// Matches a notification by its TEXT, whichever form it arrived in.
	///
	/// <para>Passing a bare string to a <c>Received().Notify(...)</c> assertion pins more than the
	/// test means to: it converts to <c>OneOf.FromT1</c> and can therefore only ever match a command
	/// that sends a plain string. Commands that carry colour send <c>OneOf.FromT0</c> — an MString —
	/// because rendering it to ANSI escapes at the call site is what left browsers printing
	/// <c>[31m</c> as text. Use this where the message content is the subject and its representation
	/// is not.</para>
	/// </summary>
	public static OneOf<MString, string> MatchingMessage(string expected) =>
		Arg.Is<OneOf<MString, string>>(m => MessagePlainTextEquals(m, expected));

	public static AnySharpObject MatchingObject(DBRef dbRef) =>
		Arg.Is<AnySharpObject>(o => o.Object().DBRef == dbRef);

	/// <summary>
	/// Every notification the substitute was asked to deliver, bucketed by recipient dbref number.
	///
	/// <para>This exists because <c>ReceivedCalls()</c> must not be enumerated while the substitute is
	/// still recording: NSubstitute's threading contract requires verification and production activity to
	/// be disjoint (nsubstitute.github.io/help/threading), and the substitute the test factories install is
	/// a singleton shared by every test in a session that TUnit runs in parallel. Clearing it is worse
	/// still — it deletes calls other tests are about to assert on.</para>
	///
	/// <para>Recording happens inside the delivery callback, on the thread that made the call, into a
	/// <see cref="ConcurrentQueue{T}"/> keyed by recipient. Enumeration never touches NSubstitute state,
	/// and <see cref="ConcurrentQueue{T}.ToArray"/> is a point-in-time snapshot that cannot throw while
	/// another thread enqueues. Recipient bucketing gives isolation on top of that: a test that reads its
	/// own uniquely-created player's bucket cannot see another test's notifications at all.</para>
	/// </summary>
	public sealed class NotificationRecorder
	{
		private readonly ConcurrentDictionary<int, ConcurrentQueue<string>> _byRecipient = new();
		private readonly ConcurrentDictionary<long, ConcurrentQueue<string>> _byHandle = new();

		internal void Record(int recipient, string message)
			=> _byRecipient.GetOrAdd(recipient, _ => new ConcurrentQueue<string>()).Enqueue(message);

		/// <summary>
		/// Records output aimed at a connection rather than at an object. Socket commands (INFO,
		/// MSSP-REQUEST, SOCKSET…) answer the descriptor that typed them, and at the connect screen
		/// there is no object to key on at all, so those notifications are invisible to
		/// <see cref="For(DBRef)"/> and need their own index.
		/// </summary>
		internal void RecordHandle(long handle, string message)
			=> _byHandle.GetOrAdd(handle, _ => new ConcurrentQueue<string>()).Enqueue(message);

		/// <summary>Everything <paramref name="who"/> has been notified of so far, in order.</summary>
		public List<string> For(DBRef who)
			=> _byRecipient.TryGetValue(who.Number, out var queue) ? [.. queue] : [];

		/// <summary>How many notifications <paramref name="who"/> has had, for windowing.</summary>
		public int CountFor(DBRef who)
			=> _byRecipient.TryGetValue(who.Number, out var queue) ? queue.Count : 0;

		/// <summary>Everything sent to connection <paramref name="handle"/> so far, in order.</summary>
		public List<string> ForHandle(long handle)
			=> _byHandle.TryGetValue(handle, out var queue) ? [.. queue] : [];

		/// <summary>How many notifications connection <paramref name="handle"/> has had, for windowing.</summary>
		public int CountForHandle(long handle)
			=> _byHandle.TryGetValue(handle, out var queue) ? queue.Count : 0;
	}

	/// <summary>
	/// Creates the <see cref="INotifyService"/> substitute used by the test factories. The real
	/// <see cref="SharpMUSH.Library.Services.NotifyService"/> consults
	/// <see cref="SharpMUSH.Library.Services.Interfaces.IHttpOutputCapture"/> before delivering to
	/// connections (inbound-HTTP output becomes the response body); tests replace INotifyService
	/// with a mock, so the mock must mirror that one behavior or HTTP integration tests would see
	/// empty bodies. Received()-style assertions are unaffected — When/Do does not change call
	/// recording, and capture state lives in an AsyncLocal so non-HTTP test flows are no-ops.
	/// </summary>
	/// <param name="recorder">
	/// Optional sink that also receives every delivered message, so a test can read what was said without
	/// enumerating <c>ReceivedCalls()</c>. See <see cref="NotificationRecorder"/>.
	/// </param>
	public static INotifyService CreateNotifyServiceSubstitute(NotificationRecorder? recorder = null)
	{
		var capture = new SharpMUSH.Library.Services.HttpOutputCapture();
		var localization = new SharpMUSH.Library.Services.LocalizationService();
		var notifier = Substitute.For<INotifyService>();

		void Deliver(int recipient, string message)
		{
			capture.TryCapture(recipient, message);
			recorder?.Record(recipient, message);
		}

		void DeliverToHandle(long handle, string message) => recorder?.RecordHandle(handle, message);

		notifier
			.When(x => x.Notify(Arg.Any<DBRef>(), Arg.Any<OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => Deliver(
				call.ArgAt<DBRef>(0).Number,
				PlainText(call.ArgAt<OneOf<MString, string>>(1))));

		// The real service's AnySharpObject overload delegates to the DBRef overload; a substitute
		// does not, so hook both.
		notifier
			.When(x => x.Notify(Arg.Any<AnySharpObject>(), Arg.Any<OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => Deliver(
				call.ArgAt<AnySharpObject>(0).Object().DBRef.Number,
				PlainText(call.ArgAt<OneOf<MString, string>>(1))));

		// Localized notifications (e.g. @include's "No such attribute: …") must also reach the
		// HTTP capture, mirroring the real NotifyService — formatted with the neutral locale.
		notifier
			.When(x => x.NotifyLocalized(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<object[]>()))
			.Do(call => Deliver(
				call.ArgAt<DBRef>(0).Number,
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(2))));

		notifier
			.When(x => x.NotifyLocalized(Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<object[]>()))
			.Do(call => Deliver(
				call.ArgAt<AnySharpObject>(0).Object().DBRef.Number,
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(2))));

		notifier
			.When(x => x.NotifyLocalized(Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()))
			.Do(call => Deliver(
				call.ArgAt<DBRef>(0).Number,
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(3))));

		notifier
			.When(x => x.NotifyLocalized(Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()))
			.Do(call => Deliver(
				call.ArgAt<AnySharpObject>(0).Object().DBRef.Number,
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(3))));

		// Handle-addressed output: the descriptor overloads. These have no DBRef to capture against —
		// a connect-screen socket has no object behind it — so they are recorded by handle instead.
		notifier
			.When(x => x.Notify(Arg.Any<long>(), Arg.Any<OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call => DeliverToHandle(
				call.ArgAt<long>(0),
				PlainText(call.ArgAt<OneOf<MString, string>>(1))));

		notifier
			.When(x => x.Notify(Arg.Any<long[]>(), Arg.Any<OneOf<MString, string>>(),
				Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>()))
			.Do(call =>
			{
				var text = PlainText(call.ArgAt<OneOf<MString, string>>(1));
				foreach (var handle in call.ArgAt<long[]>(0))
				{
					DeliverToHandle(handle, text);
				}
			});

		notifier
			.When(x => x.NotifyLocalized(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<object[]>()))
			.Do(call => DeliverToHandle(
				call.ArgAt<long>(0),
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(2))));

		notifier
			.When(x => x.NotifyLocalized(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<AnySharpObject?>(), Arg.Any<object[]>()))
			.Do(call => DeliverToHandle(
				call.ArgAt<long>(0),
				localization.Format(call.ArgAt<string>(1), null, call.ArgAt<object[]>(3))));

		return notifier;
	}

	private static string PlainText(OneOf<MString, string> msg) =>
		msg.Match(ms => ms.ToPlainText(), s => s);

	/// <summary>
	/// Polls the NSubstitute <paramref name="notifyService"/> mock until a Notify call matching
	/// the given <paramref name="executor"/> DBRef and <paramref name="containsText"/> is recorded,
	/// or until <paramref name="timeoutMs"/> elapses.  This replaces fragile <c>Task.Delay</c>
	/// waits for asynchronously-queued attribute executions (e.g. @mapsql think callbacks).
	/// </summary>
	public static async Task WaitForNotification(
		INotifyService notifyService,
		DBRef executor,
		string containsText,
		int timeoutMs = 5000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline)
		{
			var calls = notifyService.ReceivedCalls();
			foreach (var call in calls)
			{
				var args = call.GetArguments();
				if (args.Length < 2) continue;
				if (args[0] is not AnySharpObject obj) continue;
				if (obj.Object().DBRef != executor) continue;
				if (args[1] is not OneOf<MString, string> msg) continue;
				var text = msg.Match(ms => ms.ToString(), s => s);
				if (text.Contains(containsText)) return;
			}
			await Task.Delay(50);
		}
		// Timeout reached — let the caller's assertion produce the diagnostic message
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
		notifyService.ReceivedCalls()
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
				MString[] markupArgs => markupArgs.Select(m => (object)MModule.plainText(m)).ToArray(),
				object[] objectArgs => objectArgs,
				_ => []
			})
			.Any(args => localization.Format(key, null, args) == expectedText);
	}
}
