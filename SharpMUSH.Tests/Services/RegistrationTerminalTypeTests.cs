using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Consumers;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The portal's terminal names itself on its first frame, so the connection is registered with its terminal types:
/// a guest or token login sent the moment the socket opens runs its connect hooks with <c>terminfo()</c> already
/// naming the client (#2896), instead of racing the separate <c>ttype</c> frame.
/// </summary>
public class RegistrationTerminalTypeTests
{
	private const long Handle = 1000096;

	private static async Task<ConcurrentDictionary<string, string>> RegisterAsync(IReadOnlyList<string>? types)
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		var consumer = new ConnectionEstablishedConsumer(NullLogger<ConnectionEstablishedConsumer>.Instance, service,
			Substitute.For<SharpMUSH.Messaging.Abstractions.IMessageBus>());

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, "1.2.3.4", "host", "websocket",
			DateTimeOffset.UnixEpoch)
		{ TerminalTypes = types });

		return service.Get(Handle)!.Metadata;
	}

	[Test]
	public async Task Registration_RecordsTheTerminalTypesOfTheFirstFrame()
	{
		var metadata = await RegisterAsync(["SHARPMUSH-PORTAL", "ANSI", "256 COLORS", "TRUECOLOR", "UTF8"]);

		await Assert.That(metadata["TerminalType"]).IsEqualTo("SHARPMUSH-PORTAL");
		await Assert.That(metadata["TerminalTypes"]).IsEqualTo("SHARPMUSH-PORTAL\tANSI\t256 COLORS\tTRUECOLOR\tUTF8");
	}

	[Test]
	public async Task Registration_WithoutTerminalTypes_RecordsNone()
	{
		var metadata = await RegisterAsync(null);

		await Assert.That(metadata.ContainsKey("TerminalType")).IsFalse();
		await Assert.That(metadata.ContainsKey("TerminalTypes")).IsFalse();
	}
}
