using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class AdminCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>
	/// PennMUSH src/wiz.c <c>do_pcreate</c> ends with
	/// <c>notify_format(creator, T("New player '%s' (#%d) created with password '%s'"), ...)</c>.
	/// SharpMUSH created the player and told the wizard nothing at all — no confirmation, no dbref.
	/// </summary>
	/// <remarks>
	/// Was skipped for "state pollution from other tests": it asserted on a <c>Notify</c> that the
	/// command never made, and used a fixed player name that a second run in a shared session would
	/// collide with. The name is now unique per run and the assertion names the message key.
	/// </remarks>
	[Test]
	public async ValueTask PcreateCommand()
	{
		var name = $"PcreateTarget{Guid.NewGuid():N}"[..24];
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@pcreate {name}=passwordPcreate"));

		var created = result.Message!.ToPlainText();
		var dbrefNumber = created.TrimStart('#').Split(':')[0];

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			NotifyService, nameof(ErrorMessages.Notifications.PlayerCreatedFormat),
			$"New player '{name}' (#{dbrefNumber}) created with password 'passwordPcreate'")).IsTrue();
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask NewpasswordCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@newpassword #1=newpassNewpassword"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Set new password for God: newpassNewpassword")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask PasswordCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@password oldpassPassword=newpassPassword"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "Password changed.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask ShutdownCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "SHUTDOWN initiated.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RestartCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@restart"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "All objects restarted.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PurgeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Purge complete.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask PoorCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poor #1001"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "The quota system is disabled") ||
				TestHelpers.MessageEquals(msg, "I don't see that here.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask ReadcacheCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "Reindexing text files")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask ChownallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1002=#2002"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "I don't see that here.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask ChzoneallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chzoneall #1003=#2003"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "I don't see that here.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
