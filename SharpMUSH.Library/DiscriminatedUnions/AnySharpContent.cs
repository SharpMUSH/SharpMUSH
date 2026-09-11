using System.Runtime.CompilerServices;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class AnySharpContent : IUnion, IObjectShaped<AnySharpContent>
{
	public AnySharpContent(SharpPlayer value) => Value = value;
	public AnySharpContent(SharpExit value) => Value = value;
	public AnySharpContent(SharpThing value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnySharpContent other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsExit => Value is SharpExit;
	public bool IsThing => Value is SharpThing;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpExit AsExit => Value as SharpExit ?? throw UnionCase.Mismatch<SharpExit>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public string Id => this switch
	{
		SharpPlayer player => player.Id!,
		SharpExit exit => exit.Id!,
		SharpThing thing => thing.Id!
	};

	public SharpObject Object() => this switch
	{
		SharpPlayer player => player.Object,
		SharpExit exit => exit.Object,
		SharpThing thing => thing.Object
	};

	public AnySharpObject WithRoomOption() => this switch
	{
		SharpPlayer player => player,
		SharpExit exit => exit,
		SharpThing thing => thing
	};

	public AnyOptionalSharpContent WithNoneOption() => this;

	public async ValueTask<AnySharpContainer> Location() => this switch
	{
		SharpPlayer player => await player.Location.WithCancellation(CancellationToken.None),
		SharpExit exit => await exit.Location.WithCancellation(CancellationToken.None),
		SharpThing thing => await thing.Location.WithCancellation(CancellationToken.None)
	};

	/// <summary>
	/// Where this content goes home to. Players and things always have one; for an exit this is its
	/// destination, which is absent until <c>@link</c> gives it one.
	/// </summary>
	public async ValueTask<AnyOptionalSharpContainer> Home() => this switch
	{
		SharpPlayer player => (await player.Home.WithCancellation(CancellationToken.None)).WithNoneOption(),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None),
		SharpThing thing => (await thing.Home.WithCancellation(CancellationToken.None)).WithNoneOption()
	};

	public static DBRef? RefOf(AnySharpContent value) => value.Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnySharpContent value)
	{
		var content = !node.IsNone && node.Known.IsContent;
		value = content ? node.Known.AsContent : null!;
		return content;
	}
}
