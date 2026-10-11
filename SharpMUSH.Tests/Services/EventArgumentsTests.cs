using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

/// <summary>An event handler reads each argument by position and by the name <c>help event list</c> gives it.</summary>
public class EventArgumentsTests
{
	[Test]
	public async Task AnEventsArgumentsArriveByPositionAndByName()
	{
		var arguments = EventArguments.Of("OBJECT`MOVE", ["#5", "#10", "#2", "0", "teleport"]);

		await Assert.That(arguments["1"].Message.ToPlainText()).IsEqualTo("#10");
		await Assert.That(arguments["destination"].Message.ToPlainText()).IsEqualTo("#10");
		await Assert.That(arguments["ORIGIN"].Message.ToPlainText()).IsEqualTo("#2");
		await Assert.That(arguments["cause"].Message.ToPlainText()).IsEqualTo("teleport");
	}

	[Test]
	public async Task ANameWithSlashesNamesEachPartAndThePositionKeepsTheWhole()
	{
		var arguments = EventArguments.Of("SOCKET`DISCONNECT", ["3", "127.0.0.1", "quit", "120/4096/7"]);

		await Assert.That(arguments["3"].Message.ToPlainText()).IsEqualTo("120/4096/7");
		await Assert.That(arguments["received"].Message.ToPlainText()).IsEqualTo("120");
		await Assert.That(arguments["sent"].Message.ToPlainText()).IsEqualTo("4096");
		await Assert.That(arguments["commands"].Message.ToPlainText()).IsEqualTo("7");
	}

	[Test]
	public async Task ChannelAndPageMessagesShareTheirNames()
	{
		var channel = EventArguments.For(SharpEvents.ChannelMessage);
		var page = EventArguments.For(SharpEvents.PageMessage);

		await Assert.That(page.Intersect(channel).ToArray())
			.IsEquivalentTo(["speaker", "recipients", "style", "name", "message", "time", "id"]);
	}

	[Test]
	public async Task AnUnlistedEventGetsPositionsOnly()
	{
		var arguments = EventArguments.Of("TEST`EVENT", ["first"]);

		await Assert.That(arguments.Keys.ToArray()).IsEquivalentTo(["0"]);
	}
}
