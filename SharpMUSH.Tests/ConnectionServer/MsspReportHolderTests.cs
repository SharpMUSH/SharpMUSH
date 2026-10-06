using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>The connection server answers the MSSP telnet option with the report the main process sent.</summary>
public class MsspReportHolderTests
{
	[Test]
	public async Task TheTelnetOptionReportsWhatTheMainProcessSent()
	{
		var holder = new MsspReportHolder();
		await new MSSPReportConsumer(holder).HandleAsync(new MSSPReportMessage(
		[
			new MSSPVariable("NAME", ["Test Game"]),
			new MSSPVariable("CHARSET", ["ISO-8859-1", "UTF-8"])
		]));

		var variables = holder.Current.Variables;
		await Assert.That(variables["NAME"]).IsEquivalentTo(new[] { "Test Game" });
		await Assert.That(variables["CHARSET"]).IsEquivalentTo(new[] { "ISO-8859-1", "UTF-8" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task UntilAReportArrivesTheNameIsStillReported()
	{
		var holder = new MsspReportHolder();

		await Assert.That(holder.Current.Name).IsEqualTo("SharpMUSH");

		holder.Replace([]);
		await Assert.That(holder.Current.Name).IsEqualTo("SharpMUSH");
	}
}
