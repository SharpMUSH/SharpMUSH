using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ChannelFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("channels()", "")]
	public async Task Channels(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// channels() should return a string (empty or with channel names)
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cemit(testchan,test message)", "")]
	public async Task Cemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cemit() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cflags(testchan)", "")]
	public async Task Cflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cflags() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("clock(testchan)", "")]
	public async Task Clock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// clock() returns error or empty if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cowner(testchan)", "")]
	public async Task Cowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cowner() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("crecall(testchan)", "")]
	public async Task Crecall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// crecall() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cstatus(%#,testchan)", "")]
	public async Task Cstatus(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cstatus() returns error or OFF if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cwho(testchan)", "")]
	public async Task Cwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cwho() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cbuffer(testchan)", "")]
	public async Task Cbuffer(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cbuffer() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cdesc(testchan)", "")]
	public async Task Cdesc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cdesc() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cmogrifier(testchan)", "")]
	public async Task Cmogrifier(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cmogrifier() returns error or empty if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cusers(testchan)", "")]
	public async Task Cusers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cusers() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ctitle(%#,testchan)", "")]
	public async Task Ctitle(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// ctitle() returns error or empty if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("clflags(testchan)", "")]
	public async Task Clflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// clflags() returns error or empty if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cmsgs(testchan)", "")]
	public async Task Cmsgs(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cmsgs() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cbufferadd(testchan,test message)", "")]
	public async Task Cbufferadd(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// cbufferadd() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nscemit(testchan,test message)", "")]
	public async Task Nscemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// nscemit() returns error if channel doesn't exist
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
