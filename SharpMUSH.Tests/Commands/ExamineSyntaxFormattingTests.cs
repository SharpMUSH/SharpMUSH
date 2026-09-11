using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Checks attribute formatting through an isolated examiner.
/// </summary>
public class ExamineSyntaxFormattingTests
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
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExamineSyntaxFormatting");
		var player = (await Mediator.Send(new GetObjectNodeQuery(_player.DbRef))).AsPlayer;
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(player, wizard!))).IsTrue();
		var roomId = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("ExamineRoom"), player));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomId))).AsRoom;
		var origin = await player.Location.WithCancellation(CancellationToken.None);
		await Mediator.Send(new MoveObjectCommand(player, room, origin.Object().DBRef, IsSilent: true));
	}

	[After(Test)]
	public async Task DisconnectPlayer()
	{
		if (_player is not null)
			await ConnectionService.Disconnect(_player.Handle);
	}

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_player.DbRef, _player.Handle);

	private async Task<DBRef> CreateThingAsync(string prefix)
	{
		var result = await TestIsolationHelpers.CreateObjectCommandAsync(Parser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName(prefix), _player.Handle);
		return DBRef.Parse(result.Message!.ToPlainText());
	}

	private int _notificationOffset;
	private IEnumerable<OneOf<MString, string>> Messages =>
		WebAppFactoryArg.Notifications.RawFor(_player.DbRef).Skip(_notificationOffset);

	private void BeginNotificationWindow() =>
		_notificationOffset = WebAppFactoryArg.Notifications.RawCountFor(_player.DbRef);

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private async ValueTask Expect(string fragment) =>
		await Assert.That(Messages.Any(m => TestHelpers.MessageContains(m, fragment))).IsTrue();

	// The formatted block is syntax-highlighted, so ANSI escape codes sit between tokens — a fragment
	// spanning a break (newline + indent + the next token) won't appear contiguously in
	// TestHelpers.MessageContains's ToString()-with-escapes comparison. Match on plain text instead.
	private async ValueTask ExpectPlainText(string fragment) =>
		await Assert.That(Messages.Any(m => TestHelpers.MessagePlainTextContains(m, fragment))).IsTrue();

	[Test]
	public async ValueTask FlaggedAttribute_IsBrokenAcrossLines()
	{
		var obj = await CreateThingAsync("ExamFmtOn");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"examine {obj}/LONGFN"));

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
		var obj = await CreateThingAsync("ExamFmtOff");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"examine {obj}/LONGFN"));

		await Expect(LongCode);
	}

	[Test]
	public async ValueTask FlaggedAttribute_LosesNoCharacters()
	{
		var obj = await CreateThingAsync("ExamFmtIntact");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"examine {obj}/LONGFN"));

		// The formatter's last break for this input puts the closing argument on its own indented line.
		// Unlike a bare "many words indeed here)" substring (which the raw, unformatted single line would
		// also satisfy), the leading "\n  " ties this assertion to the reflowed block actually having run
		// — proving both that the tail character is intact *and* that the formatter produced it.
		await ExpectPlainText("\n  many words indeed here)");
	}

	[Test]
	public async ValueTask FlaggedAttribute_WithEmptyValue_EmitsNoStrayBlankLine()
	{
		var obj = await CreateThingAsync("ExamFmtEmpty");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&EMPTYFN {obj}="));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/EMPTYFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"examine {obj}/EMPTYFN"));

		// Every Notify call's plain text, in the order they were sent this command.
		var texts = Messages
			.Select(m => m.Match(ms => ms.ToPlainText(), s => s))
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
		ConnectionService.Update(_player.Handle, "WIDTH", "0");

		var obj = await CreateThingAsync("ExamFmtWidth0Obj");

		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"@set {obj}/LONGFN=funsyntax"));

		BeginNotificationWindow();
		await Parser.CommandParse(_player.Handle, ConnectionService, MarkupText.Plain($"examine {obj}/LONGFN"));

		// Same break as the missing-width fallback case: proves WIDTH=0 was rejected and 78 was used,
		// not that width silently became 1 (which would break after nearly every character instead).
		await ExpectPlainText("\n  words(\n    %0),");
	}
}
