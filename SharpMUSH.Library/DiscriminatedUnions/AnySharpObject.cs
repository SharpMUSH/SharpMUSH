using OneOf;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnySharpObject(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> input)
	: OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing>(input)
{
	protected bool Equals(AnySharpObject other) => this.Object().DBRef == other.Object().DBRef;

	public override int GetHashCode() => this.Object().DBRef.GetHashCode();

	public static implicit operator AnySharpObject(SharpPlayer x) => new(x);
	public static implicit operator AnySharpObject(SharpRoom x) => new(x);
	public static implicit operator AnySharpObject(SharpExit x) => new(x);
	public static implicit operator AnySharpObject(SharpThing x) => new(x);

	public async ValueTask<AnySharpContainer> Where() => await Match<ValueTask<AnySharpContainer>>(
		async player => await player.Location.WithCancellation(CancellationToken.None),
		async room => await ValueTask.FromResult(room),
		async exit => await exit.Location.WithCancellation(CancellationToken.None),
		async thing => await thing.Location.WithCancellation(CancellationToken.None)
	);
	
	public async ValueTask<AnySharpContainer> OutermostWhere()
	{
		var where = await Where();

		for (DBRef? tmpWhere = null; where.Object().DBRef != tmpWhere;)
		{
			tmpWhere = where.Object().DBRef;
			where = await where.Location();
		}

		return where;
	}

	public string[] Aliases => Match(
		player => player.Aliases,
		room => room.Aliases,
		exit => exit.Aliases,
		thing => thing.Aliases
	) ?? [];

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

	public bool IsContent => IsPlayer || IsExit || IsThing;

	public AnySharpContent AsContent => Match<AnySharpContent>(
		player => player,
		room => throw new ArgumentException("Cannot convert a room to content."),
		exit => exit,
		thing => thing
	);

	public AnySharpContainer AsContainer => Match<AnySharpContainer>(
		player => player,
		room => room,
		exit => throw new ArgumentException("Cannot convert an exit to container."),
		thing => thing
	);

	public bool IsContainer => IsPlayer || IsRoom || IsThing;

	public SharpPlayer AsPlayer => AsT0;
	public SharpRoom AsRoom => AsT1;
	public SharpExit AsExit => AsT2;
	public SharpThing AsThing => AsT3;
}