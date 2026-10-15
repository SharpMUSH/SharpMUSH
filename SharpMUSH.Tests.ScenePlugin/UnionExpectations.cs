using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>Binds the case a test expects, the way Tests.Infrastructure's <c>Expect</c> does.</summary>
internal static class UnionExpectations
{
	public static T Expect<T>(this IUnion union) => union.Value switch
	{
		T value => value,
		var other => throw new InvalidOperationException($"Expected a {typeof(T).Name}, but the result was {other}.")
	};
}
