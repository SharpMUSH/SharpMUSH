using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Wires <c>SoftcodeFormatter.Format</c> (Task 5) into <c>@grep/PRINT</c>'s attribute loop -- an
/// attribute carrying <c>cmdsyntax</c>/<c>funsyntax</c> renders as a formatted, wrapped code block
/// instead of the raw value, with the existing plain-text match highlight re-sliced from the
/// formatted result -- and covers the companion advisory set-time validation added to
/// <see cref="SharpMUSH.Library.Services.AttributeService.SetAttributeAsync"/>: storing broken code
/// into a syntax-flagged attribute warns the setter but never blocks the set. Fixture pattern
/// copied from <see cref="ExamineSyntaxFormattingTests"/>.
/// <para>
/// <see cref="WebAppFactoryArg"/> is <c>SharedType.PerTestSession</c>, so <see cref="NotifyService"/> is
/// one substitute shared across every test in the process -- <c>Received()</c> and
/// <c>DidNotReceive()</c> match calls recorded by any test that ran before them, in this class or any
/// other. Every test below calls <c>ClearReceivedCalls()</c> immediately before the command under test
/// (after its setup commands), and asserts on a fragment tied to that test's specific input.
/// </para>
/// <para>
/// <c>[NotInParallel]</c>: with <see cref="NotifyService"/> shared across the whole session, a test
/// running concurrently with this class could record a <c>Notify</c> call between one test's
/// <c>ClearReceivedCalls()</c> and its own assertion. Matches the same guard already used by
/// <c>CommunicationCommandTests</c> and <c>UtilityCommandTests</c> for the identical reason.
/// </para>
/// </summary>
[NotInParallel]
public class GrepSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private const string BrokenCode = "add(1,2";

	private ValueTask ExpectPlainText(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	// Unlike ExpectPlainText, matches against ToString() (markup intact). A regression that dropped
	// semantic colouring while reconstructing before+match+after would leave the plain text identical
	// but would never emit this fragment -- ExpectPlainText alone cannot see that class of bug.
	private ValueTask ExpectMarkup(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	[Test]
	public async ValueTask SettingBrokenCodeIntoFlaggedAttribute_WarnsButStillStores()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetWarnOn");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}=placeholder"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/BAD=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}={BrokenCode}"));

		// ErrorMessages.Returns.ParserFailure is "#-1 PARSER FAILURE: {0}" -- ParseError.ToMushFailureString()
		// formats through it, so this fragment is the exact wording the advisory notify emits.
		await ExpectPlainText("PARSER FAILURE");

		// Advisory only -- the value must still be stored despite the warning above. Clear again
		// first: ExpectPlainText matches on Received(), which would otherwise still see the
		// "PARSER FAILURE" notify above in its match window. ParseError.ToMushFailureString() embeds
		// a source excerpt for errors that land mid-expression (see ErrorWithExcerptCode below), so
		// for some broken-code values that stale notify could itself contain the asserted fragment.
		// Without this second clear, the assertion below would pass on that stale notify even if
		// get() returned nothing at all -- confirmed by temporarily redirecting the read to a
		// never-written attribute and using a mid-expression BrokenCode value: the assertion passed
		// with only one ClearReceivedCalls() and correctly failed once this second one was restored.
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"think [get({obj}/BAD)]"));
		await ExpectPlainText(BrokenCode);
	}

	[Test]
	public async ValueTask FirstWriteToAttributeWithDefaultSyntaxFlag_WarnsOnBrokenCode()
	{
		// @attribute/access is the only way to reach the gap AttributeService.SetAttributeAsync's
		// pre-set `existing` snapshot can't see: a syntax flag configured as a DEFAULT for a fresh
		// attribute *name* (wizard-only, applied to every object's first-ever instance of that name),
		// rather than via @set on an instance that already exists. GetAttributeQuery is all-or-nothing,
		// so on the very first write `existing` comes back empty -- but SetAttributeCommand applies
		// this DefaultFlags entry (funsyntax) to the brand-new node during that same call, so only a
		// post-set re-fetch can see it in time to validate.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@attribute/access DEFAULTFN=funsyntax"));

		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetWarnDefault");

		NotifyService.ClearReceivedCalls();
		// First-ever write to DEFAULTFN on this object -- the attribute node does not exist before
		// this call.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&DEFAULTFN {obj}={BrokenCode}"));

		await ExpectPlainText("PARSER FAILURE");
	}

	[Test]
	public async ValueTask SettingBrokenCodeIntoUnflaggedAttribute_IsSilent()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SetWarnOff");

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BAD {obj}={BrokenCode}"));

		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, "PARSER FAILURE")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_IsFormatted()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmt");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/print {obj}=words"));

		// The layout engine's first break for this exact input lands right after "switch(", putting
		// words() alone on an indented line and expanding it over its own argument in turn -- the same
		// break @examine's equivalent test proves against the identical LongCode input. No other code
		// path (raw or otherwise) produces a newline immediately before "words(" for this attribute;
		// only SoftcodeLayout's break insertion does, so this fragment is unreachable unless
		// @grep/PRINT actually formatted it.
		await ExpectPlainText("\n  words(\n    %0),");
	}

	[Test]
	public async ValueTask GrepPrintOnUnflaggedAttribute_RendersVerbatim()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmtOff");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/print {obj}=words"));

		// Byte-identical regression contract: unflagged output stays a single unbroken line, so this
		// exact single-line fragment (which the formatted, wrapped block would never produce whole)
		// proves formatting did not run.
		await ExpectPlainText($"LONGFN: {LongCode}");
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_PreservesSyntaxColoring()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmtColor");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/print {obj}=words"));

		// "38;2;220;220;170" is the literal 24-bit ANSI foreground sequence
		// (ANSI.SGR(38, 2, 0xDC, 0xDC, 0xAA)) SemanticTokenAnsiPalette assigns SemanticTokenType.Function
		// -- the classification "switch(" gets. Slicing before/match/after by plain-text IndexOf (as the
		// grep highlight does) reconstructs identical *plain* text whether or not the underlying markup
		// carried semantic colouring, so ExpectPlainText alone would not notice this colour vanishing;
		// only a markup-aware assertion does.
		await ExpectMarkup("38;2;220;220;170");
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_WildcardMatch_IsFormatted()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmtWild");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/wild/print {obj}=*words*"));

		// The isWild branch assigns displayValue = formatted directly, skipping the highlight-slice
		// path entirely -- a separate code path from GrepPrintOnFlaggedAttribute_IsFormatted's literal
		// match, and one with no coverage before this test.
		await ExpectPlainText("\n  words(\n    %0),");
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedEmptyAttribute_EmitsNoStrayParserFailure()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmtEmpty");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&EMPTYFN {obj}="));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/EMPTYFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		// "*" as a WILD pattern matches the empty attribute value too, reaching the print loop
		// without ever going through the literal-match path (which an empty value can't match).
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/wild/print {obj}=*"));

		// The empty-value guard skips the formatter entirely for an empty attribute -- an empty
		// funsyntax body is itself a parse error and would otherwise surface a stray parser-failure
		// summary in place of blank, exactly the bug @examine's equivalent guard was added to prevent.
		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, "PARSER FAILURE")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());

		await ExpectPlainText("EMPTYFN: ");
	}

	// Its first parse error sits mid-string rather than at the end, so ToMushFailureString() includes a
	// (near "...") excerpt -- and that excerpt is of the RAW value, which the formatted code no longer
	// reproduces contiguously because a break lands after the "0,". Hence a substring that exists in the
	// stored value, does not exist in the laid-out code, and does exist in the appended summary.
	private const string ErrorWithExcerptCode =
		"switch([add(1),y],0,aaaaaaaaaa bbbbbbbbbb cccccccccc dddddddddd eeeeeeeeee ffffffffff)";

	private const string StraddlesABreak = "0,aaaaaaaaaa";

	/// <summary>
	/// The appended error summary is prose about the code, not the code. Highlighting a match found
	/// there would claim the attribute matched on text the attribute does not contain -- and the
	/// attribute was selected on its stored value, so the real match is always in the code half.
	/// </summary>
	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_NeverHighlightsInsideTheErrorSummary()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "GrepFmtSummary");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&BADFN {obj}={ErrorWithExcerptCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/BADFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@grep/print {obj}={StraddlesABreak}"));

		var messages = NotifyService.ReceivedCalls()
			.Select(c => c.GetArguments())
			.Where(args => args.Length >= 2 && args[1] is OneOf<MString, string>)
			.Select(args => (OneOf<MString, string>)args[1]!)
			.Select(m => (Plain: m.Match(ms => ms.ToPlainText(), s => s), Markup: m.Match(ms => ms.ToString(), s => s)))
			.ToList();

		var message = messages.FirstOrDefault(m => m.Plain.StartsWith("BADFN: ", StringComparison.Ordinal));
		await Assert.That(message.Plain).IsNotNull().Because("@grep/print emitted nothing for BADFN");

		var summaryStart = message.Plain.IndexOf("#-1 PARSER FAILURE", StringComparison.Ordinal);
		await Assert.That(summaryStart).IsNotEqualTo(-1).Because("this input must produce an error summary to search");

		var summary = message.Plain[summaryStart..];

		// The three conditions that make the defect reachable at all. If any stops holding -- the
		// excerpt narrows, the break moves -- this test would go quietly idle instead of failing.
		await Assert.That(summary).Contains(StraddlesABreak)
			.Because("the summary must contain the pattern, or there is nothing to mis-highlight");
		await Assert.That(message.Plain[..summaryStart]).DoesNotContain(StraddlesABreak)
			.Because("a break must have split the pattern in the code, or the code's own match wins anyway");

		// Plain text is identical either way -- highlighting only adds markup -- so the observable is
		// whether the summary survives into the rendered output uninterrupted. A Hilight run opened
		// inside it would split this substring with ANSI escapes.
		await Assert.That(message.Markup).Contains(summary)
			.Because("the error summary was rewritten by the match highlight");
	}
}
