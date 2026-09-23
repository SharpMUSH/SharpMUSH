using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH declares the folder switch as FOLDERS (<c>src/command.c:206-207</c>) and has no FOLDER.
/// SharpMUSH declared only the singular, and switch validation is exact-match rather than prefix
/// (<c>SharpMUSHParserVisitor</c>), so <c>@mail/folders</c> was rejected as an invalid switch before
/// dispatch — even though the folder code behind it worked. Both names are now declared; the
/// singular is retained as an intentional alias.
/// </summary>
public class MailFolderSwitchTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Drives real folder state: a message is sent, the folder is renamed under the switch being
	/// tested, and the report that follows names the folder it was moved to.
	/// </summary>
	[Test]
	[Arguments("folder")]
	[Arguments("folders")]
	public async Task EitherSpelling_RenamesAFolderAndReportsIt(string switchName)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"MailFolder{switchName}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail #{player.DbRef.Number}=Folder Subject/Folder body."));

		// 1 is the sent message; file it into a folder of its own, then rename that folder.
		var moved = $"MOVED{switchName.ToUpperInvariant()}";
		var renamed = $"RENAMED{switchName.ToUpperInvariant()}";

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail/file 1={moved}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail/{switchName} {moved}={renamed}"));

		// The recorder hands back a snapshot, so it is read after the commands, not before.
		var told = WebAppFactoryArg.Notifications.For(player.DbRef);

		await Assert.That(told).DoesNotContain(m => m.Contains("INVALID SWITCH", StringComparison.OrdinalIgnoreCase));
		await Assert.That(told).Contains(m => m.Contains($"{moved} folder renamed to {renamed}", StringComparison.Ordinal));
	}

	/// <summary>
	/// The no-argument report — <c>@mail/folders</c> with nothing after it — is the form the issue
	/// was filed against, and the one PennMUSH's helpfile leads with.
	/// </summary>
	[Test]
	[Arguments("folder")]
	[Arguments("folders")]
	public async Task EitherSpelling_ReportsTheCurrentFolder(string switchName)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"MailReport{switchName}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var result = await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail/{switchName}"));
		var told = WebAppFactoryArg.Notifications.For(player.DbRef);

		await Assert.That(result.Message!.ToPlainText())
			.DoesNotContain("INVALID SWITCH", StringComparison.OrdinalIgnoreCase);
		await Assert.That(told).Contains(m => m.Contains("MAIL: Current folder is INBOX.", StringComparison.Ordinal));
	}

	/// <summary>
	/// Switching the active folder has to change what the next <c>@mail</c> reads. It did not: the active
	/// folder is kept in per-object expanded data, and <c>GetExpandedDataAsync&lt;T&gt;</c> cast a
	/// <c>JsonElement</c> to its type and so always answered null, leaving
	/// <c>MessageListHelper.CurrentMailFolder</c> fixed at INBOX and rewriting the stored folder on every
	/// read (#1227). The switch reported success the whole time.
	/// </summary>
	[Test]
	public async Task SwitchingTheActiveFolderChangesWhatMailReads()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailActiveFolder");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		async Task Run(string command) => await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		await Run($"@mail #{player.DbRef.Number}=Kept In Saved/Filed away.");
		await Run($"@mail #{player.DbRef.Number}=Stays In Inbox/Still here.");
		await Run("@mail/file 1=SAVED");
		await Run("@mail/folder SAVED");

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Run("@mail/folder");
		await Run("@mail");
		var told = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(told).Contains(m => m.Contains("MAIL: Current folder is SAVED.", StringComparison.Ordinal));
		await Assert.That(told).Contains(m => m.Contains("MAIL (folder SAVED)", StringComparison.Ordinal));
		await Assert.That(told).Contains(m => m.Contains("Kept In Saved", StringComparison.Ordinal));
		await Assert.That(told).DoesNotContain(m => m.Contains("Stays In Inbox", StringComparison.Ordinal));
	}

	/// <summary>
	/// The same state read by a second command in a second session: a forward takes its message from the
	/// active folder (<c>do_mail_fwd</c> folders the msglist, <c>extmail.c:1243</c>), so switching folders
	/// decides which message is forwarded.
	/// </summary>
	[Test]
	public async Task ForwardingTakesItsMessageFromTheActiveFolder()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailFwdFolderFrom");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailFwdFolderTo");
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);

		async Task Run(string command) => await parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain(command));

		await Run($"@mail #{sender.DbRef.Number}=Archived One/Archived body.");
		await Run($"@mail #{sender.DbRef.Number}=Inbox One/Inbox body.");
		await Run("@mail/file 1=ARCHIVE");
		await Run("@mail/folder ARCHIVE");
		await Run($"@mail/fwd 1=#{target.DbRef.Number}");

		var forwarded = await Mediator.CreateStream(new GetMailListQuery(
			(await Mediator.Send(new GetObjectNodeQuery(target.DbRef))).Expect<SharpPlayer>(), "INBOX")).ToArrayAsync();

		await Assert.That(forwarded).Count().IsEqualTo(1);
		await Assert.That(forwarded[0].Subject.ToPlainText()).IsEqualTo("Fwd: Archived One");
	}
}
