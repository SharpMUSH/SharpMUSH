using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpContent : OneOfBase<SharpPlayer, SharpExit, SharpThing>
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


	public async ValueTask<AnySharpContainer> Home()
		=> await Match(
			async player => await player.Home.WithCancellation(CancellationToken.None),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Home.WithCancellation(CancellationToken.None)
		);
}