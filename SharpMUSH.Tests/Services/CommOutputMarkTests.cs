using System.Collections.Concurrent;
using System.Text;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A notification whose context names a channel line or a page publishes its output marked so (what the portal
/// keeps out of its main output when asked to); one without stays unmarked.
/// </summary>
public class CommOutputMarkTests
{
	private static readonly DBRef Player = new(42, null);

	private static (NotifyService Service, IMessageBus Bus) BuildService()
	{
		var connections = Substitute.For<IConnectionService>();
		var portal = new IConnectionService.ConnectionData(6, Player, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		connections.Get(6L).Returns(portal);
		connections.Get(Player).Returns(_ => new[] { portal }.ToAsyncEnumerable());
		var bus = Substitute.For<IMessageBus>();
		return (new NotifyService(bus, connections, Substitute.For<ILocalizationService>(), DisabledRealityPolicy.Instance,
			null, null, null), bus);
	}

	private static CommOutput[] Marks(IMessageBus bus) =>
		[.. bus.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<MarkupOutputMessage>().Select(m => m.Comm)];

	[Test]
	[Arguments(CommOutput.Page)]
	[Arguments(CommOutput.Channel)]
	[Arguments(CommOutput.None)]
	public async Task TheContextsMarkReachesTheConnection(CommOutput comm)
	{
		var (service, bus) = BuildService();

		await service.NotifyContextAsync(new NotificationContext(Player, null, []) { Comm = comm },
			MString.Plain("Wren pages: psst"), null, INotifyService.NotificationType.Say);

		await Assert.That(Marks(bus)).IsEquivalentTo([comm]);
	}

	[Test]
	public async Task APlainNotificationIsUnmarked()
	{
		var (service, bus) = BuildService();

		await service.Notify(Player, "You see a quay.", sender: null);

		await Assert.That(Marks(bus)).IsEquivalentTo([CommOutput.None]);
	}
}
