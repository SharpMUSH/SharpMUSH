using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

public static class UnionExpectations
{
	/// <summary>
	/// The case a test expects a union to hold. Any other case fails the test here, naming the case it got —
	/// an <c>Error&lt;string&gt;</c> prints its message — rather than at whichever property the test reads next.
	/// </summary>
	public static T Expect<T>(this IUnion union, string? because = null)
		=> union.Value is T value
			? value
			: throw new InvalidOperationException(
				$"Expected a {typeof(T).Name}{(because is null ? "" : $" because {because}")}, but the result was {union.Value?.ToString() ?? "null"}.");
}
