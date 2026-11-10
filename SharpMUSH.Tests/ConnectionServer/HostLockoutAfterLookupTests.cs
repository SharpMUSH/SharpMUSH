using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A login made before the connection's host name was known is dropped when that name turns out to be one a
/// <c>!connect</c> sitelock rule names.
/// </summary>
public class HostLockoutAfterLookupTests
{
	private const long Handle = 9201;
	private const string Address = "203.0.113.9";

	private static IConnectionService Connections(DBRef? player, string host)
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Handle).Returns(new IConnectionService.ConnectionData(Handle, player,
			IConnectionService.ConnectionState.LoggedIn, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, new ConcurrentDictionary<string, string>(
				new Dictionary<string, string> { ["InternetProtocolAddress"] = Address, ["HostName"] = host })));
		return connections;
	}

	private static IOptionsWrapper<SharpMUSHOptions> Locking(string host)
	{
		var defaults = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(defaults with
		{
			SitelockRules = new SitelockRulesOptions(new Dictionary<string, string[]> { [host] = ["!connect"] })
		});
		return options;
	}

	private static async Task<DisconnectConnectionMessage> EstablishAsync(IConnectionService connections)
	{
		var dropped = new TaskCompletionSource<DisconnectConnectionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var bus = Substitute.For<IMessageBus>();
		bus.Publish(Arg.Any<DisconnectConnectionMessage>(), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask)
			.AndDoes(call => dropped.TrySetResult(call.Arg<DisconnectConnectionMessage>()));
		var consumer = new ConnectionEstablishedConsumer(NullLogger<ConnectionEstablishedConsumer>.Instance, connections, bus,
			hostNames: Substitute.For<IHostNameLookup>(), options: Locking("locked.example"));

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, Address, Address, "telnet", DateTimeOffset.UtcNow));
		return await dropped.Task.WaitAsync(TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task ALoginFromAHostAConnectRuleNames_IsDroppedOnceTheNameIsKnown()
	{
		var dropped = await EstablishAsync(Connections(new DBRef(42), "locked.example"));

		await Assert.That(dropped.Handle).IsEqualTo(Handle);
	}
}
