using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
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
}
