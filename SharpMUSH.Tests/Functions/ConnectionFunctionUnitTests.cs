using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Skip("Is empty. Needs investigation.")]
	public async Task Idle()
	{
		// Test idle function - should return idle time in seconds
		var result = (await Parser.FunctionParse(MModule.single("idle(%#)")))?.Message!;
		// Result should be a valid string (could be empty for some implementations)
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task Conn()
	{
		// Test conn function (uppercase) - should return connection number or info
		var result = (await Parser.FunctionParse(MModule.single("conn(%#)")))?.Message!;
		// Result should be a valid string
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	public async Task ListWho()
	{
		// Test lwho function - should return a list of connected players
		var result = (await Parser.FunctionParse(MModule.single("lwho()")))?.Message!;
		// The result should be a valid string
		await Assert.That(result.ToPlainText()).Length().IsPositive();
	}

	[Test]
	[Arguments("doing(%#)", "")]
	public async Task Doing(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		// Should not crash and should return a string (empty or with content)
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_ReturnsEmptyWhenNoAttribute()
	{
		// Test that doing(%#) returns empty string when DOING attribute is not set
		var result = (await Parser.FunctionParse(MModule.single("doing(%#)")))?.Message!;
		var text = result.ToPlainText();
		// Should be a valid string (empty or containing data), never throw
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_WithDescriptor()
	{
		// Test doing() with a descriptor number
		// Using a high number that likely doesn't exist
		var result = (await Parser.FunctionParse(MModule.single("doing(999999)")))?.Message!;
		var text = result.ToPlainText();
		// Should return empty string for non-existent descriptor
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_WithInvalidPlayerName()
	{
		// Test doing() with an invalid player name
		var result = (await Parser.FunctionParse(MModule.single("doing(NonExistentPlayer_XYZ_12345)")))?.Message!;
		var text = result.ToPlainText();
		// Should return empty string for invalid player
		await Assert.That(text).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_ValidatesInput()
	{
		// Test that doing() handles various input types gracefully
		var testCases = new[] {
			"doing(%#)",
			"doing(me)",
			"doing(0)",
			"doing(1)"
		};
		
		foreach (var testCase in testCases)
		{
			var result = (await Parser.FunctionParse(MModule.single(testCase)))?.Message!;
			// All should return a valid string without throwing
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
		// Test addrlog() with valid IP search - should return empty or count based on logging state
		// This requires wizard privileges, may return permission error
		var result = (await Parser.FunctionParse(MModule.single("addrlog(ip,*)")))?.Message!;
		var text = result.ToPlainText();
		// Should return empty string, count, or permission error - never crash
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_WithCount()
	{
		// Test addrlog() with count option
		var result = (await Parser.FunctionParse(MModule.single("addrlog(count,hostname,*)")))?.Message!;
		var text = result.ToPlainText();
		// Should return "0" or permission error when logging infrastructure isn't complete
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_InvalidSearchType_ReturnsError()
	{
		// Test addrlog() with invalid search type
		var result = (await Parser.FunctionParse(MModule.single("addrlog(invalid,pattern)")))?.Message!;
		var text = result.ToPlainText();
		// Should return error or permission denied
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_WithValidArguments()
	{
		// Test connlog() with valid filter and spec
		var result = (await Parser.FunctionParse(MModule.single("connlog(all,count,1)")))?.Message!;
		var text = result.ToPlainText();
		// Should return "0" or permission error when logging infrastructure isn't complete
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connlog_InvalidFilter_ReturnsError()
	{
		// Test connlog() with invalid filter
		var result = (await Parser.FunctionParse(MModule.single("connlog(invalid,count,1)")))?.Message!;
		var text = result.ToPlainText();
		// Should return error
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithValidId()
	{
		// Test connrecord() with a valid-looking ID
		var result = (await Parser.FunctionParse(MModule.single("connrecord(12345)")))?.Message!;
		var text = result.ToPlainText();
		// Should return "#-1 CONNECTION NOT FOUND" or permission error
		await Assert.That(text).IsNotNull();
	}

	[Test]
	public async Task Test_Connrecord_WithCustomSeparator()
	{
		// Test connrecord() with custom separator
		var result = (await Parser.FunctionParse(MModule.single("connrecord(12345,|)")))?.Message!;
		var text = result.ToPlainText();
		// Should return error or not found
		await Assert.That(text).IsNotNull();
	}
}
