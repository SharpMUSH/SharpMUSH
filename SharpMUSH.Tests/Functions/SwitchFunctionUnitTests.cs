using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class SwitchFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("switch(a,a,1,b,2,0)", "1")]
	[Arguments("switch(c,a,1,b,2,0)", "0")]
	public async Task Switch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitch(abc,a*,1,b*,2,0)", "1")]
	public async Task Reswitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchall(abc,a*,1,b*,2)", "1 2")]
	public async Task Reswitchall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchi(ABC,a*,1,b*,2,0)", "1")]
	public async Task Reswitchi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitchalli(ABC,a*,1,b*,2)", "1 2")]
	public async Task Reswitchalli(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("slev()", "0")]
	public async Task SlevOutsideSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(foo,foo,slev(),0)", "1")]
	[Arguments("switch(foo,foo,switch(bar,bar,slev(),0),0)", "2")]
	public async Task SlevInsideSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stext()", "")]
	public async Task StextOutsideSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(hello,hello,stext(),0)", "hello")]
	[Arguments("switch(world,world,stext(0),0)", "world")]
	public async Task StextInsideSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(0),0),0)", "inner")]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(1),0),0)", "outer")]
	[Arguments("switch(outer,outer,switch(inner,inner,stext(L),0),0)", "outer")]
	public async Task StextNestedSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("reswitch(test,t.*,stext(),0)", "test")]
	public async Task StextInsideReswitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stext(invalid)", "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER")]
	[Arguments("stext(-1)", "#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER")]
	public async Task StextWithInvalidArguments(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(test,test,stext(10),0)", "")]
	public async Task StextBeyondDepth(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(test,test,stext(l),0)", "test")]
	public async Task StextLowercaseL(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(hello,hello,%$0,0)", "hello")]
	[Arguments("switch(world,world,%$0,0)", "world")]
	public async Task PercentDollarRegisterSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("switch(outer,outer,switch(inner,inner,%$0,0),0)", "inner")]
	[Arguments("switch(outer,outer,switch(inner,inner,%$1,0),0)", "outer")]
	[Arguments("switch(outer,outer,switch(inner,inner,%$L,0),0)", "outer")]
	public async Task PercentDollarNestedSwitch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
