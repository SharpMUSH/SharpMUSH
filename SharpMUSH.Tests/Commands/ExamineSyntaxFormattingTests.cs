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
/// </summary>
public class ExamineSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
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
		// "words(%0)," alone on an indented line. No other code path (raw or otherwise) produces a
		// newline immediately before "words(" — only SoftcodeLayout's break insertion does.
		await ExpectPlainText("\n  words(%0),");
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

		var attributeNotifications = NotifyService.ReceivedCalls()
			.Select(c => c.GetArguments())
			.Where(args => args.Length >= 2 && args[1] is OneOf<MString, string> msg
				&& TestHelpers.MessageContains(msg, "EMPTYFN ["))
			.ToList();

		// One line for the header, none for an empty formatted block — matching the unflagged path,
		// which renders a bare attribute with no value as a single line too.
		await Assert.That(attributeNotifications.Count).IsEqualTo(1);
	}
}
