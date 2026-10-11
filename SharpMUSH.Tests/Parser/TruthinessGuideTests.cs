namespace SharpMUSH.Tests.Parser;

/// <summary>
/// The softcode skill's and Code Style guide's "t() in a condition" section. Every condition reads its
/// value with the same test <c>t()</c> uses (<c>Predicates.Truthy</c>, PennMUSH's <c>parse_boolean</c>),
/// under the same <c>tiny_booleans</c> setting, so <c>t()</c> inside one changes nothing. Where a value is
/// compared, counted or kept as text, it does change the result.
/// </summary>
public class TruthinessGuideTests : ServerTestBase
{
	private static readonly string[] Values = ["", "0", "1", "5", "-1", "abc", "false", "#-1 NO MATCH", "#5", "-0", "0.0", "0x0", "1abc", "0abc", "%b"];

	// {0} is the value being tested.
	private static readonly string[] Conditions =
	[
		"if({0},yes,no)", "ifelse({0},yes,no)", "cond({0},yes,no)", "ncond({0},yes,no)", "condall({0},yes,no)", "ncondall({0},yes,no)",
		"not({0})", "and({0},1)", "or({0},0)", "xor({0},0)", "nand({0},1)", "nor({0},0)",
		"cand({0},1)", "cor({0},0)", "ncand({0},1)", "ncor({0},0)"
	];

	private async Task<string> Evaluate(string code, bool tinyBooleans)
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { TinyBooleans = tinyBooleans }
		});
		return await Eval(code);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AFunctionConditionGivesTheSameAnswerWithoutT(bool tinyBooleans)
	{
		foreach (var condition in Conditions)
		{
			foreach (var value in Values)
			{
				var bare = await Evaluate(string.Format(condition, value), tinyBooleans);
				var wrapped = await Evaluate(string.Format(condition, $"t({value})"), tinyBooleans);
				await Assert.That(wrapped).IsEqualTo(bare).Because($"{condition} with {value}");
			}
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AListPredicateGivesTheSameAnswerWithoutT(bool tinyBooleans)
	{
		const string list = "1 0 5 abc #-1 -0 0.0 1abc";
		foreach (var function in new[] { "filterbool", "every", "some" })
		{
			var bare = await Evaluate($"{function}(lit(#lambda/%0),{list})", tinyBooleans);
			var wrapped = await Evaluate($"{function}(lit(#lambda/[t(%0)]),{list})", tinyBooleans);
			await Assert.That(wrapped).IsEqualTo(bare).Because(function);
		}
	}

	[Test]
	[Arguments("")]
	[Arguments("0")]
	[Arguments("1")]
	[Arguments("abc")]
	[Arguments("#-1 NO MATCH")]
	[Arguments("-0")]
	public async Task ACommandConditionGivesTheSameAnswerWithoutT(string value)
	{
		foreach (var command in new[] { "@break", "@assert" })
		{
			foreach (var test in new[] { value, $"t({value})" })
			{
				var attribute = "TRU" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
				await Cmd($"&{attribute}`STEP me={command} {test}=&{attribute}`OUT me=stopped;&{attribute}`OUT me=ran on");
				await Cmd($"@include me/{attribute}`STEP");
				var outcome = await Eval($"get(me/{attribute}`OUT)");

				var expected = (command == "@break") == Predicate(value) ? "stopped" : "ran on";
				await Assert.That(outcome).IsEqualTo(expected).Because($"{command} {test}");
			}
		}

		foreach (var test in new[] { value, $"t({value})" })
		{
			var attribute = "TRU" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
			await Cmd($"&{attribute}`STEP me=@if {test}={{&{attribute}`OUT me=yes}},{{&{attribute}`OUT me=no}}");
			await Cmd($"@include me/{attribute}`STEP");

			await Assert.That(await Eval($"get(me/{attribute}`OUT)")).IsEqualTo(Predicate(value) ? "yes" : "no")
				.Because($"@if {test}");
		}
	}

	private static bool Predicate(string value) => value is "1" or "abc";

	[Test]
	// A test whose result is a position, a count or a dbref needs no t() either.
	[Arguments("if(match(a b c,b),yes,no)", "yes")]
	[Arguments("if(match(a b c,d),yes,no)", "no")]
	[Arguments("if(member(a b c,c),yes,no)", "yes")]
	[Arguments("if(pos(x,abc),yes,no)", "no")]
	[Arguments("if(words(a b),yes,no)", "yes")]
	[Arguments("if(num(*nobody-here),yes,no)", "no")]
	[Arguments("not(pos(x,abc))", "1")]
	[Arguments("if(t(match(a b c,b)),1,0)", "1")]
	[Arguments("t(match(a b c,b))", "1")]
	public async Task APositionOrCountIsAlreadyACondition(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	// Where the value is compared, counted or kept, t() is what turns it into 1 or 0.
	[Arguments("filter(lit(#lambda/%0),1 2 abc 0)", "1")]
	[Arguments("filter(lit(#lambda/[t(%0)]),1 2 abc 0)", "1 2 abc")]
	[Arguments("filterbool(lit(#lambda/%0),1 2 abc 0)", "1 2 abc")]
	[Arguments("switch(1,match(a b c,c),yes,no)", "no")]
	[Arguments("switch(1,t(match(a b c,c)),yes,no)", "yes")]
	[Arguments("case(1,match(a b c,c),yes,no)", "no")]
	[Arguments("cond(match(a b c,c),yes,no)", "yes")]
	[Arguments("cand(pos(=,a=b),1)", "1")]
	[Arguments("add(t(abc),t(0),t(5))", "2")]
	[Arguments("t(match(a b c,c))", "1")]
	[Arguments("match(a b c,c)", "3")]
	// strlen() is not the same test as the value: "0" is false, but it is one character long.
	[Arguments("if(0,yes,no)", "no")]
	[Arguments("if(strlen(0),yes,no)", "yes")]
	public async Task WhereTChangesTheResult(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task TheHelpExamplesGiveWhatTheySay()
	{
		var attribute = "TRU" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
		await Cmd($"&{attribute} me=%0");

		await Assert.That(await Cmd("think if(match(a b c,b),yes,no)")).IsEqualTo("yes");
		await Assert.That(await Cmd("think not(pos(x,abc))")).IsEqualTo("1");
		await Assert.That(await Cmd($"think filter(me/{attribute},1 2 abc 0)")).IsEqualTo("1");
		await Assert.That(await Cmd($"think filterbool(me/{attribute},1 2 abc 0)")).IsEqualTo("1 2 abc");
		await Assert.That(await Cmd("think switch(1,match(a b c,c),yes,no)")).IsEqualTo("no");
		await Assert.That(await Cmd("think add(t(abc),t(0),t(5))")).IsEqualTo("2");
		await Assert.That(await Cmd("think add(abc,0,5)")).IsEqualTo("#-1 ARGUMENTS MUST BE NUMBERS");
	}
}
