using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests for newly implemented PennMUSH functions
/// </summary>
public class NewPennMUSHFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	#region DELETE/INSERT Tests (Aliases for ldelete/linsert)

	[Test]
	[Arguments("delete(a b c d,2)", "a c d")]
	[Arguments("delete(a b c d,1)", "b c d")]
	[Arguments("delete(one|two|three,2,|)", "one|three")]
	public async Task DELETE_RemovesItemFromList(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("insert(This is a string,4,test)", "This is a test string")]
	[Arguments("insert(one|three|four,2,two,|)", "one|two|three|four")]
	[Arguments("insert(meep bleep gleep,-3,GOOP)", "meep GOOP bleep gleep")]
	public async Task INSERT_AddsItemToList(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("linsert(This is a string,4,test)", "This is a test string")]
	[Arguments("linsert(one|three|four,2,two,|)", "one|two|three|four")]
	[Arguments("linsert(meep bleep gleep,-3,GOOP)", "meep GOOP bleep gleep")]
	public async Task LINSERT_InsertsItemAtPosition(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	#endregion

	#region LCSTR2/UCSTR2 Tests

	[Test]
	[Arguments("lcstr2(Foo BAR bAz)", "foo bar baz")]
	[Arguments("lcstr2(UPPERCASE)", "uppercase")]
	[Arguments("lcstr2(MiXeD CaSe)", "mixed case")]
	public async Task LCSTR2_ConvertsToLowercase(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ucstr2(Foo BAR baz)", "FOO BAR BAZ")]
	[Arguments("ucstr2(lowercase)", "LOWERCASE")]
	[Arguments("ucstr2(MiXeD CaSe)", "MIXED CASE")]
	public async Task UCSTR2_ConvertsToUppercase(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	#endregion

	#region SHA0 Tests

	[Test]
	[Arguments("sha0(test)")]
	public async Task SHA0_ReturnsNotSupported(string input)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NOT SUPPORTED");
	}

	#endregion

	#region CONVSECS/CONVTIME Tests

	[Test]
	public async Task CONVSECS_ConvertsSecondsToTimeString()
	{
		var result = (await Parser.FunctionParse(MModule.single("convsecs(0)")))?.Message!;
		// The exact output depends on the local timezone
		// Just verify it contains a year
		var resultText = result.ToPlainText();
		await Assert.That(resultText).Contains("1969").Or.Contains("1970");
	}

	[Test]
	public async Task CONVTIME_HandlesInvalidInput()
	{
		// Test with invalid input returns #-1
		var result = (await Parser.FunctionParse(MModule.single("convtime(invalid)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1");
	}

	#endregion

	#region CONFIG Tests

	[Test]
	
	[Arguments("config(money_singular)", "Penny")]
	[Arguments("config(money_plural)", "Pennies")]
	public async Task CONFIG_ReturnsConfigurationValues(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task CONFIG_NoArgs_ReturnsListOfOptions()
	{
		var result = (await Parser.FunctionParse(MModule.single("config()")))?.Message!;
		var resultText = result.ToPlainText();
		// Just verify it returns something non-empty
		await Assert.That(resultText).IsNotEmpty();
	}

	#endregion

	#region IDLESECS Tests

	[Test]
	public async Task IDLESECS_ReturnsIdleTimeOrNegativeOne()
	{
		// Test that it returns a numeric value for the executor
		// It may return -1 if not connected, which is valid
		var result = (await Parser.FunctionParse(MModule.single("idlesecs()")))?.Message!;
		var isNumeric = int.TryParse(result.ToPlainText(), out var idleTime);
		await Assert.That(isNumeric).IsTrue();
		// -1 is a valid return value for disconnected/dark wizards
		await Assert.That(idleTime).IsGreaterThanOrEqualTo(-1);
	}

	#endregion

	#region REGREPLACE Tests

	[Test]
	[Arguments("regreplace(hello world,world,universe)", "hello universe")]
	[Arguments("regreplace(test123,\\\\d+,456)", "test456")] // Double-escape for parser
	[Arguments("regreplace(HELLO,hello,hi,i)", "hi")] // Case insensitive with 'i' flag
	public async Task REGREPLACE_ReplacesPatternInString(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	#endregion

	#region WEBSOCKET Tests

	[Test]
	public async Task WEBSOCKET_HTML_ReturnsEmpty()
	{
		var result = (await Parser.FunctionParse(MModule.single("websocket_html(<b>test</b>)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task WEBSOCKET_JSON_ReturnsEmpty()
	{
		var result = (await Parser.FunctionParse(MModule.single("websocket_json({\"test\":\"value\"})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	#endregion
}
