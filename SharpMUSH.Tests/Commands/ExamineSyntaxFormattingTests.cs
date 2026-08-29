using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Wires <c>SoftcodeFormatter.Format</c> (Task 5) into <c>@examine</c>'s attribute loop: an attribute
/// carrying <c>cmdsyntax</c>/<c>funsyntax</c> renders as a highlighted header followed by a formatted,
/// wrapped code block, while an unflagged attribute keeps today's exact single-line rendering.
/// Fixture pattern copied from <see cref="ExamineNullOwnerTests"/>.
/// <para>
/// <see cref="WebAppFactoryArg"/> is <c>SharedType.PerTestSession</c>, so <see cref="NotifyService"/> is
/// one substitute shared across every test in the process — <c>Received()</c> matches calls recorded by
/// any test that ran before it, in this class or any other. Every test below calls
/// <c>ClearReceivedCalls()</c> immediately before the <c>examine</c> invocation it's asserting on, and
/// asserts on a fragment the formatter alone produces for this exact input (a specific broken line with
/// its 2-space indent), not a substring that also appears in the raw, unformatted attribute value.
/// </para>
/// <para>
/// <c>[NotInParallel]</c>: with <see cref="NotifyService"/> shared across the whole session, a test
/// running concurrently with this class could record a <c>Notify</c> call between one test's
/// <c>ClearReceivedCalls()</c> and its own assertion, corrupting order-sensitive checks like
/// <see cref="FlaggedAttribute_WithEmptyValue_EmitsNoStrayBlankLine"/>'s "the call right after the
/// header" lookup. Matches the same guard already used by <c>CommunicationCommandTests</c> and
/// <c>UtilityCommandTests</c> for the identical reason.
/// </para>
/// </summary>
[NotInParallel]
public class ExamineSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private ValueTask Expect(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	// The formatted block is syntax-highlighted, so ANSI escape codes sit between tokens — a fragment
	// spanning a break (newline + indent + the next token) won't appear contiguously in
	// TestHelpers.MessageContains's ToString()-with-escapes comparison. Match on plain text instead.
	private ValueTask ExpectPlainText(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	[Test]
	public async ValueTask FlaggedAttribute_IsBrokenAcrossLines()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOn");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// The layout engine's first break for this exact input lands right after "switch(", putting
		// words() alone on an indented line — and, because a call that breaks expands everything nested
		// inside it, splitting words() over its own argument in turn. No other code path (raw or
		// otherwise) produces a newline immediately before "words(" — only SoftcodeLayout's break
		// insertion does.
		await ExpectPlainText("\n  words(\n    %0),");
	}

	[Test]
	public async ValueTask UnflaggedAttribute_RendersVerbatim()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOff");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		await Expect(LongCode);
	}

	[Test]
	public async ValueTask FlaggedAttribute_LosesNoCharacters()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtIntact");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// The formatter's last break for this input puts the closing argument on its own indented line.
		// Unlike a bare "many words indeed here)" substring (which the raw, unformatted single line would
		// also satisfy), the leading "\n  " ties this assertion to the reflowed block actually having run
		// — proving both that the tail character is intact *and* that the formatter produced it.
		await ExpectPlainText("\n  many words indeed here)");
	}

	[Test]
	public async ValueTask FlaggedAttribute_WithEmptyValue_EmitsNoStrayBlankLine()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtEmpty");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&EMPTYFN {obj}="));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/EMPTYFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/EMPTYFN"));

		// Every Notify call's plain text, in the order they were sent this command.
		var texts = NotifyService.ReceivedCalls()
			.Select(c => c.GetArguments())
			.Where(args => args.Length >= 2 && args[1] is OneOf<MString, string>)
			.Select(args => ((OneOf<MString, string>)args[1]!).Match(ms => ms.ToPlainText(), s => s))
			.ToList();

		var headerIndex = texts.FindIndex(t => t.StartsWith("EMPTYFN ["));

		// First confirm the header itself still fires — otherwise "no blank line" would be true for the
		// trivial (and wrong) reason that nothing at all was notified for this attribute.
		await Assert.That(headerIndex).IsNotEqualTo(-1);

		// The bug this guards against is a *second* Notify right after the header, for the empty
		// formatted block (which -- because an empty funsyntax body is itself a parse error -- is not
		// literally an empty string but a parser-failure summary; asserting "not empty" would have missed
		// that). @examine's structure after the attribute loop is fixed: the very next line is always
		// "Home:" (for a Thing/Player) or the room's exits/contents section, never anything derived from
		// the attribute just rendered. So the guard is intact exactly when nothing sits between the
		// header and that next structural line.
		await Assert.That(texts[headerIndex + 1]).StartsWith("Home:");
	}

	[Test]
	public async ValueTask FlaggedAttribute_WithZeroWidthConnection_FallsBackTo78()
	{
		// RFC 1073: a NAWS WIDTH of 0 means "unspecified" from the client, not "wrap at column zero" --
		// but it's client-controlled metadata that parses as a perfectly valid int. A player who reports
		// 0 (or a broken client that always does) must still get the 78-column fallback, not a
		// SoftcodeLayout.Compute clamp to width 1.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamFmtWidth0");
		ConnectionService.Update(testPlayer.Handle, "WIDTH", "0");

		// A fresh mortal can't @set or examine an object it doesn't own; God's WIZARD bit lets this
		// player create, flag and examine its own attribute in one identity, keeping the connection
		// (and its WIDTH=0 metadata) tied to the same executor throughout.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtWidth0Obj");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// Same break as the no-connection fallback case: proves WIDTH=0 was rejected and 78 was used,
		// not that width silently became 1 (which would break after nearly every character instead).
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(testPlayer.DbRef),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, "\n  words(\n    %0),")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}
}
