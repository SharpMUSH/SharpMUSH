using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Services;

public class LocationMutationCoherenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task RawLocationHandlerForwardsCancellationWithoutMutation()
	{
		var store = Substitute.For<IObjectStore>();
		var factory = new TestObjectFactory();
		var target = factory.CreateThing(20, "Target").AsContent;
		var destination = new AnySharpContainer(factory.CreateRoom(21, "Room"));
		var token = new CancellationToken(true);
		store.SetContentLocation(target, destination, token).Returns(_ => ValueTask.FromCanceled(token));
		await Assert.That(async () => await new SetObjectLocationCommandHandler(store)
			.Handle(new SetObjectLocationCommand(target, destination), token)).Throws<OperationCanceledException>();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MediatorLocationMutationsInvalidateWarmedContentsAndBothLocationIdentities(bool rawSetter)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var store = Factory.Services.GetRequiredService<IObjectStore>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		async Task<AnySharpContainer> Room(string name) => (await mediator.Send(new GetObjectNodeQuery(
			await store.CreateRoomAsync(name + Guid.NewGuid().ToString("N"), god)))).Expect<AnySharpObject>().AsContainer;
		var original = await Room("Old source");
		var destination = await Room("New source");
		var reference = await store.CreateExitAsync("Exit", [], original, god);
		var exit = (await mediator.Send(new GetObjectNodeQuery(reference))).Expect<SharpExit>();
		await mediator.CreateStream(new GetContentsQuery(original)).ToArrayAsync();
		await mediator.CreateStream(new GetContentsQuery(destination)).ToArrayAsync();
		await Assert.That((await mediator.Send(new GetLocationQuery(reference))).Expect<AnySharpContainer>().Object().DBRef)
			.IsEqualTo(original.Object().DBRef);
		await Assert.That((await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef).IsEqualTo(original.Object().DBRef);
		if (rawSetter) await mediator.Send(new SetObjectLocationCommand(new AnySharpContent(exit), destination));
		else await mediator.Send(new MoveObjectCommand(new AnySharpContent(exit), destination, original.Object().DBRef, IsSilent: true));
		await Assert.That((await mediator.CreateStream(new GetContentsQuery(original)).ToArrayAsync()).Select(item => item.Object().DBRef)).DoesNotContain(reference);
		await Assert.That((await mediator.CreateStream(new GetContentsQuery(destination)).ToArrayAsync()).Select(item => item.Object().DBRef)).Contains(reference);
		await Assert.That((await mediator.Send(new GetLocationQuery(reference))).Expect<AnySharpContainer>().Object().DBRef)
			.IsEqualTo(destination.Object().DBRef);
		await Assert.That((await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef).IsEqualTo(destination.Object().DBRef);
	}
}
