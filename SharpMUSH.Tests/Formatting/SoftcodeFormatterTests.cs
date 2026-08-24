using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Formatting;

/// <summary>
/// <see cref="SoftcodeFormatter"/> composes <see cref="SoftcodeLayout"/> (Task 2/3) and
/// <see cref="SemanticTokenRenderer"/> (Task 4); it owns no highlighting or layout logic of its own.
/// These tests exercise the composition — round-tripping, character preservation, break insertion and
/// the error summary — not the rules those two services already have their own test suites for.
/// <para>
/// A real <see cref="IMUSHCodeParser"/> is required (via <see cref="ServerWebAppFactory"/>) because
/// <see cref="SoftcodeFormatter.Format"/> builds its classifier from
/// <see cref="SoftcodeLayout.ClassifierFor"/>, which needs the real function library to tell an
/// evaluating call from a source-copying one — <c>SoftcodeLayoutEquivalenceTests</c> relies on the same
/// fixture for the same reason.
/// </para>
/// </summary>
public class SoftcodeFormatterTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private MString Format(string src, IReadOnlyList<SemanticToken>? sem = null,
		IReadOnlyList<ParseError>? errors = null, int width = 78)
		=> SoftcodeFormatter.Format(MModule.single(src), TestLexer.Lex(src),
			sem ?? [], errors ?? [], width, Parser);

	[Test]
	public async Task PlainText_RoundTripsUnchanged()
	{
		var result = Format("add(1,2)");
		await Assert.That(MModule.plainText(result)).IsEqualTo("add(1,2)");
	}

	[Test]
	public async Task LongInput_GainsNewlines()
	{
		var result = Format("switch(words(%0),0,nothing at all,1,just one,many here)", width: 30);
		await Assert.That(MModule.plainText(result)).Contains("\n");
	}

	[Test]
	public async Task NoCharactersAreLost_EvenWithoutSemanticTokens()
	{
		const string src = "switch(words(%0),0,nothing at all,1,just one,many here)";
		var result = MModule.plainText(Format(src, width: 30));

		static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
		await Assert.That(Strip(result)).IsEqualTo(Strip(src));
	}

	/// <summary>
	/// <see cref="ParseError.ToMushFailureString"/> reports "end of expression" rather than a numbered
	/// position whenever <see cref="ParseError.Column"/> sits at or past the end of the line in
	/// <see cref="ParseError.InputText"/> — including when <c>InputText</c> is left unset. This error's
	/// <c>InputText</c> is deliberately longer than "add(1,2" so column 7 lands mid-line and the summary
	/// exercises the numbered-position branch, which is the behaviour <c>@examine</c> callers see for a
	/// typical unterminated call reported against its enclosing attribute body.
	/// </summary>
	[Test]
	public async Task ErrorSummary_IsAppendedBeneathTheCode()
	{
		var errors = new[]
		{
			new ParseError
			{
				Line = 1, Column = 7, Message = "mismatched input",
				OffendingToken = ")", ExpectedTokens = ["COMMAWS", "CPAREN"],
				InputText = "add(1,2) trailing text so column 7 is not at the end of the line"
			}
		};

		var result = MModule.plainText(Format("add(1,2", errors: errors));

		await Assert.That(result).Contains("add(1,2");
		await Assert.That(result).Contains("position 7");
	}

	[Test]
	public async Task NoErrors_AppendsNoSummary()
	{
		var result = MModule.plainText(Format("add(1,2)"));
		await Assert.That(result.Split('\n')).Count().IsEqualTo(1);
	}

	[Test]
	public async Task EmptyInput_ReturnsEmpty()
	{
		var result = SoftcodeFormatter.Format(MModule.empty(), [], [], [], 78, Parser);
		await Assert.That(MModule.plainText(result)).IsEqualTo("");
	}
}
