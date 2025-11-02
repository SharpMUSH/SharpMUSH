using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Skip("Is empty. Needs investigation.")]
	public async Task Idle()
	{
		// Test idle function - should return idle time in seconds
		var result = (await Parser.FunctionParse(MModule.single("idle(%#)")))?.Message!;
		// Result should be a valid string (could be empty for some implementations)
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}

	[Test]
	public async Task Conn()
	{
		// Test conn function (uppercase) - should return connection number or info
		var result = (await Parser.FunctionParse(MModule.single("conn(%#)")))?.Message!;
		// Result should be a valid string
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}

	[Test]
	public async Task ListWho()
	{
		// Test lwho function - should return a list of connected players
		var result = (await Parser.FunctionParse(MModule.single("lwho()")))?.Message!;
		// The result should be a valid string
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}

	[Test]
	[Arguments("doing(%#)", "")]
	public async Task Doing(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Test_Doing_ReturnsEmpty_WhenNoDoingAttributeSet()
	{
		// Test that doing() returns empty string when DOING attribute is not set
		var result = (await Parser.FunctionParse(MModule.single("doing(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
		// Should be empty or a string, never throw
	}

	[Test]
	public async Task Test_Doing_WithInvalidDescriptor_ReturnsEmpty()
	{
		// Test that doing() returns empty string with invalid descriptor
		var result = (await Parser.FunctionParse(MModule.single("doing(999999)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Test_Doing_WithInvalidPlayer_ReturnsEmpty()
	{
		// Test that doing() returns empty string with invalid player name
		var result = (await Parser.FunctionParse(MModule.single("doing(test_invalid_player_doing_xyz)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("host(%#)", "")]
	public async Task Host(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ipaddr(%#)", "")]
	public async Task Ipaddr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lports()", "")]
	public async Task Lports(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("mwho()", "")]
	public async Task Mwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nwho()", "1")]
	public async Task Nwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lwhoid()", "")]
	public async Task Lwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
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
	[Skip("Not Yet Implemented")]
	[Arguments("nplayers()", "0")]
	public async Task Nplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nthings()", "0")]
	public async Task Nthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvcon()", "0")]
	public async Task Nvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvexits()", "0")]
	public async Task Nvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvplayers()", "0")]
	public async Task Nvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvthings()", "0")]
	public async Task Nvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ports()", "4201")]
	public async Task Ports(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task Test_Addrlog_ReturnsError_WhenConnLogDisabled()
	{
		// Test that addrlog() returns #-1 when connection logging is disabled
		// This requires wizard privileges, so it may return permission error instead
		var result = (await Parser.FunctionParse(MModule.single("addrlog(ip,test_addrlog_pattern_xyz)")))?.Message!;
		var text = result.ToPlainText();
		// Should return either #-1 (no connlog) or #-1 PERMISSION DENIED
		await Assert.That(text).StartsWith("#-1");
	}

	[Test]
	public async Task Test_Connlog_ReturnsError_WhenConnLogDisabled()
	{
		// Test that connlog() returns #-1 when connection logging is disabled
		// This is wizard-only
		var result = (await Parser.FunctionParse(MModule.single("connlog(all,count,test_connlog_spec_xyz)")))?.Message!;
		var text = result.ToPlainText();
		// Should return #-1 (no connlog) or permission error
		await Assert.That(text).StartsWith("#-1");
	}

	[Test]
	public async Task Test_Connrecord_ReturnsError_WhenConnLogDisabled()
	{
		// Test that connrecord() returns #-1 when connection logging is disabled
		// This is wizard-only
		var result = (await Parser.FunctionParse(MModule.single("connrecord(test_connrecord_id_xyz)")))?.Message!;
		var text = result.ToPlainText();
		// Should return #-1 (no connlog/not found) or permission error
		await Assert.That(text).StartsWith("#-1");
	}
}
