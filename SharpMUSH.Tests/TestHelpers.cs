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
}
