using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// How the caching behaviours store an object-shaped result: as the dbref it names rather than the
/// instance, resolved back through the object node cache on every read.
/// </summary>
/// <remarks>
/// A contents list, a location answer and a lookup by name each name objects that the node cache
/// already holds. Storing the instance again would give the process one snapshot per result, and a
/// write would have to reach every one of them. Storing the dbref gives one instance per object,
/// the node cache's, so a handler that mutates it is seen through every result that names it, and
/// nothing needs re-pointing. The entry still carries a tag per named object: the result itself
/// (which player has this name, where this thing is) can change when that object is written.
/// </remarks>
public interface IObjectShape<T>
{
	/// <summary>The object <paramref name="value"/> names, or null when it names none.</summary>
	int? NumberOf(T value);

	/// <summary>
	/// The value for a node the cache resolved, or false when the node cannot be one: the object is
	/// gone, or is not of the type the result carries.
	/// </summary>
	bool TryFromNode(AnyOptionalSharpObject node, out T value);
}

/// <summary>What a single object-shaped result is stored as.</summary>
public sealed record CachedObjectRef(int? Number);

/// <summary>What a stream of object-shaped results is stored as.</summary>
public sealed record CachedObjectRefs(int[] Numbers);

public static class ObjectShapes
{
	/// <summary>The shape for <typeparamref name="T"/>, or null when it is not object-shaped.</summary>
	public static IObjectShape<T>? For<T>() => Cache<T>.Shape;

	private static class Cache<T>
	{
		public static readonly IObjectShape<T>? Shape = (IObjectShape<T>?)Resolve(typeof(T));
	}

	private static object? Resolve(Type type) => type switch
	{
		_ when type == typeof(SharpObject) => new ObjectShape(),
		_ when type == typeof(SharpPlayer) => new PlayerShape(),
		_ when type == typeof(AnySharpObject) => new AnyObjectShape(),
		_ when type == typeof(AnyOptionalSharpObject) => new AnyOptionalObjectShape(),
		_ when type == typeof(AnySharpContainer) => new ContainerShape(),
		_ when type == typeof(AnyOptionalSharpContainer) => new OptionalContainerShape(),
		_ when type == typeof(AnySharpContent) => new ContentShape(),
		_ => null,
	};

	private sealed class ObjectShape : IObjectShape<SharpObject>
	{
		public int? NumberOf(SharpObject value) => value.Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out SharpObject value)
		{
			value = node.IsNone ? null! : node.Known.Object();
			return !node.IsNone;
		}
	}

	private sealed class PlayerShape : IObjectShape<SharpPlayer>
	{
		public int? NumberOf(SharpPlayer value) => value.Object.Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out SharpPlayer value)
		{
			value = node.IsPlayer ? node.AsPlayer : null!;
			return node.IsPlayer;
		}
	}

	private sealed class AnyObjectShape : IObjectShape<AnySharpObject>
	{
		public int? NumberOf(AnySharpObject value) => value.Object().Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out AnySharpObject value)
		{
			value = node.IsNone ? null! : node.Known;
			return !node.IsNone;
		}
	}

	private sealed class AnyOptionalObjectShape : IObjectShape<AnyOptionalSharpObject>
	{
		public int? NumberOf(AnyOptionalSharpObject value) => value.IsNone ? null : value.Known.Object().Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpObject value)
		{
			value = node;
			return true;
		}
	}

	private sealed class ContainerShape : IObjectShape<AnySharpContainer>
	{
		public int? NumberOf(AnySharpContainer value) => value.Object().Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out AnySharpContainer value)
		{
			var container = !node.IsNone && node.Known.IsContainer;
			value = container ? node.Known.AsContainer : null!;
			return container;
		}
	}

	private sealed class OptionalContainerShape : IObjectShape<AnyOptionalSharpContainer>
	{
		public int? NumberOf(AnyOptionalSharpContainer value) => value.IsNone ? null : value.WithoutNone().Object().Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpContainer value)
		{
			if (node.IsNone)
			{
				value = new None();
				return true;
			}

			var container = node.Known.IsContainer;
			value = container ? node.Known.AsContainer.Match<AnyOptionalSharpContainer>(p => p, r => r, t => t) : null!;
			return container;
		}
	}

	private sealed class ContentShape : IObjectShape<AnySharpContent>
	{
		public int? NumberOf(AnySharpContent value) => value.Object().Key;

		public bool TryFromNode(AnyOptionalSharpObject node, out AnySharpContent value)
		{
			var content = !node.IsNone && node.Known.IsContent;
			value = content ? node.Known.AsContent : null!;
			return content;
		}
	}
}
