using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
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
	/// any object whose DBRef equals that of <paramref name="expected"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static AnySharpObject MatchingObject(AnySharpObject expected) =>
		Arg.Is<AnySharpObject>((AnySharpObject o) => o.Object().DBRef == expected.Object().DBRef);

	/// <summary>
	/// Returns an NSubstitute argument matcher for <see cref="AnySharpObject"/> that matches
	/// any object whose DBRef equals <paramref name="dbRef"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static AnySharpObject MatchingObject(DBRef dbRef) =>
		Arg.Is<AnySharpObject>((AnySharpObject o) => o.Object().DBRef == dbRef);

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
	/// <c>NotifyLocalized(who, key, args)</c> carries only a <em>receiver</em> (<c>who</c>) and has
	/// no separate sender parameter — unlike the four-argument <c>Notify(receiver, msg, sender, type)</c>
	/// overload.  Use <paramref name="receiverDbRef"/> to assert the object that is being notified.
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
				c.GetMethodInfo().Name == "NotifyLocalized" &&
				c.GetArguments().Length >= 2 &&
				c.GetArguments()[1] is string k && k == key &&
				(receiverDbRef == null ||
				 (c.GetArguments()[0] is AnySharpObject obj && obj.Object().DBRef == receiverDbRef) ||
				 (c.GetArguments()[0] is DBRef d && d == receiverDbRef)) &&
				(senderDbRef == null ||
				 (c.GetArguments().Length >= 3 &&
				  c.GetArguments()[2] is AnySharpObject sObj && sObj.Object().DBRef == senderDbRef)));
}
