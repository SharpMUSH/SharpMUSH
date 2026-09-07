using System.Reflection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Behaviors;

/// <summary>What a single object-shaped result is stored as: the full object id it names, or none.</summary>
public sealed record CachedObjectRef(DBRef? Ref);

/// <summary>What a stream of object-shaped results is stored as: the full object ids it names.</summary>
public sealed record CachedObjectRefs(DBRef[] Refs);

/// <summary>
/// The <see cref="IObjectShaped{TSelf}"/> members of <typeparamref name="T"/>, bound once, for the
/// caching behaviours, which are generic over every result type and cannot constrain it.
/// </summary>
public static class ObjectShaped<T>
{
	public delegate bool TryFromNodeDelegate(AnyOptionalSharpObject node, out T value);

	/// <summary>Whether <typeparamref name="T"/> names an object.</summary>
	public static readonly bool Supported;

	public static readonly Func<T, DBRef?> RefOf = null!;

	public static readonly TryFromNodeDelegate TryFromNode = null!;

	static ObjectShaped()
	{
		var shaped = typeof(T).GetInterfaces().Any(i =>
			i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IObjectShaped<>) && i.GetGenericArguments()[0] == typeof(T));
		if (!shaped)
		{
			return;
		}

		var bound = typeof(ObjectShaped<T>)
			.GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!
			.MakeGenericMethod(typeof(T))
			.Invoke(null, null)!;
		(RefOf, TryFromNode) = ((Func<T, DBRef?>, TryFromNodeDelegate))bound;
		Supported = true;
	}

	private static object Bind<TShaped>() where TShaped : IObjectShaped<TShaped>
		=> ((Func<TShaped, DBRef?>)TShaped.RefOf, new ObjectShaped<TShaped>.TryFromNodeDelegate(TShaped.TryFromNode));
}
