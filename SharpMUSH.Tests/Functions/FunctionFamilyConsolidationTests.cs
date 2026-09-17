using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Function pairs PennMUSH implements once and SharpMUSH had written out twice, pinned at the
/// points where the copies had already drifted.
/// </summary>
public class FunctionFamilyConsolidationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	/// <summary>
	/// <c>SETQ</c> and <c>SETR</c> are both <c>fun_setq</c>, which rejects an odd argument count
	/// for either name (<c>src/funmisc.c:327-332</c>). Only <c>setr</c> carried
	/// <c>EvenArgsOnly</c>, so an odd-argument <c>setq</c> reached the pairing loop and read past
	/// the last argument.
	/// </summary>
	[Test]
	[Arguments("setq")]
	[Arguments("setr")]
	public async Task RegisterSettersRejectAnOddArgumentCount(string function)
		=> await Assert.That(await Eval($"{function}(a,1,b)"))
			.IsEqualTo($"#-1 FUNCTION ({function.ToUpperInvariant()}) EXPECTS AN EVEN NUMBER OF ARGUMENTS");

	/// <summary>
	/// The pair differs in exactly one thing: <c>setr</c> echoes the first value back
	/// (<c>src/funmisc.c:348</c>). Everything else — how many pairs are stored, what a bad register
	/// name returns — is one body.
	/// </summary>
	[Test]
	public async Task RegisterSettersDifferOnlyInWhatTheyReturn()
	{
		await Assert.That(await Eval("[setq(fc0,alpha,fc1,beta)][r(fc0)]/[r(fc1)]")).IsEqualTo("alpha/beta");
		await Assert.That(await Eval("[setr(fc2,alpha,fc3,beta)]|[r(fc2)]/[r(fc3)]")).IsEqualTo("alpha|alpha/beta");
	}

	/// <summary>
	/// <c>grab</c>, <c>graball</c>, <c>match</c> and <c>matchall</c> are one scan over a split list
	/// with a wildcard pattern; they differ only in emitting the element or its one-based position,
	/// and the first hit or every hit. A miss is an empty element for <c>grab</c> and a zero
	/// position for <c>match</c>.
	/// </summary>
	[Test]
	[Arguments("grab(alpha beta gamma,b*)", "beta")]
	[Arguments("graball(alpha beta bravo,b*)", "beta bravo")]
	[Arguments("match(alpha beta gamma,b*)", "2")]
	[Arguments("matchall(alpha beta bravo,b*)", "2 3")]
	[Arguments("grab(alpha beta,z*)", "")]
	[Arguments("graball(alpha beta,z*)", "")]
	[Arguments("match(alpha beta,z*)", "0")]
	[Arguments("matchall(alpha beta,z*)", "")]
	public async Task WildcardScanFamilyKeepsItsFourShapes(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>The <c>-all</c> forms take a separate output delimiter in their fourth argument.</summary>
	[Test]
	[Arguments("graball(a-alpha|b-beta|b-bravo,b*,|,+)", "b-beta+b-bravo")]
	[Arguments("matchall(a-alpha|b-beta|b-bravo,b*,|,+)", "2+3")]
	public async Task WildcardScanAllFormsHonourTheirOutputDelimiter(string call, string expected)
		=> await Assert.That(await Eval(call)).IsEqualTo(expected);

	/// <summary>The input delimiter is the output delimiter when no fourth argument is given.</summary>
	[Test]
	public async Task WildcardScanAllFormsDefaultTheOutputDelimiterToTheInputOne()
		=> await Assert.That(await Eval("graball(a-alpha|b-beta|b-bravo,b*,|)")).IsEqualTo("b-beta|b-bravo");
}
