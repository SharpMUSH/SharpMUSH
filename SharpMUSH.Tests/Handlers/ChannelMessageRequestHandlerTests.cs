using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Handlers;

public class ChannelMessageRequestHandlerTests
{
	[Test]
	public async Task SourcelessMessageReachesMembersWithNoSender()
	{
		var notifyService = Substitute.For<INotifyService>();
		var handler = new ChannelMessageRequestHandler(
			Substitute.For<IPermissionService>(),
			notifyService,
			Substitute.For<IMediator>(),
			Substitute.For<IAttributeService>(),
			NullLogger<ChannelMessageRequestHandler>.Instance);

		var member = new AnySharpObject(Thing(300, "Listener"));
		var channel = new SharpChannel
		{
			Name = MarkupText.Plain("Public"),
			Owner = new(async _ => { await Task.CompletedTask; return null!; }),
			Members = new(() => new[] { new SharpChannel.MemberAndStatus(member, new SharpChannelStatus(null, null, null, null, null)) }
				.ToAsyncEnumerable()),
			Privs = []
		};

		await handler.Handle(new ChannelMessageNotification(
			channel,
			new None(),
			INotifyService.NotificationType.Emit,
			MarkupText.Plain("The server is restarting."),
			MarkupText.Empty,
			MarkupText.Plain("System"),
			MarkupText.Empty,
			[]), CancellationToken.None);

		await notifyService.Received(1).Notify(member, Arg.Any<SharpMessage>(), null, INotifyService.NotificationType.Emit);
	}

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
