using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// With <c>use_dns</c> on, a connection that arrives with only its address is given the name the address resolves
/// to as its host, which is what <c>LASTSITE</c>, WHO and sitelock host rules read.
/// </summary>
public class HostNameLookupTests
{
	private const long Handle = 9101;

	private static (ConnectionEstablishedConsumer Consumer, IConnectionService Connections) Build(bool useDns, IHostNameResolver resolver,
		string address = "203.0.113.5")
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Handle).Returns(new IConnectionService.ConnectionData(Handle, null,
			IConnectionService.ConnectionState.Connected, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, new ConcurrentDictionary<string, string>(
				new Dictionary<string, string> { ["InternetProtocolAddress"] = address })));
		var defaults = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(defaults with { Net = defaults.Net with { UseDns = useDns } });
		return (new ConnectionEstablishedConsumer(NullLogger<ConnectionEstablishedConsumer>.Instance, connections,
			Substitute.For<IMessageBus>(), configuration: options, hostNames: resolver), connections);
	}

	private static IHostNameResolver Resolving(string name)
	{
		var resolver = Substitute.For<IHostNameResolver>();
		resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<string?>(name));
		return resolver;
	}

	[Test]
	public async Task AnAddressAsHost_BecomesTheNameItResolvesTo()
	{
		var (consumer, connections) = Build(useDns: true, Resolving("player.example"));

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, "203.0.113.5", "203.0.113.5", "telnet", DateTimeOffset.UnixEpoch));

		await Assert.That(() => connections.ReceivedCalls()
				.Any(call => call.GetMethodInfo().Name == nameof(IConnectionService.Update)
					&& call.GetArguments() is [Handle, "HostName", "player.example"]))
			.WaitsFor(updated => updated.IsTrue(), timeout: TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(20));
	}

	[Test]
	public async Task WithUseDnsOff_TheAddressStaysTheHost()
	{
		var resolver = Resolving("player.example");
		var (consumer, _) = Build(useDns: false, resolver);

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, "203.0.113.5", "203.0.113.5", "telnet", DateTimeOffset.UnixEpoch));

		await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task AHostTheSocketAlreadyNamed_IsNotLookedUp()
	{
		var resolver = Resolving("player.example");
		var (consumer, _) = Build(useDns: true, resolver);

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, "203.0.113.5", "named.example", "telnet", DateTimeOffset.UnixEpoch));

		await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
