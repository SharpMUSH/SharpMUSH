using System.Runtime.CompilerServices;
using OneOf;

namespace SharpMUSH.Tests;

/// <summary>
/// Shared helper methods for unit tests.
/// </summary>
public static class TestHelpers
{
	/// <summary>
	/// Checks if a OneOf&lt;MString, string&gt; message contains the expected text.
	/// </summary>
	/// <param name="msg">The message to check</param>
	/// <param name="expected">The expected text to find</param>
	/// <returns>True if the message contains the expected text, false otherwise</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToString().Contains(expected),
			s => s.Contains(expected));

	/// <summary>
	/// Checks if a OneOf&lt;MString, string&gt; message equals the expected text.
	/// </summary>
	/// <param name="msg">The message to check</param>
	/// <param name="expected">The expected text</param>
	/// <returns>True if the message equals the expected text, false otherwise</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool MessageEquals(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToString() == expected,
			s => s == expected);
}
