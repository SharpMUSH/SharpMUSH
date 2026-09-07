using SharpMUSH.Library.Extensions;
using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpContent : OneOfBase<SharpPlayer, SharpExit, SharpThing>, IObjectShaped<AnySharpContent>
{
	public AnySharpContent(OneOf<SharpPlayer, SharpExit, SharpThing> input) : base(input)
	{
	}

	public static implicit operator AnySharpContent(SharpPlayer x) => new(x);
	public static implicit operator AnySharpContent(SharpExit x) => new(x);
	public static implicit operator AnySharpContent(SharpThing x) => new(x);

	public bool IsPlayer => IsT0;
	public bool IsExit => IsT1;
	public bool IsThing => IsT2;

	public SharpPlayer AsPlayer => AsT0;
	public SharpExit AsExit => AsT1;
	public SharpThing AsThing => AsT2;

	public string Id
		=> Match(
			player => player.Id,
			exit => exit.Id,
			thing => thing.Id)!;

	public AnySharpObject WithRoomOption()
		=> Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing
		);

	public AnyOptionalSharpContent WithNoneOption()
		=> Match<AnyOptionalSharpContent>(
			player => player,
			exit => exit,
			thing => thing
		);

	public async ValueTask<AnySharpContainer> Location()
		=> await Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async exit => await exit.Location.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None)
		);


	/// <summary>
	/// Where this content goes home to. Players and things always have one; for an exit this is its
	/// destination, which is absent until <c>@link</c> gives it one.
	/// </summary>
	public async ValueTask<AnyOptionalSharpContainer> Home()
		=> await Match<ValueTask<AnyOptionalSharpContainer>>(
			async player => (await player.Home.WithCancellation(CancellationToken.None)).WithNoneOption(),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => (await thing.Home.WithCancellation(CancellationToken.None)).WithNoneOption()
		);

	public static DBRef? RefOf(AnySharpContent value) => value.Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnySharpContent value)
	{
		var content = !node.IsNone && node.Known.IsContent;
		value = content ? node.Known.AsContent : null!;
		return content;
	}
}
