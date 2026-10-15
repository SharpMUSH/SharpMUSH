using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class SwitchFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// switch() glob-matches through the same quick_wild as everything else, so it is
	/// case-insensitive; verified against a live PennMUSH 1.8.8, where switch(ABC,abc,YES,NO) is
	/// YES.
	/// </summary>
	[Test]
	[Arguments("switch(a,a,1,b,2,0)", "1")]
	[Arguments("switch(c,a,1,b,2,0)", "0")]
	[Arguments("switch(ABC,abc,YES,NO)", "YES")]
	[Arguments("switch(abc,AB*,YES,NO)", "YES")]
	public async Task Switch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitch(abc,a*,1,b*,2,0)", "1")]
	// Penn reswitch.1-3: regex matching (case-sensitive)
	[Arguments("reswitch(test STRING,t,1,0)", "1")]
	[Arguments("reswitch(test STRING,t,1,e,2,0)", "1")]
	[Arguments("reswitch(test STRING,E,1,0)", "0")]
	[Arguments("reswitch(test,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,t.*,match)", "match")]
	[Arguments("reswitch(test,x.*,nomatch,default)", "default")]
	// Penn reswitch.4, reswitchi.12, reswitchall.8 and reswitchalli.16 (complex regex with special
	// chars) are not here: SharpMUSH evaluates NoParse pattern args via ParsedMessage(), so {4},
	// [A-Z], {6} get consumed by the parser. PennMUSH passes them raw.
	public async Task Reswitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchall(abc,a*,1,b*,2)", "12")]
	// Penn reswitch.5-7: reswitchall matches all patterns
	[Arguments("reswitchall(test STRING,t,1,0)", "1")]
	[Arguments("reswitchall(test STRING,t,1,e,2,0)", "12")]
	[Arguments("reswitchall(test STRING,E,1,0)", "0")]
	[Arguments("reswitchall(test,t.*,match1,e.*,match2)", "match1match2")]
	public async Task Reswitchall(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchi(ABC,a*,1,b*,2,0)", "1")]
	// Penn reswitch.9-11: case-insensitive matching
	[Arguments("reswitchi(test STRING,t,1,0)", "1")]
	[Arguments("reswitchi(test STRING,t,1,e,2,0)", "1")]
	[Arguments("reswitchi(test STRING,E,1,0)", "1")]
	[Arguments("reswitchi(TEST,t.*,match)", "match")]
	public async Task Reswitchi(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchalli(ABC,a*,1,b*,2)", "12")]
	// Penn reswitch.13-15: case-insensitive match-all
	[Arguments("reswitchalli(test STRING,t,1,0)", "1")]
	[Arguments("reswitchalli(test STRING,t,1,e,2,0)", "12")]
	[Arguments("reswitchalli(test STRING,E,1,0)", "1")]
	[Arguments("reswitchalli(TEST,t.*,match1,e.*,match2)", "match1match2")]
	public async Task Reswitchalli(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("slev()", "0")]
	public async Task SlevOutsideSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(foo,foo,slev(),0)", "1")]
	[Arguments("switch(foo,foo,switch(bar,bar,slev(),0),0)", "2")]
	public async Task SlevInsideSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stext()", "")]
	public async Task StextOutsideSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(hello,hello,stext(),0)", "hello")]
	[Arguments("switch(world,world,stext(0),0)", "world")]
	public async Task StextInsideSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(0),0),0)", "inner")]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(1),0),0)", "outer")]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(L),0),0)", "outer")]
	public async Task StextNestedSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitch(test,t.*,stext(),0)", "test")]
	public async Task StextInsideReswitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stext(invalid)", "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER")]
	[Arguments("stext(-1)", "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER")]
	public async Task StextWithInvalidArguments(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(test,test,stext(10),0)", "")]
	public async Task StextBeyondDepth(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(test,test,stext(l),0)", "test")]
	public async Task StextLowercaseL(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(hello,hello,%$0,0)", "hello")]
	[Arguments("switch(world,world,%$0,0)", "world")]
	public async Task PercentDollarRegisterSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(outer,outer,switch(inner,inner,%$0,0),0)", "inner")]
	[Arguments("switch(outer,outer,switch(inner,inner,%$1,0),0)", "outer")]
	[Arguments("switch(outer,outer,switch(inner,inner,%$L,0),0)", "outer")]
	public async Task PercentDollarNestedSwitch(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// #$ is the subject in the list or default that runs, as PennMUSH's fun_switch splices it. A nested switch's
	/// #$ names the outermost switch whose list it is written in. case(), caseall(), if() and ifelse() bind it too,
	/// as TinyMUX's do, but give way to a switch() family around or inside them, which PennMUSH code expects.
	/// </summary>
	[Test]
	[Arguments("switch(moof,*a*,foo,*b*,bar,*t*,neat,#$)", "moof")]
	[Arguments("switch(abc,a*,<#$>,no)", "<abc>")]
	[Arguments("switchall(abc,a*,#$,*c,-#$)", "abc-abc")]
	[Arguments("case(moof,moof,#$)", "moof")]
	[Arguments("caseall(moof,moof,#$,moof,-#$)", "moof-moof")]
	[Arguments("case(moof,x,no,#$)", "moof")]
	[Arguments("case(b,#$,x,y)", "y")]
	[Arguments("if(1,#$)", "1")]
	[Arguments("if(0,x,<#$>)", "<0>")]
	[Arguments("ifelse(abc,#$,no)", "abc")]
	[Arguments("if(1,switch(b,*,#$))", "b")]
	[Arguments("switch(a,*,if(1,#$))", "a")]
	[Arguments("if(abc,case(d,d,#$))", "abc")]
	[Arguments("if(#$,#$)", "#$")]
	[Arguments("reswitch(abc,^a,#$)", "abc")]
	[Arguments("switch(a,*,switch(b,*,#$))", "a")]
	[Arguments("switch(a,*,switch(b,*,%$0#$))", "ba")]
	[Arguments("switch(a,*,reswitch(b,b,#$))", "a")]
	[Arguments("case(a,a,switch(b,*,#$))", "b")]
	[Arguments("switch(a,*,case(b,b,#$))", "a")]
	[Arguments("iter(x y,switch(##,*,#$))", "x y")]
	// if() is not a switch: stext(), %$0 and slev() pass over it.
	[Arguments("if(1,[stext(0)]-[slev()]-%$0)", "-0-#-1 REGISTER OUT OF RANGE")]
	[Arguments("switch(a,*,if(1,[stext(0)]-[slev()]-%$0))", "a-1-a")]
	public async Task HashDollarIsTheSubject(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// #$ is text where no switch's list reads it: in a pattern (PennMUSH splices only the list), outside any
	/// switch, unevaluated, escaped, and in the subject itself, which is never evaluated again.
	/// </summary>
	[Test]
	[Arguments("#$", "#$")]
	[Arguments("switch(abc,#$,yes,no)", "no")]
	[Arguments("switch(#$,#$,yes,no)", "yes")]
	[Arguments("switch(b,*,switch(b,#$,x,y))", "x")]
	[Arguments("switch(a,*,lit(#$))", "#$")]
	[Arguments("switch(a,*,\\#$)", "#$")]
	[Arguments("switch(lit([add(1,1)]),*,#$)", "[add(1,1)]")]
	[Arguments("switch(a,*,#$0 x#)", "a0 x#")]
	// The subject's text is inserted, never evaluated again: PennMUSH splices it into the code first.
	[Arguments("switch(lit(%#),*,#$)", "%#")]
	[Arguments("switch(lit([add(1,1)]),*,#$:%$0)", "[add(1,1)]:[add(1,1)]")]
	public async Task HashDollarAsText(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// #$ belongs to the switch it is written in: an attribute the list calls keeps it as text, unless that
	/// attribute runs a switch of its own.
	/// </summary>
	[Test]
	public async Task HashDollarStaysInItsOwnAttribute()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("HashDollarObj");
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService, name);
		var obj = $"#{DBRef.Parse(created.Message!.ToPlainText()).Number}";
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&SHOW {obj}=#$"));
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&OWN {obj}=switch(z,*,#$)"));

		async Task Check(string fn, string exp) =>
			await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain(fn))).ToPlainText()).IsEqualTo(exp);

		await Check($"switch(a,*,u({obj}/SHOW))", "#$");
		await Check($"switch(a,*,u({obj}/OWN))", "z");
		await Check($"switch(a,*,#$[u({obj}/SHOW)])", "a#$");
	}
}
