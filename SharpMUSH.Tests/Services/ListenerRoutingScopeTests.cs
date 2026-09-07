using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Which objects one notification's listener pass evaluates.
///
/// <para>It used to be every object in the sender's room, for every notification — and a room
/// broadcast is one notification per occupant, so a room of N ran the pass N times over the same N
/// objects. Quadratic in occupancy, and every listener fired N times for one <c>say</c>.</para>
///
/// <para>A notification is heard by the object it is addressed to. That object's listeners are the
/// ones that run, and the broadcast that produced N notifications evaluates each occupant once.</para>
/// </summary>
public class ListenerRoutingScopeTests
{
	private readonly TestObjectFactory _factory = new();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IAttributeService _attributes = Substitute.For<IAttributeService>();
	private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
	private readonly IListenPatternMatcher _patterns = Substitute.For<IListenPatternMatcher>();

	private SharpRoom Room => _factory.CreateRoom(100, "The Room");

	private AnySharpObject Occupant(int number) => _factory.CreateThing(number, $"Occupant {number}", Room);

	private ListenerRoutingService NewService()
	{
		var lockService = Substitute.For<ILockService>();
		lockService.Evaluate(Arg.Any<LockType>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(true);

		_permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
			Arg.Any<IPermissionService.InteractType>()).Returns(new ValueTask<bool>(true));

		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns(new ValueTask<OptionalSharpAttributeOrError>(new OneOf.Types.None()));

		var serviceProvider = Substitute.For<IServiceProvider>();
		serviceProvider.GetService(typeof(IAttributeService)).Returns(_attributes);

		return new ListenerRoutingService(
			_mediator,
			_patterns,
			_permissions,
			lockService,
			Substitute.For<IConnectionService>(),
			serviceProvider,
			Substitute.For<IMessageBus>());
	}

	/// <summary>Resolves any object this test made, so the service can look its target up.</summary>
	private void ResolveObjectsThroughMediator(params AnySharpObject[] objects)
	{
		_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == Room.Object.DBRef.Number),
				Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(Room));

		// Everything passed in is in the room. Stubbed so that a pass which still walks the contents
		// really does reach the bystanders, rather than passing these tests on an empty stream.
		_mediator.CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>())
			.Returns(objects.Select(o => o.AsContent).ToAsyncEnumerable());

		foreach (var obj in objects)
		{
			var known = obj;
			_mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef.Number == known.Object().DBRef.Number),
					Arg.Any<CancellationToken>())
				.Returns(new ValueTask<AnyOptionalSharpObject>(known.WithNoneOption()));
		}
	}

	private ValueTask Route(ListenerRoutingService service, AnySharpObject target,
		AnySharpObject speaker, DBRef[]? excluded = null)
		=> service.ProcessNotificationAsync(
			new NotificationContext(target.Object().DBRef, Room.Object.DBRef, excluded ?? []),
			"hello everyone",
			speaker,
			INotifyService.NotificationType.Say);

	[Test]
	public async Task OnlyTheObjectAddressedHasItsListenAttributeRead()
	{
		var speaker = Occupant(1);
		var target = Occupant(2);
		var bystander = Occupant(3);
		ResolveObjectsThroughMediator(speaker, target, bystander);
		var service = NewService();

		await Route(service, target, speaker);

		await Assert.That(ListenReadsFor(target)).IsEqualTo(1);
		await Assert.That(ListenReadsFor(bystander)).IsEqualTo(0)
			.Because("a bystander hears the broadcast through its own notification, not through this one");
	}

	/// <summary>
	/// How many objects one pass considers at all. Three occupants, one notification: the pass used to
	/// weigh all three, and a broadcast makes one notification per occupant, so the room paid N² gates
	/// — each with a lock evaluation behind it — for a single line of speech.
	/// </summary>
	[Test]
	public async Task ExactlyOneObjectIsConsidered()
	{
		var speaker = Occupant(1);
		var target = Occupant(2);
		ResolveObjectsThroughMediator(speaker, target, Occupant(3));
		var service = NewService();

		await Route(service, target, speaker);

		await Assert.That(InteractionChecks()).IsEqualTo(1);
	}

	/// <summary>
	/// The pass no longer asks for the room's contents at all. This is the quadratic term: one
	/// contents enumeration per notification, and one notification per occupant.
	/// </summary>
	[Test]
	public async Task TheRoomsContentsAreNotEnumerated()
	{
		var speaker = Occupant(1);
		var target = Occupant(2);
		ResolveObjectsThroughMediator(speaker, target);
		var service = NewService();

		await Route(service, target, speaker);

		await Assert.That(_mediator.ReceivedCalls()
				.SelectMany(c => c.GetArguments())
				.OfType<GetContentsQuery>()
				.Count())
			.IsEqualTo(0);
	}

	[Test]
	public async Task ATargetTheSpeakerCannotBeHeardByIsNotEvaluated()
	{
		var speaker = Occupant(1);
		var target = Occupant(2);
		ResolveObjectsThroughMediator(speaker, target);
		var service = NewService();
		_permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
			Arg.Any<IPermissionService.InteractType>()).Returns(new ValueTask<bool>(false));

		await Route(service, target, speaker);

		await Assert.That(ListenReadsFor(target)).IsEqualTo(0);
	}

	[Test]
	public async Task AnExcludedTargetIsNotEvaluated()
	{
		var speaker = Occupant(1);
		var target = Occupant(2);
		ResolveObjectsThroughMediator(speaker, target);
		var service = NewService();

		await Route(service, target, speaker, excluded: [target.Object().DBRef]);

		await Assert.That(ListenReadsFor(target)).IsEqualTo(0);
	}

	/// <summary>
	/// How many times the LISTEN attribute of <paramref name="listener"/> was read. Counted by dbref:
	/// the service re-wraps each object in a fresh union, so the argument is never the same instance.
	/// </summary>
	private int ListenReadsFor(AnySharpObject listener)
		=> _attributes.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == nameof(IAttributeService.GetAttributeAsync))
			.Count(c => c.GetArguments() is [_, AnySharpObject obj, "LISTEN", ..] && Is(obj, listener));

	private int InteractionChecks()
		=> _permissions.ReceivedCalls()
			.Count(c => c.GetMethodInfo().Name == nameof(IPermissionService.CanInteract));

	private static bool Is(AnySharpObject one, AnySharpObject other)
		=> one.Object().DBRef.Number == other.Object().DBRef.Number;
}
