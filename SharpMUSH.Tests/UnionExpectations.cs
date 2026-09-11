using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

/// <summary>
/// Unwraps the case of a union that a test goes on to use.
/// </summary>
/// <remarks>
/// A wrong case fails here, naming the case that came back and its value (an <c>Error&lt;string&gt;</c>
/// prints its message), instead of as a null dereference or an invalid cast further down the test.
/// Where a test only asserts the case, <c>Assert.That(x.Value).IsTypeOf&lt;T&gt;()</c> says so directly.
/// </remarks>
internal static class UnionExpectations
{
	public static T Expect<T>(this IUnion union) =>
		union.Value is T value
			? value
			: throw new InvalidOperationException(
				$"Expected the {typeof(T).Name} case of {union.GetType().Name}, got {union.Value?.ToString() ?? "no value"}.");
}
