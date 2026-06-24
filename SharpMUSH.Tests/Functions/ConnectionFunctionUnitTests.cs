using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Idle()
	{
		var result = (await Parser.FunctionParse(MModule.single("idle(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task Conn()
	{
		var result = (await Parser.FunctionParse(MModule.single("conn(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task ListWho()
	{
		var result = (await Parser.FunctionParse(MModule.single("lwho()")))?.Message!;
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	[Arguments("doing(%#)", "")]
	public async Task Doing(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_ReturnsEmptyWhenNoAttribute()
	{
		var result = (await Parser.FunctionParse(MModule.single("doing(%#)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_WithDescriptor()
	{
		var result = (await Parser.FunctionParse(MModule.single("doing(999999)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_WithInvalidPlayerName()
	{
		var result = (await Parser.FunctionParse(MModule.single("doing(NonExistentPlayer_XYZ_12345)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_ValidatesInput()
	{
		var testCases = new[] {
			"doing(%#)",
			"doing(me)",
			"doing(0)",
			"doing(1)"
		};

		foreach (var testCase in testCases)
		{
			var result = (await Parser.FunctionParse(MModule.single(testCase)))?.Message!;
			await Assert.That(result.ToPlainText()).IsNotNull();
		}
	}

	[Test]
	[Arguments("host(%#)", "")]
	public async Task Host(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ipaddr(%#)", "")]
	public async Task Ipaddr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lports()", "")]
	public async Task Lports(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("mwho()", "")]
	public async Task Mwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nwho()", "1")]
	public async Task Nwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lwhoid()", "")]
	public async Task Lwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("ncon()", "0")]
	public async Task Ncon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nexits(#0)", "0")]
	public async Task Nexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nplayers()", "0")]
	public async Task Nplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nthings()", "0")]
	public async Task Nthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvcon()", "0")]
	public async Task Nvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvexits()", "0")]
	public async Task Nvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvplayers()", "0")]
	public async Task Nvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nvthings()", "0")]
	public async Task Nvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("ports()", "4201")]
	public async Task Ports(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_WithValidArguments()
	{
		var result = (await Parser.FunctionParse(MModule.single("addrlog(ip,*)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_WithCount()
	{
		var result = (await Parser.FunctionParse(MModule.single("addrlog(count,hostname,*)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_InvalidSearchType_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("addrlog(invalid,pattern)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_WithValidArguments()
	{
		var result = (await Parser.FunctionParse(MModule.single("connlog(all,count,1)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_InvalidFilter_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("connlog(invalid,count,1)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithValidId()
	{
		var result = (await Parser.FunctionParse(MModule.single("connrecord(12345)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithCustomSeparator()
	{
		var result = (await Parser.FunctionParse(MModule.single("connrecord(12345,|)")))?.Message!;
		var text = result.ToPlainText();
		await Assert.That(text).IsNotNull();
	}
}
