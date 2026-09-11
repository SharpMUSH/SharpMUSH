using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Commands;

public class NoSpoofEmitRealityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(true, false, true, true)]
	[Arguments(true, true, false, true)]
	[Arguments(true, true, true, false)]
	[Arguments(false, false, false, true)]
	public async Task HearingUsesEnactorWhileInteractionLockRetainsExecutor(
		bool enabled, bool executorVisible, bool enactorVisible, bool lockAllowed)
	{
		var objects = new TestObjectFactory();
		var room = objects.CreateRoom(70, "room");
		var executor = objects.CreatePlayer(71, "executor", room);
		var enactor = objects.CreatePlayer(72, "enactor", room);
		var recipient = objects.CreatePlayer(73, "recipient", room);
		executor.Expect<SharpPlayer>().Id = "executor";
		enactor.Expect<SharpPlayer>().Id = "enactor";
		recipient.Expect<SharpPlayer>().Id = "recipient";
		executor.Object().Powers = new(() => new[] { new SharpPower
		{
			Name = "Can_Spoof", Alias = "", System = true,
			SetPermissions = [], UnsetPermissions = [], TypeRestrictions = []
		} }.ToAsyncEnumerable());
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(recipient.Object().DBRef, executor.Object().DBRef).Returns(executorVisible);
		reality.CanPerceiveAsync(recipient.Object().DBRef, enactor.Object().DBRef).Returns(enactorVisible);
		var locks = Substitute.For<ILockService>();
		locks.Evaluate(LockType.Interact, recipient, executor).Returns(lockAllowed);
		locks.Evaluate(LockType.Interact, recipient, enactor).Returns(!lockAllowed);
		var permissions = new PermissionService(locks, Substitute.For<IOptionsMonitor<SharpMUSHOptions>>(),
			enabled ? reality : DisabledRealityPolicy.Instance);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			new AnyOptionalSharpObject(call.Arg<GetObjectNodeQuery>().DBRef.Equals(executor.Object().DBRef)
				? executor : enactor));
		mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[] { recipient.MinusRoom() }.ToAsyncEnumerable());
		var notifications = Substitute.For<INotifyService>();
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
			Factory.Services, mediator, permissions, notifications);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CurrentState.Returns(ParserState.RootFor(executor.Object().DBRef) with
		{
			Enactor = enactor.Object().DBRef,
			Switches = ["NOEVAL"],
			Arguments = new() { ["0"] = new CallState("literal [add(1,2)]") }
		});

		await commands.NoSpoofEmit(parser, new SharpCommandAttribute { Name = "@NSEMIT" });

		var delivered = (!enabled || enactorVisible) && lockAllowed;
		await notifications.Received(delivered ? 1 : 0).Notify(recipient,
			TestHelpers.MatchingMessage("literal [add(1,2)]"), enactor, INotifyService.NotificationType.NSEmit);
		await locks.DidNotReceive().Evaluate(LockType.Interact, recipient, enactor);
	}
}
