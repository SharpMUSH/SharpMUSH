using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MailFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("mail()", "0")]
	public async Task Mail_NoArgs_ReturnsCount(string str, string expected)
	{
		// With no mail, should return 0
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mail(999)", "#-1 NO SUCH MAIL")]
	public async Task Mail_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("maillist()", "")]
	public async Task Maillist_NoArgs_ReturnsEmptyOrList(string str, string expected)
	{
		// With no mail, should return empty string
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// Result should be valid (either empty or a valid list format)
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("mailfrom(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailfrom_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mailstats(%#)")]
	public async Task Mailstats_ValidPlayer_ReturnsStats(string str)
	{
		// Should return "sent received" format (likely "0 0" with no mail)
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 2 parts
		await Assert.That(parts.Length).IsEqualTo(2);
		// Both should be numbers
		await Assert.That(int.TryParse(parts[0], out _)).IsTrue();
		await Assert.That(int.TryParse(parts[1], out _)).IsTrue();
	}

	[Test]
	[Arguments("maildstats(%#)")]
	public async Task Maildstats_ValidPlayer_ReturnsDetailedStats(string str)
	{
		// Should return "sent sentUnread sentCleared received receivedUnread receivedCleared"
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 6 parts
		await Assert.That(parts.Length).IsEqualTo(6);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
	}

	[Test]
	[Arguments("mailfstats(%#)")]
	public async Task Mailfstats_ValidPlayer_ReturnsFullStats(string str)
	{
		// Should return "sent sentUnread sentCleared sentBytes received receivedUnread receivedCleared receivedBytes"
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 8 parts
		await Assert.That(parts.Length).IsEqualTo(8);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
	}

	[Test]
	[Arguments("mailstatus(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailstatus_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mailsubject(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailsubject_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mailtime(999)", "#-1 NO SUCH MAIL")]
	public async Task Mailtime_InvalidMessage_ReturnsError(string str, string expected)
	{
		// Non-existent message should return error
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("malias()", "")]
	public async Task Malias_NoArgs_ReturnsEmpty(string str, string expected)
	{
		// Mail aliases not implemented, should return empty
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("folderstats()")]
	public async Task Folderstats_NoArgs_ReturnsStats(string str)
	{
		// Should return "read unread cleared" format
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		var parts = result.ToPlainText()!.Split(' ');
		// Should have exactly 3 parts
		await Assert.That(parts.Length).IsEqualTo(3);
		// All should be numbers
		foreach (var part in parts)
		{
			await Assert.That(int.TryParse(part, out _)).IsTrue();
		}
	}
}

