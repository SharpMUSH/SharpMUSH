using OneOf;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpObject : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing>
{
	public AnySharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> input) : base(input) { }
	public static implicit operator AnySharpObject(SharpPlayer x) => new(x);
	public static implicit operator AnySharpObject(SharpRoom x) => new(x);
	public static implicit operator AnySharpObject(SharpExit x) => new(x);
	public static implicit operator AnySharpObject(SharpThing x) => new(x);

	public AnySharpContainer Where => Match(
		player => player.Location(),
		room => room,
		exit => exit.Location(),
		thing => thing.Location()
	);

	public AnySharpContainer MinusExit()
		=> Match<AnySharpContainer>(
			player => player,
			room => room,
			exit => throw new ArgumentException("Cannot convert an exit to a non-exit."),
			thing => thing
		);

	public AnySharpContent MinusRoom()
		=> Match<AnySharpContent>(
			player => player,
			room => throw new ArgumentException("Cannot convert an room to a non-room."),
			exit => exit,
			thing => thing
		);

	public bool IsPlayer => IsT0;
	public bool IsRoom => IsT1;
	public bool IsExit => IsT2;
	public bool IsThing => IsT3;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpExit AsExit => AsT2;
	public SharpThing AsThing => AsT3;
}