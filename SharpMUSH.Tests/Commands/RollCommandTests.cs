using System.Text.Json;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Recorded rolls: <c>@roll</c> tells the room and keeps the roll, roll()'s <c>record</c> option keeps
/// one from softcode, and rollinfo() reads either back. One-sided dice keep every total known.
/// </summary>
public partial class RollCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[GeneratedRegex(@"\(roll (\d+)\)$")]
	private static partial Regex RollId();

	private async Task<string> Evaluate(string softcode)
		=> (await FunctionParser.EvaluateAsync(MarkupText.Plain(softcode))).ToPlainText();

	private async Task<(TestIsolationHelpers.TestPlayer Roller, TestIsolationHelpers.TestPlayer Watcher)> TwoInARoomAsync(string prefix)
	{
		var dig = await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		var roller = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}R", room);
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}W", room);
		return (roller, watcher);
	}

	private string HeardRoll(DBRef who, string roller)
		=> WebAppFactoryArg.Notifications.For(who).Single(m => m.StartsWith($"{roller} rolls ", StringComparison.Ordinal));

	[Test]
	public async Task AtRollTellsTheRoomAndKeepsTheRoll()
	{
		var (roller, watcher) = await TwoInARoomAsync("RollRoom");

		var result = await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("@roll 2d1+1"));

		var heard = HeardRoll(watcher.DbRef, roller.Name);
		var id = RollId().Match(heard).Groups[1].Value;
		await Assert.That(heard).IsEqualTo($"{roller.Name} rolls 2d1[1, 1] + 1 = 3 (roll {id})");
		await Assert.That(HeardRoll(roller.DbRef, roller.Name)).IsEqualTo(heard);
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(id);

		using var info = JsonDocument.Parse(await Evaluate($"rollinfo({id})"));
		var root = info.RootElement;
		await Assert.That(root.GetProperty("id").GetInt64().ToString()).IsEqualTo(id);
		await Assert.That(root.GetProperty("name").GetString()).IsEqualTo(roller.Name);
		await Assert.That(root.GetProperty("roller").GetString()).StartsWith($"#{roller.DbRef.Number}:");
		await Assert.That(root.GetProperty("formula").GetString()).IsEqualTo("2d1+1");
		await Assert.That(root.GetProperty("total").GetDecimal()).IsEqualTo(3m);
		await Assert.That(root.TryGetProperty("by", out _)).IsFalse();
	}

	[Test]
	public async Task AtRollEvaluatesTheFormula()
	{
		var (roller, watcher) = await TwoInARoomAsync("RollEval");

		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("@roll 1d1+[add(1,1)]"));

		await Assert.That(HeardRoll(watcher.DbRef, roller.Name)).StartsWith($"{roller.Name} rolls 1d1[1] + 2 = 3 (roll ");
	}

	[Test]
	public async Task AtRollNoevalTakesTheFormulaAsTyped()
	{
		var (roller, watcher) = await TwoInARoomAsync("RollTyped");

		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("@roll/noeval {1d1,2}kh + 1d1[fire]"));

		await Assert.That(HeardRoll(watcher.DbRef, roller.Name))
			.StartsWith($"{roller.Name} rolls {{1d1[1], 2}}kh[(1), 2] + 1d1[1] fire = 3 (roll ");
	}

	[Test]
	public async Task AtRollReadsDataAfterTheEquals()
	{
		var (roller, watcher) = await TwoInARoomAsync("RollData");
		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("&STAT`DEX me=2"));

		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("@roll 1d1+@stat.dex=me"));

		await Assert.That(HeardRoll(watcher.DbRef, roller.Name))
			.StartsWith($"{roller.Name} rolls 1d1[1] + stat.dex(2) = 3 (roll ");
	}

	[Test]
	public async Task ARecordedRollAnswersJsonThatRollInfoGivesBack()
	{
		var recorded = await Evaluate("""roll(1d1+1,,lit({"record":true}))""");

		using var json = JsonDocument.Parse(recorded);
		var id = json.RootElement.GetProperty("id").GetInt64();
		await Assert.That(json.RootElement.GetProperty("total").GetDecimal()).IsEqualTo(2m);
		await Assert.That(await Evaluate($"rollinfo({id})")).IsEqualTo(recorded);
		await Assert.That(await Evaluate($"rollinfo(#{id})")).IsEqualTo(recorded);
	}

	[Test]
	public async Task HelpExamplesWork()
	{
		var (roller, watcher) = await TwoInARoomAsync("RollHelp");
		var global = TestIsolationHelpers.GenerateUniqueName("RollCmd");
		var created = await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain($"@create {global}"));
		var command = DBRef.Parse(created.Message.ToPlainText().Trim());
		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain($"@set #{command.Number}=!no_command"));
		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain(
			$$"""&cmd`roll #{{command.Number}}=$+roll *:@remit %l=[setq(r,roll(%0,%#,lit({"record":true})))][name(%#)] rolls [json_query(%qr,extract,$.text)] (roll [json_query(%qr,extract,$.id)])"""));

		await CommandParser.CommandParse(roller.Handle, ConnectionService, MarkupText.Plain("+roll 2d1+1"));

		var heard = HeardRoll(watcher.DbRef, roller.Name);
		var id = RollId().Match(heard).Groups[1].Value;
		await Assert.That(heard).IsEqualTo($"{roller.Name} rolls 2d1[1, 1] + 1 = 3 (roll {id})");
		await Assert.That(await Evaluate($"json_query(rollinfo({id}),extract,$.text)")).IsEqualTo("2d1[1, 1] + 1 = 3");
		await Assert.That(await Evaluate($"json_query(rollinfo({id}),extract,$.name)")).IsEqualTo(roller.Name);
		await Assert.That(await Evaluate($"json_query(rollinfo({id}),extract,$.by)")).StartsWith($"#{command.Number}:");
	}

	[Test]
	[Arguments("""roll(1d6,,lit({"record":true,"mode":"max"}))""", ErrorMessages.Returns.RollRecordOnlyRolls)]
	[Arguments("""roll(1d6,,lit({"record":true,"output":"total"}))""", ErrorMessages.Returns.RollRecordAnswersJson)]
	[Arguments("rollinfo(abc)", ErrorMessages.Returns.NoSuchRoll)]
	[Arguments("rollinfo(0)", ErrorMessages.Returns.NoSuchRoll)]
	public async Task RefusalsSayWhy(string softcode, string error)
	{
		await Assert.That(await Evaluate(softcode)).IsEqualTo(error);
	}
}
