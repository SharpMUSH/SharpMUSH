using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A container, or none. Found containers are one case, so <c>x is AnySharpContainer found</c> binds
/// the container.
/// </summary>
[Union]
public sealed class AnyOptionalSharpContainer : IUnion, IObjectShaped<AnyOptionalSharpContainer>
{
	public AnyOptionalSharpContainer(AnySharpContainer value) => Value = value;
	public AnyOptionalSharpContainer(None value) => Value = value;

	public static implicit operator AnyOptionalSharpContainer(SharpPlayer value) => new(new AnySharpContainer(value));
	public static implicit operator AnyOptionalSharpContainer(SharpRoom value) => new(new AnySharpContainer(value));
	public static implicit operator AnyOptionalSharpContainer(SharpThing value) => new(new AnySharpContainer(value));

	public object? Value { get; }

	/// <summary>
	/// Equal when both are none, or both hold containers <see cref="AnySharpContainer"/> calls equal:
	/// the same model instance.
	/// </summary>
	public override bool Equals(object? obj) => obj is AnyOptionalSharpContainer other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => this is AnySharpContainer and SharpPlayer;
	public bool IsRoom => this is AnySharpContainer and SharpRoom;
	public bool IsThing => this is AnySharpContainer and SharpThing;
	public bool IsNone => this is None;

	public SharpObject? Object() => this switch
	{
		AnySharpContainer found => found.Object(),
		None => null
	};

	public string? Id() => this switch
	{
		AnySharpContainer found => found.Id,
		None => null
	};

	public AnyOptionalSharpObject WithExitOption() => this switch
	{
		AnySharpContainer found => found.WithExitOption(),
		None none => none
	};

	public static DBRef? RefOf(AnyOptionalSharpContainer value) => value switch
	{
		AnySharpContainer found => found.Object().DBRef,
		None => null
	};

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpContainer value)
	{
		if (node is None none)
		{
			value = none;
			return true;
		}

		var isContainer = AnySharpContainer.TryFromNode(node, out var container);
		value = isContainer ? new AnyOptionalSharpContainer(container) : null!;
		return isContainer;
	}
}
