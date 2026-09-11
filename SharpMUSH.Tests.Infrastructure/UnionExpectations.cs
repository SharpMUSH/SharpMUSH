using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

public static class UnionExpectations
{
	/// <summary>
	/// The case a test expects a union to hold. Any other case fails the test here, naming the case it got —
	/// an <c>Error&lt;string&gt;</c> prints its message — rather than at whichever property the test reads next.
	/// A case that is itself a union is looked through, so an optional object answers
	/// <c>Expect&lt;SharpPlayer&gt;()</c> as well as <c>Expect&lt;AnySharpObject&gt;()</c>.
	/// </summary>
	public static T Expect<T>(this IUnion union, string? because = null) => union.Value switch
	{
		T value => value,
		IUnion nested => nested.Expect<T>(because),
		var other => throw new InvalidOperationException(
			$"Expected a {typeof(T).Name}{(because is null ? "" : $" because {because}")}, but the result was {other?.ToString() ?? "null"}.")
	};
}
