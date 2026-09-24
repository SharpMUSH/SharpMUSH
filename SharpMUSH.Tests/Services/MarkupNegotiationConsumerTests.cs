using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
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

	private static Task Pueblo(ConnectionService service, INotifyService? notifyService = null) =>
		new PuebloNegotiatedConsumer(NullLogger<PuebloNegotiatedConsumer>.Instance, service,
				notifyService ?? Substitute.For<INotifyService>())
			.HandleAsync(new PuebloNegotiatedMessage(Handle, "PUEBLOCLIENT 2.50"));

	/// <summary>
	/// The handshake's start sequence ends with <c>&lt;xch_page clear=text&gt;</c>, which wipes the
	/// greeting the player was just shown. PennMUSH redraws the connect screen at the same point
	/// (<c>welcome_user</c>, <c>src/bsd.c</c>).
	/// </summary>
	[Test]
	public async Task Pueblo_GreetsAgainAfterTheHandshakeClearedTheScreen()
	{
		var service = await RegisteredAsync();
		var notify = Substitute.For<INotifyService>();

		await Pueblo(service, notify);

		await notify.Received(1).NotifyLocalized(Handle, nameof(ErrorMessages.Notifications.Connected));
	}

	[Test]
	public async Task Pueblo_LeavesALoggedInConnectionAlone()
	{
		var service = await RegisteredAsync();
		await service.Bind(Handle, new DBRef(1));
		var notify = Substitute.For<INotifyService>();

		await Pueblo(service, notify);

		await notify.DidNotReceive().NotifyLocalized(Handle, Arg.Any<string>());
	}

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

	/// <summary>
	/// The two consumers run concurrently, so the Pueblo one decides from the value it writes rather than
	/// from one it read a moment earlier: a read, then a write, can put "pueblo" over an "mxp" that
	/// landed in between.
	/// </summary>
	[Test]
	public async Task TheFormatIsDecidedInTheWriteItself()
	{
		var service = await RegisteredAsync();
		var seen = new List<string?>();

		service.Update(Handle, "OUTPUT_FORMAT", current =>
		{
			seen.Add(current);
			return "mxp";
		});
		service.Update(Handle, "OUTPUT_FORMAT", current =>
		{
			seen.Add(current);
			return current == "mxp" ? "mxp" : "pueblo";
		});

		await Assert.That(seen).IsEquivalentTo(new string?[] { null, "mxp" })
			.Because("the change sees the value in place at the moment it writes");
		await Assert.That(service.Get(Handle)!.Metadata["OUTPUT_FORMAT"]).IsEqualTo("mxp");
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
