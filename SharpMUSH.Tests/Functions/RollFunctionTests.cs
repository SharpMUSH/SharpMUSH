using System.Globalization;
using System.Text.Json;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// roll() as softcode calls it. Dice are random here, so totals are checked against their range;
/// the dice engine is covered with scripted faces in <c>DiceRollTests</c>.
/// </summary>
public class RollFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Evaluate(string softcode)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(softcode))).ToPlainText();

	private async Task<decimal> Total(string softcode)
		=> decimal.Parse(await Evaluate(softcode), CultureInfo.InvariantCulture);

	[Test]
	[Arguments("roll(2d6+5)", 7, 17)]
	[Arguments("roll(2d6kh3+2)", 4, 14)]
	[Arguments("roll(4dF)", -4, 4)]
	[Arguments("roll(d20)", 1, 20)]
	[Arguments("roll(d%%)", 1, 100)]
	[Arguments("roll(1d6,,total)", 1, 6)]
	[Arguments("roll({max(1d6,1d6)+2})", 3, 8)]
	[Arguments("roll(lit(floor(1d6/2)))", 0, 3)]
	[Arguments("roll(lit({1d20,10}kh))", 10, 20)]
	[Arguments("roll(lit(2d6[fire]+1))", 3, 13)]
	public async Task TotalsFallInTheirRange(string softcode, int lowest, int highest)
	{
		for (var i = 0; i < 20; i++)
		{
			var total = await Total(softcode);
			await Assert.That(total).IsGreaterThanOrEqualTo(lowest);
			await Assert.That(total).IsLessThanOrEqualTo(highest);
		}
	}

	[Test]
	[Arguments("roll(2d6x+1dF+2,,min)", "3")]
	[Arguments("roll(2d6x+1dF+2,,max)", "15")]
	[Arguments("""roll(4d6kh3,,lit({"mode":"max"}))""", "18")]
	[Arguments("roll(1d20-1d4,,min)", "-3")]
	[Arguments("roll(1d20-1d4,,max)", "19")]
	public async Task MinAndMaxGiveTheRange(string softcode, string expected)
	{
		await Assert.That(await Evaluate(softcode)).IsEqualTo(expected);
	}

	[Test]
	public async Task VariablesComeFromAJsonObject()
	{
		var softcode = """roll(1dF+DEXTERITY,lit({"DEXTERITY":"1d6+SPEED","SPEED":2}))""";
		for (var i = 0; i < 20; i++)
		{
			var total = await Total(softcode);
			await Assert.That(total).IsGreaterThanOrEqualTo(2);
			await Assert.That(total).IsLessThanOrEqualTo(9);
		}
	}

	[Test]
	public async Task VariablesComeFromAnObjectsAttributes()
	{
		// '-' is minus in a formula, so the name keeps to letters, digits and _.
		var root = TestIsolationHelpers.GenerateUniqueName("STAT").ToUpperInvariant().Replace('-', '_');
		await WebAppFactoryArg.CommandParser.CommandParse(MarkupText.Plain($"&{root} me=stats"));
		await WebAppFactoryArg.CommandParser.CommandParse(MarkupText.Plain($"&{root}`SPEED me=2"));
		await WebAppFactoryArg.CommandParser.CommandParse(MarkupText.Plain($"&{root}`DEX me=1d6+@{root.ToLowerInvariant()}.speed"));

		var text = await Evaluate($"roll(1dF+{root.ToLowerInvariant()}.dex,me,text)");

		await Assert.That(text).Matches(
			$@"^1dF\[-?[01]\] \+ {root.ToLowerInvariant()}\.dex\(1d6\[[1-6]\] \+ {root.ToLowerInvariant()}\.speed\(2\)\) = \d+$");
	}

	[Test]
	public async Task AMissingAttributeIsNamed()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("NOSTAT").ToUpperInvariant().Replace('-', '_');

		await Assert.That(await Evaluate($"roll(1d6+{name},me)"))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollNoSuchVariableFormat, name));
	}

	[Test]
	public async Task VariablesCanBeJsonStoredInAnAttribute()
	{
		var attribute = TestIsolationHelpers.GenerateUniqueName("STATS").ToUpperInvariant();
		await WebAppFactoryArg.CommandParser.CommandParse(MarkupText.Plain(
			$$$"""&{{{attribute}}} me={"DEXTERITY":"1d6+SPEED","SPEED":2,"skills":{"stealth":3}}"""));

		var text = await Evaluate($"roll(1dF+DEXTERITY+skills.stealth,v({attribute}),text)");

		await Assert.That(text).Matches(@"^1dF\[-?[01]\] \+ DEXTERITY\(1d6\[[1-6]\] \+ SPEED\(2\)\) \+ skills\.stealth\(3\) = \d+$");
	}

	[Test]
	public async Task TextShowsEveryDie()
	{
		await Assert.That(await Evaluate("roll(4d6kh3,,text)")).Matches(@"^4d6kh3\[.*\(\d\).*\] = \d+$");
	}

	[Test]
	public async Task JsonIsJson()
	{
		var json = await Evaluate("roll(2d6kh1+1,,json)");
		using var document = JsonDocument.Parse(json);

		await Assert.That(document.RootElement.GetProperty("formula").GetString()).IsEqualTo("2d6kh1+1");
		await Assert.That(document.RootElement.GetProperty("dice")[0].GetProperty("results").GetArrayLength()).IsEqualTo(2);
	}

	[Test]
	[Arguments("valid(roll,4d6kh3+STR)", "1")]
	[Arguments("valid(roll,lit({1d20,10}kh))", "1")]
	[Arguments("valid(roll,2d6+)", "0")]
	public async Task ValidChecksAFormula(string softcode, string expected)
	{
		await Assert.That(await Evaluate(softcode)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("roll(1d6,lit({not json}))", ErrorMessages.Returns.RollInvalidJson)]
	[Arguments("roll(1d6,json(array,1))", ErrorMessages.Returns.RollDataMustBeObject)]
	[Arguments("roll(1d6,,sum)", ErrorMessages.Returns.RollOutputMode)]
	[Arguments("roll(1d6+STR)", "#-1 NO SUCH ROLL VARIABLE: STR")]
	[Arguments("roll(1001d6)", ErrorMessages.Returns.RollTooManyDice)]
	public async Task Errors(string softcode, string expected)
	{
		await Assert.That(await Evaluate(softcode)).IsEqualTo(expected);
	}
}
