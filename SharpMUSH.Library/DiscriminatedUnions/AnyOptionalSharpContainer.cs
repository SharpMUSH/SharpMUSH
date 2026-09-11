using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed partial class AnyOptionalSharpContainer : IUnion, IObjectShaped<AnyOptionalSharpContainer>
{
	public AnyOptionalSharpContainer(SharpPlayer value) => Value = value;
	public AnyOptionalSharpContainer(SharpRoom value) => Value = value;
	public AnyOptionalSharpContainer(SharpThing value) => Value = value;
	public AnyOptionalSharpContainer(None value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnyOptionalSharpContainer other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom => Value is SharpRoom;
	public bool IsThing => Value is SharpThing;
	public bool IsNone => Value is None;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => Value as SharpRoom ?? throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public SharpObject? Object() => this switch
	{
		SharpPlayer player => player.Object,
		SharpRoom room => room.Object,
		SharpThing thing => thing.Object,
		None => null
	};

	public string? Id() => this switch
	{
		SharpPlayer player => player.Id,
		SharpRoom room => room.Id,
		SharpThing thing => thing.Id,
		None => null
	};

	public AnyOptionalSharpObject WithExitOption() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpThing thing => thing,
		None none => none
	};

	public AnySharpContainer WithoutNone() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpThing thing => thing,
		None => throw new Exception("Cannot convert None to a valid object.")
	};

	public static DBRef? RefOf(AnyOptionalSharpContainer value) => value.IsNone ? null : value.WithoutNone().Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnyOptionalSharpContainer value)
	{
		if (node.IsNone)
		{
			value = new None();
			return true;
		}

		var container = node.Known.IsContainer;
		value = container ? node.Known.AsContainer.WithNoneOption() : null!;
		return container;
	}
}
