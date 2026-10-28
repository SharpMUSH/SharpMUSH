using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The functions TinyMUX games call that PennMUSH has no name for, and the TinyMUX argument forms of
/// functions both have. Expected answers are TinyMUX 2.14's documented ones where it gives them.
/// </summary>
public class MuxFunctionTests : ServerTestBase
{
	private static IDisposable QueueFunctions(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxQueueFunctions = on }
		});

	[Test]
	[Arguments("strip(This is a test,is)", "Th  a tet")]
	[Arguments("strip(abcdefgABCDEFG,azGy!7$B)", "bcdefgACDEF")]
	[Arguments("strip(ansi(r,red))", "red")]
	[Arguments("[between(1,10,5)][between(1,10,10)][between(1,10,10,1)][between(1.5,2.5,2)]", "1011")]
	[Arguments("between(a,10,5)", ErrorMessages.Returns.Numbers)]
	[Arguments("[roman(1999)] [roman(4)] [roman(3999)]", "MCMXCIX IV MMMCMXCIX")]
	[Arguments("roman(0)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("roman(4000)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("roman(x)", ErrorMessages.Returns.Integer)]
	[Arguments("[crc32(MUX)] [crc32(M,UX)] [crc32()]", "629126998 629126998 0")]
	[Arguments("[strmem(This is a test)] [strmem(chr(233))]", "14 2")]
	[Arguments("ledit(apple banana lime plum,banana plum,yellow purple)", "apple yellow lime purple")]
	[Arguments("ledit(apple|banana|lime|plum,banana|lime,yellow,|,+)", "apple+yellow++plum")]
	[Arguments("ledit(a b c,d,x)", "a b c")]
	[Arguments("ledit(a b A,a,x)", "x b A")]
	[Arguments("moon(947182440)", "The Moon is New")]
	[Arguments("moon(948458160)", "The Moon is Full")]
	[Arguments("moon(949000000)", "The Moon is Waning Gibbous (62% of Full)")]
	[Arguments("moon(947820000)", "The Moon is Waxing Half (50% of Full)")]
	[Arguments("moon(947800000)", "The Moon is Waxing Crescent (48% of Full)")]
	[Arguments("[moon(948000000,1)] [moon(949000000,1)]", "71 -62")]
	[Arguments("moon(x)", ErrorMessages.Returns.Number)]
	[Arguments("lrand(5,5,3,|)", "5|5|5")]
	[Arguments("[lrand(1,6,0)][lrand(6,1,3)]", "")]
	[Arguments("[distribute(10,1)] [distribute(0,3,|)]", "10 0|0|0")]
	[Arguments("distribute(1,0)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("distribute(1000001,1)", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("distribute(x,1)", ErrorMessages.Returns.Integers)]
	[Arguments("[choose(red blue green,0 1 0)] [choose(red|blue|green,5 -1 0,|)]", "blue red")]
	[Arguments("choose(red blue,1)", ErrorMessages.Returns.ListsMustBeEqualSize)]
	[Arguments("choose(red blue,0 0)", "")]
	[Arguments("bittype(#1)", "7")]
	public async Task TinyMuxFunctions(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	[Arguments("ansi(r,Red,g,Green)", "RedGreen")]
	[Arguments("ansi(r,Red,g)", "#-1 FUNCTION (ANSI) EXPECTS AN EVEN NUMBER OF ARGUMENTS BUT GOT 3")]
	[Arguments("extract(a|b|c|d,2,2,|,-)", "b-c")]
	[Arguments("extract(a|b|c|d,2,2,|,)", "bc")]
	[Arguments("insert(a|c,2,b,|,-)", "a-b-c")]
	[Arguments("remove(a|b|c,b,|,-)", "a-c")]
	[Arguments("secs(utc,8)", ErrorMessages.Returns.InvalidPrecision)]
	public async Task TinyMuxArgumentForms(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	[Test]
	public async Task SecsTakesAZoneAndDecimalPlaces()
	{
		await Assert.That(await Eval("secs(utc,3)")).Matches(@"^\d+\.\d{3}$");
		await Assert.That(await Eval("secs(local,0)")).Matches(@"^\d+$");
		await Assert.That(await Eval("secs(utc)")).Matches(@"^\d+$");
	}

	[Test]
	public async Task RandomFunctionsStayInRange()
	{
		var rolls = (await Eval("lrand(1,6,500)")).Split(' ').Select(int.Parse).ToArray();
		await Assert.That(rolls.Length).IsEqualTo(500);
		await Assert.That(rolls.All(roll => roll is >= 1 and <= 6)).IsTrue();

		var bins = (await Eval("distribute(1000,7)")).Split(' ').Select(int.Parse).ToArray();
		await Assert.That(bins.Length).IsEqualTo(7);
		await Assert.That(bins.Sum()).IsEqualTo(1000);

		await Assert.That(await Eval("lrand(1,6,9000)")).Matches(@"^(\d (?!$)){7999}\d$");
	}

	[Test]
	public async Task CommandListsReadDollarAndCaretAttributes()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxCmds");
		await Eval($"[setq(0,create({thing}))][attrib_set(%q0/CMD.HELLO,lit($Hello *:think hi))][attrib_set(%q0/LISTEN.WAVE,lit(^* waves:think wave))][attrib_set(%q0/NOTE,text)]");
		var dbref = await Eval($"num({thing})");

		await Assert.That(await Eval($"lcmds({dbref})")).IsEqualTo("hello *");
		await Assert.That(await Eval($"lcmds({dbref},|,^)")).IsEqualTo("* waves");
		await Assert.That(await Eval($"lattrcmds({dbref})")).IsEqualTo("CMD.HELLO");
		await Assert.That(await Eval($"lattrcmds({dbref}/NOTE)")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task RoomsWithinFollowExitsByDepth()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("MuxRooms");
		var rooms = (await Eval($"[setq(0,num(dig({name}A)))][setq(1,num(dig({name}B)))][setq(2,num(dig({name}C)))][null(open({name}AB,%q1,%q0),open({name}BC,%q2,%q1),open({name}BA,%q0,%q1))]%q0 %q1 %q2"))
			.Split(' ');
		await Assert.That(rooms.Length).IsEqualTo(3);
		var (a, b, c) = (rooms[0], rooms[1], rooms[2]);

		await Assert.That(await Eval($"lrooms({a})")).IsEqualTo(b);
		await Assert.That(await Eval($"lrooms({a},2)")).IsEqualTo($"{b} {c}");
		await Assert.That(await Eval($"lrooms({a},2,0)")).IsEqualTo(c);
		await Assert.That(await Eval($"lrooms({a},0)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"lrooms({a},51)")).IsEqualTo(ErrorMessages.Returns.NumberOutOfRange);
		await Assert.That(await Eval($"lrooms(me)")).IsEqualTo(ErrorMessages.Returns.NotARoom);
	}

	[Test]
	public async Task DestroyMarksTheObjectForDestruction()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxDestroy");
		await Assert.That(await Eval($"[setq(0,create({thing}))][destroy(%q0)][hasflag(%q0,GOING)]")).IsEqualTo("1");
	}

	[Test]
	public async Task QueueFunctionsAreOffByDefault()
	{
		using var _ = QueueFunctions(false);

		await Assert.That(await Eval("trigger(me/NOTHING)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
		await Assert.That(await Eval("verb(me)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
	}

	[Test]
	public async Task TriggerQueuesTheAttributeQuietly()
	{
		using var _ = QueueFunctions(true);
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxTrigger");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxTriggered");
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval($"[setq(0,create({thing}))][attrib_set(%q0/GO,lit(@pemit %#={marker} %0))][trigger(%q0/GO,arg)]"))
			.IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, $"{marker} arg", startIndex: before);
		await Assert.That(Notifications.For(god).Skip(before).Any(line => line.Contains($"{thing} - Triggered."))).IsFalse();
	}

	[Test]
	public async Task TriggerQuietSwitchLeavesOutTheNotice()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxTriggerCmd");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxTriggerCmdRan");
		var god = WebAppFactoryArg.ExecutorDBRef;
		await Eval($"[setq(0,create({thing}))][attrib_set(%q0/GO,lit(@pemit %#={marker}))]");
		var before = Notifications.CountFor(god);

		await Cmd($"@trigger/quiet {thing}/GO");

		await Notifications.WaitForAsync(god, marker, startIndex: before);
		await Assert.That(Notifications.For(god).Skip(before).Any(line => line.Contains($"{thing} - Triggered."))).IsFalse();
	}

	[Test]
	public async Task VerbTellsTheActorWhatAttribute()
	{
		using var _ = QueueFunctions(true);
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxVerb");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxVerbed");
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = Notifications.CountFor(god);

		await Assert.That(await Eval($"[setq(0,create({thing}))][attrib_set(%q0/XTEST,{marker})][verb(%q0,me,XTEST,default)]"))
			.IsEqualTo(string.Empty);

		await Notifications.WaitForAsync(god, marker, startIndex: before);
	}
}
