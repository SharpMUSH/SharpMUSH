using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Pueblo and MXP are different dialects: Pueblo writes a command link as &lt;A XCH_CMD&gt; and MXP as
/// &lt;SEND HREF&gt;, and neither client understands the other's. So negotiating one must never report
/// the other — softcode that asks <c>pueblo()</c> and gets "1" for an MXP client writes it a tag it
/// cannot read.
/// </summary>
public class MarkupNegotiationConsumerTests
{
	private const long Handle = 1000077;

	private static async Task<ConnectionService> RegisteredAsync()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await service.Register(Handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>());
		return service;
	}

	private static Task Mxp(ConnectionService service) =>
		new MxpNegotiatedConsumer(NullLogger<MxpNegotiatedConsumer>.Instance, service)
			.HandleAsync(new MxpNegotiatedMessage(Handle));

	private static Task Pueblo(ConnectionService service) =>
		new PuebloNegotiatedConsumer(NullLogger<PuebloNegotiatedConsumer>.Instance, service)
			.HandleAsync(new PuebloNegotiatedMessage(Handle, "PUEBLOCLIENT 2.50"));

	[Test]
	public async Task Mxp_DoesNotClaimPueblo()
	{
		var service = await RegisteredAsync();

		await Mxp(service);

		var metadata = service.Get(Handle)!.Metadata;
		await Assert.That(metadata["OUTPUT_FORMAT"]).IsEqualTo("mxp");
		await Assert.That(metadata.GetValueOrDefault("PUEBLO", "0")).IsEqualTo("0")
			.Because("an MXP client cannot read <A XCH_CMD>, so pueblo() must not report it as a Pueblo client");
	}

	[Test]
	public async Task Pueblo_AfterMxp_KeepsMxpAndReportsPueblo()
	{
		var service = await RegisteredAsync();

		await Mxp(service);
		await Pueblo(service);

		var metadata = service.Get(Handle)!.Metadata;
		await Assert.That(metadata["OUTPUT_FORMAT"]).IsEqualTo("mxp")
			.Because("the socket server keeps rendering MXP for a client that answers both");
		await Assert.That(metadata["PUEBLO"]).IsEqualTo("1")
			.Because("the client did send the Pueblo handshake");
	}

	[Test]
	[Arguments(OutputFormat.Ansi, OutputFormat.Pueblo, OutputFormat.Pueblo)]
	[Arguments(OutputFormat.Ansi, OutputFormat.Mxp, OutputFormat.Mxp)]
	[Arguments(OutputFormat.Pueblo, OutputFormat.Mxp, OutputFormat.Mxp)]
	[Arguments(OutputFormat.Mxp, OutputFormat.Pueblo, OutputFormat.Mxp)]
	public async Task SocketServerFormat_AgreesWithTheEngine(OutputFormat current, OutputFormat offered, OutputFormat expected)
	{
		await Assert.That(current.Negotiate(offered)).IsEqualTo(expected);
	}
}
