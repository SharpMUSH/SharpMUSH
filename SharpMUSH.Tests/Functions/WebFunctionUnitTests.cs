using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class WebFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// PennMUSH urlencode() uses libcurl's curl_easy_escape: RFC 3986 percent-encoding,
	// space -> %20 (not +), only A-Za-z0-9-._~ left unescaped, hex digits uppercased.
	// (\% in the input keeps a percent literal past the MUSH substitution layer.)
	[Test]
	[Arguments(@"urlencode(test string)", "test%20string")]
	[Arguments(@"urlencode(a/b c)", "a%2Fb%20c")]
	[Arguments(@"urlencode(a+b)", "a%2Bb")]
	[Arguments(@"urlencode(a&b=c)", "a%26b%3Dc")]
	[Arguments(@"urlencode(foo-_.~bar)", "foo-_.~bar")]
	[Arguments(@"urlencode(100\%)", "100%25")]
	public async Task Urlencode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PennMUSH urldecode() uses libcurl's curl_easy_unescape: only %XX is decoded; a literal
	// '+' is left untouched (unlike form decoding); non-printable decoded bytes become '?'.
	[Test]
	[Arguments(@"urldecode(test\%20string)", "test string")]
	[Arguments(@"urldecode(a\%2Fb\%20c)", "a/b c")]
	[Arguments(@"urldecode(test+string)", "test+string")]
	[Arguments(@"urldecode(a\%2Fb+c)", "a/b+c")]
	[Arguments(@"urldecode(100\%25)", "100%")]
	[Arguments(@"urldecode(a\%09b)", "a?b")]
	public async Task Urldecode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wshtml(<b>test</b>)", "")]
	public async Task Wshtml(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wsjson({\"test\":\"value\"})", "")]
	public async Task Wsjson(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("oob(test)", "")]
	public async Task Oob(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("oob(me, room.contents)", "0")]
	public async Task OobNoConnectionReturnsZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("pueblo()", "0")]
	public async Task Pueblo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ssl(%#)", "0")]
	public async Task Ssl(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("terminfo(%#)", "")]
	public async Task Terminfo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("width(%#)", "78")]
	public async Task Width(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
