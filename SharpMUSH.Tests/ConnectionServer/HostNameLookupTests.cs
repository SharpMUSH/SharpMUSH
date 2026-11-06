using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// With <c>use_dns</c> on, a connection that arrives with only its address is given the name the address resolves
/// to as its host, which is what <c>LASTSITE</c>, WHO and sitelock host rules read.
/// </summary>
public class HostNameLookupTests
{
	private const long Handle = 9101;
	private const string Address = "203.0.113.5";

	private static (HostNameLookup Lookup, IConnectionService Connections) Build(bool useDns, IHostNameResolver resolver,
		string host = Address)
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Handle).Returns(new IConnectionService.ConnectionData(Handle, null,
			IConnectionService.ConnectionState.Connected, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, new ConcurrentDictionary<string, string>(
				new Dictionary<string, string> { ["InternetProtocolAddress"] = Address, ["HostName"] = host })));
		var defaults = ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst");
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(defaults with { Net = defaults.Net with { UseDns = useDns } });
		return (new HostNameLookup(connections, options, resolver, NullLogger<HostNameLookup>.Instance), connections);
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
		var (lookup, connections) = Build(useDns: true, Resolving("player.example"));

		await lookup.LookUpAsync(Handle);

		connections.Received(1).Update(Handle, "HostName", "player.example");
		connections.Received(1).Update(Handle, HostNameLookup.LookedUpMetadata, "1");
	}

	[Test]
	public async Task ALoginWaitsForTheLookupAlreadyRunning()
	{
		var answer = new TaskCompletionSource<string?>();
		var resolver = Substitute.For<IHostNameResolver>();
		resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(answer.Task));
		var (lookup, connections) = Build(useDns: true, resolver);

		var atConnect = lookup.LookUpAsync(Handle);
		var atLogin = lookup.LookUpAsync(Handle);
		await Assert.That(atLogin.IsCompleted).IsFalse();
		answer.SetResult("player.example");
		await Task.WhenAll(atConnect, atLogin);

		await resolver.Received(1).ResolveAsync(Address, Arg.Any<CancellationToken>());
		connections.Received(1).Update(Handle, "HostName", "player.example");
	}

	[Test]
	public async Task WithUseDnsOff_TheAddressStaysTheHost()
	{
		var resolver = Resolving("player.example");
		var (lookup, connections) = Build(useDns: false, resolver);

		await lookup.LookUpAsync(Handle);

		await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		connections.DidNotReceive().Update(Handle, "HostName", Arg.Any<string>());
	}

	[Test]
	public async Task AHostTheSocketAlreadyNamed_IsNotLookedUp()
	{
		var resolver = Resolving("player.example");
		var (lookup, _) = Build(useDns: true, resolver, host: "named.example");

		await lookup.LookUpAsync(Handle);

		await resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
