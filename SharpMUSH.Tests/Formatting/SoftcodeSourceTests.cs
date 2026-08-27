using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// <see cref="SoftcodeSource"/> decides where an attribute's match data ends and its code begins, and
/// validates only the latter. A real <see cref="IMUSHCodeParser"/> is required (via
/// <see cref="ServerWebAppFactory"/>) because the claim under test is about what the production
/// grammar reports, not about a stand-in.
/// </summary>
public class SoftcodeSourceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>
	/// The pattern half ends at the first colon the command regex accepts — which is not the first
	/// colon in the text, because <c>\:</c> is an escaped one. A naive <c>IndexOf(':')</c> would cut
	/// this value at index 6 rather than 12 and leave half the pattern exposed to the layout engine.
	/// </summary>
	[Test]
	public async Task MatchPatternPrefixLength_HonoursTheEscapedColon()
	{
		const string source = @"$time\:now:@pemit %#=ok";

		await Assert.That(SoftcodeSource.MatchPatternPrefixLength(source, ParseType.CommandList))
			.IsEqualTo(source.IndexOf(":@pemit", StringComparison.Ordinal) + 1);
	}

	/// <summary>
	/// Mirrors <c>CommandAttributeScanner</c>'s <c>commandBodyStart</c>: the spaces between the <c>:</c>
	/// and the command are skipped there, so they are part of the inert half here too.
	/// </summary>
	[Test]
	public async Task MatchPatternPrefixLength_RunsPastTheSpacesAfterTheColon()
		=> await Assert.That(SoftcodeSource.MatchPatternPrefixLength("$foo:   @pemit %#=ok", ParseType.CommandList))
			.IsEqualTo("$foo:   ".Length);

	/// <summary>
	/// The gate: a <c>funsyntax</c> attribute is evaluated whole, so nothing in it is match data even
	/// when it happens to start with a <c>$</c>.
	/// </summary>
	[Test]
	public async Task MatchPatternPrefixLength_IsZeroOutsideTheCommandListDialect()
		=> await Assert.That(SoftcodeSource.MatchPatternPrefixLength("$foo:@pemit %#=ok", ParseType.Function))
			.IsEqualTo(0);

	[Test]
	public async Task MatchPatternPrefixLength_IsZeroWithoutAPattern()
		=> await Assert.That(SoftcodeSource.MatchPatternPrefixLength("@pemit %#=ok", ParseType.CommandList))
			.IsEqualTo(0);

	/// <summary>
	/// The set-time symptom of the same defect. <c>$give [a,b} to *:</c> is an ordinary wildcard
	/// pattern — a bracket and a brace are just characters to match — but read as softcode the <c>[</c>
	/// opens a bracket group that the <c>}</c> does not close, so parsing runs off the end of the value.
	/// Validating the whole thing therefore emits a <c>#-1 PARSER FAILURE</c> for an attribute that
	/// works.
	/// </summary>
	[Test]
	public async Task Validate_DoesNotParseTheCommandPattern()
	{
		const string source = "$give [a,b} to *:@pemit %#=ok";

		await Assert.That(Parser.ValidateAndGetErrors(MModule.single(source), ParseType.CommandList))
			.IsNotEmpty().Because("if the whole value parsed cleanly there would be no spurious warning to suppress");

		await Assert.That(SoftcodeSource.Validate(Parser, MModule.single(source), ParseType.CommandList))
			.IsEmpty().Because("only the text from the command-list index onward is ever parsed");
	}

	/// <summary>
	/// A genuine error in the code half must still be reported — and reported where the player can see
	/// it, in the whole value's coordinates rather than the slice's.
	/// </summary>
	[Test]
	public async Task Validate_ReportsCodeErrorsAtTheirPositionInTheWholeValue()
	{
		// The pattern is deliberately long and deliberately valid softcode in its own right: long, so
		// that the slice's own columns all fall inside it and an un-shifted result is unmistakable;
		// valid, so that whole-value validation finds the same single error and its columns are the
		// right answer to compare against.
		const string source = "$a really long command pattern goes here:@pemit %#=[add(1,2";
		var prefixLength = SoftcodeSource.MatchPatternPrefixLength(source, ParseType.CommandList);

		var sliceErrors = Parser.ValidateAndGetErrors(
			MModule.single(source[prefixLength..]), ParseType.CommandList);
		var wholeErrors = Parser.ValidateAndGetErrors(MModule.single(source), ParseType.CommandList);
		var errors = SoftcodeSource.Validate(Parser, MModule.single(source), ParseType.CommandList);

		await Assert.That(sliceErrors).IsNotEmpty().Because("the code half must actually be broken here");
		await Assert.That(sliceErrors.All(e => e.Column < prefixLength)).IsTrue()
			.Because("this test only bites while the slice's own columns fall inside the prefix");

		await Assert.That(errors.Select(e => e.Column)).IsEquivalentTo(wholeErrors.Select(e => e.Column))
			.Because("a valid pattern half means the shifted columns must land exactly where validating "
							 + "the whole value puts them");
		await Assert.That(errors.All(e => e.Column >= prefixLength)).IsTrue()
			.Because($"columns were not shifted past the {prefixLength}-character pattern: "
							 + string.Join(", ", errors.Select(e => e.Column)));
	}
}
