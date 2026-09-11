using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Checks grep formatting and set-time syntax warnings through an isolated player.
/// </summary>
public class GrepSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private TestIsolationHelpers.TestPlayer _player = null!;

	[Before(Test)]
	public async Task CreatePlayer()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GrepSyntaxFormatting");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {_player.DbRef}=WIZARD"));
	}

	[After(Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);

	private int _notificationOffset;
	private IEnumerable<SharpMessage> Messages =>
		WebAppFactoryArg.Notifications.RawFor(_player.DbRef).Skip(_notificationOffset);

	private void BeginNotificationWindow() =>
		_notificationOffset = WebAppFactoryArg.Notifications.RawCountFor(_player.DbRef);

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private const string BrokenCode = "add(1,2";

	private async ValueTask ExpectPlainText(string fragment) =>
		await Assert.That(Messages.Any(m => TestHelpers.MessagePlainTextContains(m, fragment))).IsTrue();

	// Unlike ExpectPlainText, matches against ToString() (markup intact). A regression that dropped
	// semantic colouring while reconstructing before+match+after would leave the plain text identical
	// but would never emit this fragment -- ExpectPlainText alone cannot see that class of bug.
	private async ValueTask ExpectMarkup(string fragment) =>
		await Assert.That(Messages.Any(m => TestHelpers.MessageContains(m, fragment))).IsTrue();

	[Test]
	public async ValueTask SettingBrokenCodeIntoFlaggedAttribute_WarnsButStillStores()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "SetWarnOn");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&BAD {obj}=placeholder"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/BAD=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&BAD {obj}={BrokenCode}"));

		// ErrorMessages.Returns.ParserFailure is "#-1 PARSER FAILURE: {0}" -- ParseError.ToMushFailureString()
		// formats through it, so this fragment is the exact wording the advisory notify emits.
		await ExpectPlainText("PARSER FAILURE");

		// Start a new recipient-specific window so the warning cannot satisfy the read assertion.
		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"think [get({obj}/BAD)]"));
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
		var attribute = TestIsolationHelpers.GenerateUniqueName("DEFAULTFN");
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@attribute/access {attribute}=funsyntax"));

		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "SetWarnDefault");

		BeginNotificationWindow();
		// First-ever write to this unique attribute on this object -- the node does not exist before
		// this call.
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&{attribute} {obj}={BrokenCode}"));

		await ExpectPlainText("PARSER FAILURE");
	}

	[Test]
	public async ValueTask SettingBrokenCodeIntoUnflaggedAttribute_IsSilent()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "SetWarnOff");

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&BAD {obj}={BrokenCode}"));

		await Assert.That(Messages.Any(m => TestHelpers.MessagePlainTextContains(m, "PARSER FAILURE"))).IsFalse();
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_IsFormatted()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmt");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/print {obj}=words"));

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
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmtOff");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/print {obj}=words"));

		// Byte-identical regression contract: unflagged output stays a single unbroken line, so this
		// exact single-line fragment (which the formatted, wrapped block would never produce whole)
		// proves formatting did not run.
		await ExpectPlainText($"LONGFN: {LongCode}");
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedAttribute_PreservesSyntaxColoring()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmtColor");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/print {obj}=words"));

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
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmtWild");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/wild/print {obj}=*words*"));

		// The isWild branch assigns displayValue = formatted directly, skipping the highlight-slice
		// path entirely -- a separate code path from GrepPrintOnFlaggedAttribute_IsFormatted's literal
		// match, and one with no coverage before this test.
		await ExpectPlainText("\n  words(\n    %0),");
	}

	[Test]
	public async ValueTask GrepPrintOnFlaggedEmptyAttribute_EmitsNoStrayParserFailure()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmtEmpty");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&EMPTYFN {obj}="));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/EMPTYFN=funsyntax"));

		BeginNotificationWindow();
		// "*" as a WILD pattern matches the empty attribute value too, reaching the print loop
		// without ever going through the literal-match path (which an empty value can't match).
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/wild/print {obj}=*"));

		// The empty-value guard skips the formatter entirely for an empty attribute -- an empty
		// funsyntax body is itself a parse error and would otherwise surface a stray parser-failure
		// summary in place of blank, exactly the bug @examine's equivalent guard was added to prevent.
		await Assert.That(Messages.Any(m => TestHelpers.MessagePlainTextContains(m, "PARSER FAILURE"))).IsFalse();

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
		var obj = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "GrepFmtSummary");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&BADFN {obj}={ErrorWithExcerptCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/BADFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@grep/print {obj}={StraddlesABreak}"));

		var messages = Messages
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
