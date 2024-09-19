using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpContainer : OneOfBase<SharpPlayer, SharpRoom, SharpThing>
{
	public AnySharpContainer(OneOf<SharpPlayer, SharpRoom, SharpThing> input) : base(input) { }
	public static implicit operator AnySharpContainer(SharpPlayer x) => new(x);
	public static implicit operator AnySharpContainer(SharpRoom x) => new(x);
	public static implicit operator AnySharpContainer(SharpThing x) => new(x);

	public AnySharpObject WithExitOption()
		=> Match<AnySharpObject>(
			player => player,
			room => room,
			thing => thing
		);

	public string Id => Match(
		player => player.Id!,
		room => room.Id!,
		thing => thing.Id!
	);

	public bool IsPlayer => IsT0;
	public bool IsRoom => IsT1;
	public bool IsThing => IsT2;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpThing AsThing => AsT2;
}