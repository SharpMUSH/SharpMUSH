using OneOf;
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
}
