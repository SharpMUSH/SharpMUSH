using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests for newly implemented PennMUSH functions
/// </summary>
public class NewPennMUSHFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	#region DELETE (alias of strdelete) / INSERT (alias of linsert)

	/// <summary>
	/// PennMUSH's hardcoded alias table maps DELETE to STRDELETE (<c>src/function.c:335</c>), not to
	/// LDELETE: zero-based deletion of <em>characters</em>. This asserted list semantics, so
	/// <c>delete(abcdef,1,2)</c> answered the empty string where PennMUSH answers <c>adef</c>.
	/// LDELETE keeps the list meaning and is tested below.
	/// </summary>
	[Test]
	[Arguments("delete(abcdef,1,2)", "adef")]
	[Arguments("delete(abcdefgh,3,2)", "abcfgh")]
	[Arguments("strdelete(abcdef,1,2)", "adef")]
	public async Task DELETE_IsAnAliasOfStrdelete(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>fun_delete</c> (<c>src/funstr.c:345</c>), against the live 1.8.8 oracle. A bare
	/// <c>string.Remove</c> throws on every one of these. Note the negative length: the shipped
	/// PennMUSH help claims it deletes backwards, but the code leaves <c>num</c> negative and
	/// <c>ansi_string_delete</c> (<c>src/markup.c:2301</c>) returns early on <c>count &lt; 1</c>, so
	/// the string comes back untouched. The oracle agrees with the code, so SharpMUSH follows it.
	/// </summary>
	[Test]
	[Arguments("strdelete(abcdef,x,2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("strdelete(abcdef,1,y)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("strdelete(abcdef,-1,2)", "#-1 OUT OF RANGE")]
	[Arguments("strdelete(abcdef,10,2)", "abcdef")]
	[Arguments("strdelete(abcdef,6,1)", "abcdef")]
	[Arguments("strdelete(abcdef,2,0)", "abcdef")]
	[Arguments("strdelete(abcdefgh,3,-2)", "abcdefgh")]
	[Arguments("strdelete(abcdef,2,-99)", "abcdef")]
	[Arguments("strdelete(abcdef,4,99)", "abcd")]
	public async Task StrdeleteOutOfRange(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("insert(This is a string,4,test)", "This is a test string")]
	[Arguments("insert(one|three|four,2,two,|)", "one|two|three|four")]
	[Arguments("insert(meep bleep gleep,-3,GOOP)", "meep GOOP bleep gleep")]
	public async Task INSERT_AddsItemToList(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("linsert(This is a string,4,test)", "This is a test string")]
	[Arguments("linsert(one|three|four,2,two,|)", "one|two|three|four")]
	[Arguments("linsert(meep bleep gleep,-3,GOOP)", "meep GOOP bleep gleep")]
	public async Task LINSERT_InsertsItemAtPosition(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ucstr2(Foo BAR baz)", "FOO BAR BAZ")]
	[Arguments("ucstr2(lowercase)", "LOWERCASE")]
	[Arguments("ucstr2(MiXeD CaSe)", "MIXED CASE")]
	public async Task UCSTR2_ConvertsToUppercase(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	#endregion

	#region SHA0 Tests

	[Test]
	[Arguments("sha0(test)")]
	public async Task SHA0_ReturnsNotSupported(string input)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NOT SUPPORTED");
	}

	#endregion

	#region CONVSECS/CONVTIME Tests

	[Test]
	public async Task CONVSECS_ConvertsSecondsToTimeString()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("convsecs(0)")))?.Message!;
		// The exact output depends on the local timezone
		var resultText = result.ToPlainText();
		await Assert.That(resultText).Contains("1969").Or.Contains("1970");
	}

	[Test]
	public async Task CONVTIME_HandlesInvalidInput()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("convtime(invalid)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidTime);
	}

	#endregion

	#region CONFIG Tests

	[Test]
	[Category("NeedsSetup")]
	[Skip("Config values are dynamic and environment-specific")]
	[Arguments("config(money_singular)", "Penny")]
	[Arguments("config(money_plural)", "Pennies")]
	public async Task CONFIG_ReturnsConfigurationValues(string input, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task CONFIG_NoArgs_ReturnsListOfOptions()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("config()")))?.Message!;
		var resultText = result.ToPlainText();
		await Assert.That(resultText).IsNotEmpty();
	}

	#endregion

	#region IDLESECS Tests

	[Test]
	public async Task IDLESECS_ReturnsIdleTimeOrNegativeOne()
	{
		// It may return -1 if not connected, which is valid
		var result = (await Parser.FunctionParse(MarkupText.Plain("idlesecs()")))?.Message!;
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
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	#endregion

	#region WEBSOCKET Tests

	[Test]
	public async Task WEBSOCKET_HTML_ReturnsEmpty()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("websocket_html(<b>test</b>)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task WEBSOCKET_JSON_ReturnsEmpty()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("websocket_json({\"test\":\"value\"})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	[Arguments("websocket_html(<b>test</b>,NoSuchPlayerForWebsocket)")]
	[Arguments("websocket_json({\"test\":\"value\"},NoSuchPlayerForWebsocket)")]
	public async Task WEBSOCKET_UnknownPlayer_ReturnsNoMatch(string input)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(input)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("#-1 NO MATCH");
	}

	#endregion
}
