using SharpMUSH.Library.Authorization;
using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Commands;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ChannelBroadcastServiceTests
{
	[Test]
	public async Task SourcelessMessageReachesMembersWithNoSender()
	{
		var notifyService = Substitute.For<INotifyService>();
		var service = Service(notifyService, Substitute.For<IEventService>());
		var member = new AnySharpObject(Thing(300, "Listener"));

		await service.BroadcastAsync(Line(Channel(member, gagged: false)), CancellationToken.None);

		await notifyService.Received(1).Notify(member, Arg.Any<SharpMessage>(), null, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// CHANNEL`MESSAGE names the members the line was delivered to, as parts: the channel, the speaker
	/// (empty for a sourceless line), the style word, the name, the message, the time and the line's id.
	/// </summary>
	[Test]
	public async Task DeliveredLine_RaisesChannelMessageNamingItsRecipients()
	{
		var eventService = Substitute.For<IEventService>();
		var service = Service(Substitute.For<INotifyService>(), eventService);
		var member = new AnySharpObject(Thing(310, "Listener"));
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await service.BroadcastAsync(Line(Channel(member, gagged: false), INotifyService.NotificationType.Say),
			CancellationToken.None);

		await eventService.Received(1).TriggerEventAsync(SharpEvents.ChannelMessage,
			null,
			Arg.Is<string[]>(args =>
				args.Length == 8
				&& args[0] == "Public"
				&& args[1] == string.Empty
				&& args[2] == "say"
				&& args[3] == "System"
				&& args[4] == "The server is restarting."
				&& args[5] == member.Object().DBRef.ToString()
				&& long.Parse(args[6]) >= before
				&& args[7] == Ids.Peek.ToString(System.Globalization.CultureInfo.InvariantCulture)));
	}

	/// <summary>
	/// The id the event passes on (the <c>comm.message</c> payload's <c>id</c>) is the id the line is
	/// buffered under, which is what the portal's recall endpoint returns: a pulled and a pushed copy of
	/// one line are known to be one. The buffered line keeps its parts for the same endpoint.
	/// </summary>
	[Test]
	public async Task BufferedLine_CarriesTheIdTheEventPassedOn_AndItsParts()
	{
		var eventService = Substitute.For<IEventService>();
		var mediator = Substitute.For<IMediator>();
		var permissions = Substitute.For<IPermissionService>();
		permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), IPermissionService.InteractType.Hear)
			.Returns(true);
		var service = Service(Substitute.For<INotifyService>(), eventService, mediator, permissions);
		var member = new AnySharpObject(Thing(340, "Speaker"));
		var line = Line(Channel(member, gagged: false), INotifyService.NotificationType.Pose) with { Source = member };

		await service.BroadcastAsync(line, CancellationToken.None);

		var raised = eventService.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IEventService.TriggerEventAsync))
			.GetArguments()[2] as string[];
		var buffered = mediator.ReceivedCalls()
			.Select(call => call.GetArguments()[0])
			.OfType<AddChannelMessageCommand>()
			.Single().Message;

		await Assert.That(buffered.Id).IsGreaterThan(0);
		await Assert.That(raised![7]).IsEqualTo(buffered.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
		await Assert.That(buffered.Style).IsEqualTo("pose");
		await Assert.That(buffered.SpeakerName).IsEqualTo("System");
		await Assert.That(buffered.MessageText).IsEqualTo("The server is restarting.");
	}

	/// <summary>A line nobody was sent — here, its one member gagged the channel — raises nothing.</summary>
	[Test]
	public async Task UndeliveredLine_RaisesNoEvent()
	{
		var eventService = Substitute.For<IEventService>();
		var service = Service(Substitute.For<INotifyService>(), eventService);
		var member = new AnySharpObject(Thing(320, "Gagged"));

		await service.BroadcastAsync(Line(Channel(member, gagged: true)), CancellationToken.None);

		await eventService.DidNotReceive().TriggerEventAsync(Arg.Any<string>(), Arg.Any<DBRef?>(), Arg.Any<string[]>());
	}

	/// <summary>
	/// CHANNEL`MESSAGE follows delivery, not the recall buffer: once the members have their terminal line,
	/// a buffer write that fails must not keep the portal's comm feed from getting the same line.
	/// </summary>
	[Test]
	public async Task BufferFailure_AfterDelivery_StillRaisesChannelMessage()
	{
		var eventService = Substitute.For<IEventService>();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<AddChannelMessageCommand>(), Arg.Any<CancellationToken>())
			.Returns<ValueTask<Unit>>(_ => throw new InvalidOperationException("recall store unavailable"));
		// Only a line with a speaker is buffered, and a speaker's line reaches a member who will hear them.
		var permissions = Substitute.For<IPermissionService>();
		permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), IPermissionService.InteractType.Hear)
			.Returns(true);
		var service = Service(Substitute.For<INotifyService>(), eventService, mediator, permissions);
		var member = new AnySharpObject(Thing(330, "Speaker"));
		var line = Line(Channel(member, gagged: false), INotifyService.NotificationType.Say) with { Source = member };

		await Assert.That(async () => await service.BroadcastAsync(line, CancellationToken.None))
			.Throws<InvalidOperationException>();

		await eventService.Received(1).TriggerEventAsync(SharpEvents.ChannelMessage,
			Arg.Any<DBRef?>(),
			Arg.Is<string[]>(args => args[5] == member.Object().DBRef.ToString()));
	}

	/// <summary>Hands out a fixed id, so a test can name the one the next line will carry.</summary>
	private sealed class FixedIds : IChannelMessageIdSource
	{
		public long Peek { get; } = 1_790_780_182_950_000;
		public long Latest => Peek;
		public ValueTask<long> NextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Peek);
	}

	private static readonly FixedIds Ids = new();

	private static ChannelBroadcastService Service(INotifyService notifyService, IEventService eventService,
		IMediator? mediator = null, IPermissionService? permissions = null) =>
		new(
			permissions ?? Substitute.For<IPermissionService>(),
			notifyService,
			mediator ?? Substitute.For<IMediator>(),
			Substitute.For<IAttributeService>(),
			Substitute.For<IMUSHCodeParser>(),
			eventService,
			Ids,
			NullLogger<ChannelBroadcastService>.Instance);

	private static SharpChannel Channel(AnySharpObject member, bool gagged) => new()
	{
		Name = MarkupText.Plain("Public"),
		OwnerDBRef = new DBRef(-1),
		Owner = new(async _ => { await Task.CompletedTask; return null!; }),
		Members = new(() => new[] { new SharpChannel.MemberAndStatus(member, new SharpChannelStatus(null, gagged, null, null, null)) }
			.ToAsyncEnumerable()),
		Privs = []
	};

	private static ChannelMessageNotification Line(SharpChannel channel,
		INotifyService.NotificationType type = INotifyService.NotificationType.Emit) => new(
		channel,
		new None(),
		type,
		MarkupText.Plain("The server is restarting."),
		MarkupText.Empty,
		MarkupText.Plain("System"),
		MarkupText.Empty,
		[]);

	private static SharpThing Thing(int key, string name)
	{
		var room = new SharpRoom
		{
			Aliases = [],
			Object = Object(key + 1, "Room", "Room"),
			Location = new(async _ => { await Task.CompletedTask; return new None(); })
		};

		return new SharpThing
		{
			Aliases = [],
			Object = Object(key, name, "Thing"),
			Location = new(async _ => { await Task.CompletedTask; return room; }),
			Home = new(async _ => { await Task.CompletedTask; return room; })
		};
	}

	private static SharpObject Object(int key, string name, string type) => new()
	{
		Key = key,
		Name = name,
		Type = type,
		Locks = ImmutableDictionary<string, SharpLockData>.Empty,
		Owner = new(async _ => { await Task.CompletedTask; return null!; }),
		Grants = new(_ => Task.FromResult(ObjectGrants.None)),
		Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
		Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
		LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
		AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
		LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
		Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
		Parent = new(async _ => { await Task.CompletedTask; return new None(); }),
		Zone = new(async _ => { await Task.CompletedTask; return new None(); }),
		Children = new(() => AsyncEnumerable.Empty<SharpObject>())
	};
}
