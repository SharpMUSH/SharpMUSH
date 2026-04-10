using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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
}
