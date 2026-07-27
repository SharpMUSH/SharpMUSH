using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// PennMUSH capitalizes the first character of a substitution's output when the selector letter is
/// uppercase — <c>%Q0</c> vs <c>%q0</c>, <c>%I0</c> vs <c>%i0</c>, and so on — and emits a literal
/// <c>% </c> (percent then space) rather than stripping the percent. Every expectation here was
/// observed on a live PennMUSH server. Uses the FunctionParse-and-assert pattern (not the older
/// NotifyService.Notify calls, which assert nothing).
/// </summary>
public class SubstitutionCapitalizationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string expression) =>
		(await Parser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	[Test]
	[Arguments("[setq(0,foo)]%q0", "foo")]
	[Arguments("[setq(0,foo)]%Q0", "Foo")]
	[Arguments("[setq(0,42x)]%Q0", "42x")]
	[Arguments("[setq(test,foo)]%q<test>", "foo")]
	[Arguments("[setq(test,foo)]%Q<test>", "Foo")]
	[Arguments("iter(alpha,%i0)", "alpha")]
	[Arguments("iter(alpha,%I0)", "Alpha")]
	[Arguments("iter(alpha,%iL)", "alpha")]
	[Arguments("iter(alpha,%IL)", "Alpha")]
	public async Task UppercaseSelectorCapitalizesFirstChar(string input, string expected)
	{
		await Assert.That(await Eval(input)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("a% b", "a% b")]
	public async Task PercentSpaceIsLiteral(string input, string expected)
	{
		await Assert.That(await Eval(input)).IsEqualTo(expected);
	}
}
