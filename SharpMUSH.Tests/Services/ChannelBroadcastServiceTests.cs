using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Services;
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

		await service.BroadcastAsync(Emit(Channel(member, gagged: false)), CancellationToken.None);

		await notifyService.Received(1).Notify(member, Arg.Any<SharpMessage>(), null, INotifyService.NotificationType.Emit);
	}

	/// <summary>
	/// CHANNEL`MESSAGE names the members the line was delivered to, as parts: the channel, the speaker
	/// (empty for a sourceless line), the style word, the name, the message and the time.
	/// </summary>
	[Test]
	public async Task DeliveredLine_RaisesChannelMessageNamingItsRecipients()
	{
		var eventService = Substitute.For<IEventService>();
		var service = Service(Substitute.For<INotifyService>(), eventService);
		var member = new AnySharpObject(Thing(310, "Listener"));
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await service.BroadcastAsync(Emit(Channel(member, gagged: false)), CancellationToken.None);

		await eventService.Received(1).TriggerEventAsync(
			Arg.Any<IMUSHCodeParser>(),
			SharpEvents.ChannelMessage,
			null,
			Arg.Is<string[]>(args =>
				args.Length == 7
				&& args[0] == "Public"
				&& args[1] == string.Empty
				&& args[2] == "emit"
				&& args[3] == "System"
				&& args[4] == "The server is restarting."
				&& args[5] == member.Object().DBRef.ToString()
				&& long.Parse(args[6]) >= before));
	}

	/// <summary>A line nobody was sent — here, its one member gagged the channel — raises nothing.</summary>
	[Test]
	public async Task UndeliveredLine_RaisesNoEvent()
	{
		var eventService = Substitute.For<IEventService>();
		var service = Service(Substitute.For<INotifyService>(), eventService);
		var member = new AnySharpObject(Thing(320, "Gagged"));

		await service.BroadcastAsync(Emit(Channel(member, gagged: true)), CancellationToken.None);

		await eventService.DidNotReceive().TriggerEventAsync(
			Arg.Any<IMUSHCodeParser>(), Arg.Any<string>(), Arg.Any<DBRef?>(), Arg.Any<string[]>());
	}

	private static ChannelBroadcastService Service(INotifyService notifyService, IEventService eventService) =>
		new(
			Substitute.For<IPermissionService>(),
			notifyService,
			Substitute.For<IMediator>(),
			Substitute.For<IAttributeService>(),
			Substitute.For<IMUSHCodeParser>(),
			eventService,
			NullLogger<ChannelBroadcastService>.Instance);

	private static SharpChannel Channel(AnySharpObject member, bool gagged) => new()
	{
		Name = MarkupText.Plain("Public"),
		Owner = new(async _ => { await Task.CompletedTask; return null!; }),
		Members = new(() => new[] { new SharpChannel.MemberAndStatus(member, new SharpChannelStatus(null, gagged, null, null, null)) }
			.ToAsyncEnumerable()),
		Privs = []
	};

	private static ChannelMessageNotification Emit(SharpChannel channel) => new(
		channel,
		new None(),
		INotifyService.NotificationType.Emit,
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
