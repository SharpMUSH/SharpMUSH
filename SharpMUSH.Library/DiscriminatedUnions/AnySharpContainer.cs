using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.DiscriminatedUnions;

[Union]
public sealed class AnySharpContainer : IUnion, IObjectShaped<AnySharpContainer>
{
	public AnySharpContainer(SharpPlayer value) => Value = value;
	public AnySharpContainer(SharpRoom value) => Value = value;
	public AnySharpContainer(SharpThing value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is AnySharpContainer other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public AnySharpObject WithExitOption() => this switch
	{
		SharpPlayer player => player,
		SharpRoom room => room,
		SharpThing thing => thing
	};

	public AnyOptionalSharpContainer WithNoneOption() => this;

	public string Id => this switch
	{
		SharpPlayer player => player.Id!,
		SharpRoom room => room.Id!,
		SharpThing thing => thing.Id!
	};

	public SharpObject Object() => this switch
	{
		SharpPlayer player => player.Object,
		SharpRoom room => room.Object,
		SharpThing thing => thing.Object
	};

	public async ValueTask<AnySharpContainer> Location() => this switch
	{
		SharpPlayer player => await player.Location.WithCancellation(CancellationToken.None),
		SharpRoom room => room,
		SharpThing thing => await thing.Location.WithCancellation(CancellationToken.None)
	};

	public IAsyncEnumerable<AnySharpContent> Content(IMediator mediator) =>
		mediator.CreateStream(new GetContentsQuery(this));

	public bool IsPlayer => Value is SharpPlayer;
	public bool IsRoom => Value is SharpRoom;
	public bool IsThing => Value is SharpThing;

	public SharpPlayer AsPlayer => Value as SharpPlayer ?? throw UnionCase.Mismatch<SharpPlayer>(Value);
	public SharpRoom AsRoom => Value as SharpRoom ?? throw UnionCase.Mismatch<SharpRoom>(Value);
	public SharpThing AsThing => Value as SharpThing ?? throw UnionCase.Mismatch<SharpThing>(Value);

	public static DBRef? RefOf(AnySharpContainer value) => value.Object().DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out AnySharpContainer value)
	{
		var container = !node.IsNone && node.Known.IsContainer;
		value = container ? node.Known.AsContainer : null!;
		return container;
	}
}
