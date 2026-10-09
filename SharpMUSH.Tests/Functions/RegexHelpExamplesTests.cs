using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>Executable porting examples from the .NET regex help guides.</summary>
public class RegexHelpExamplesTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(@"regmatch(j,lit(\A[\p{L}\p{Nd}]\z))", "1")]
	[Arguments(@"regmatch(+,lit(\A[\p{L}\p{Nd}]\z))", "0")]
	[Arguments(@"regmatch(foo_bar,lit(\A\w+\z))", "1")]
	[Arguments(@"regmatch(foo bar,lit(\A\w+\z))", "0")]
	[Arguments(@"regmatch(café,lit(\A\p{L}+\z))", "1")]
	[Arguments(@"regmatch(café,lit(\A[A-Za-z]+\z))", "0")]
	[Arguments(@"regmatch(٣,lit(\A\d\z))", "1")]
	[Arguments(@"regmatch(٣,lit(\A[0-9]\z))", "0")]
	[Arguments(@"[regmatch(cookies=30,\\A%(.+%)=%(\[0-9\]+%)\\z,1:food 2:amount)]|%q<food>|%q<amount>", "1|cookies|30")]
	[Arguments(@"regmatch(sense and sensibility,\\A%(?<word>sens|respons%)e and \\k<word>ibility\\z)", "1")]
	[Arguments(@"regmatch(sense and responsibility,\\A%(?<word>sens|respons%)e and \\k<word>ibility\\z)", "0")]
	[Arguments(@"regmatch(aa,%(?P<word>a%))", "#-1 REGEXP ERROR: INVALID REGULAR EXPRESSION")]
	[Arguments(@"regmatch(123,lit(\A[[:digit:]]+\z))", "0")]
	[Arguments(@"regmatch(aa,lit(a*+))", "#-1 REGEXP ERROR: INVALID REGULAR EXPRESSION")]
	[Arguments(@"regmatch(aaa,lit(\Aa{0,3}\z))", "1")]
	[Arguments(@"regmatch(aaa,lit(\Aa{,3}\z))", "0")]
	[Arguments(@"regmatch(ABC,\\A%(?i:abc%)\\z)", "1")]
	[Arguments(@"regedit(this test is the best string,%(?<char>.%)est,$<char>rash)", "this trash is the best string")]
	[Arguments(@"regeditall(this test is the best string,%(.%)est,capstr($1)rash)", "this Trash is the Brash string")]
	[Arguments(@"regedit(abc,%(?<x>a%)%(b%),$2$1)", "bac")]
	[Arguments(@"regreplace(abc,%(?<x>a%)%(b%),lit($2$1))", "abc")]
	[Arguments(@"regreplace(abc,%(?<x>a%)%(b%),lit(${x}$1))", "abc")]
	[Arguments("regmatch(lit(abc\n),lit(^abc$))", "1")]
	[Arguments("regmatch(lit(abc\n),lit(\\Aabc\\z))", "0")]
	public async Task PublishedExampleMatchesTheEngineAndParser(string expression, string expected)
	{
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task ForeachMarkersAreSingleCharacters()
	{
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));
		await parser.FunctionParse(MarkupText.Plain("attrib_set(me/REGEX_HELP_UPPER,lit(ucstr(%0)))"));
		var result = await parser.FunctionParse(MarkupText.Plain("foreach(REGEX_HELP_UPPER,quiet quiet >shout< quiet,>,<)"));
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo("quiet quiet SHOUT quiet");
	}

}
