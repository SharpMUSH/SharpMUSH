using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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
		var name = $"Pcr{Guid.NewGuid():N}"[..12];
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=passwordPcreate"));

		var created = result.Message.ToPlainText();
		var dbrefNumber = created.TrimStart('#').Split(':')[0];

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			NotifyService, nameof(ErrorMessages.Notifications.PlayerCreatedFormat),
			$"New player '{name}' (#{dbrefNumber}) created with password 'passwordPcreate'")).IsTrue();
	}

	/// <summary>
	/// PennMUSH src/wiz.c <c>do_newpassword</c> tells the wizard
	/// <c>Password for %s changed.</c> (the password itself only with /generate, where the wizard
	/// needs to learn it) and tells the player <c>Your password has been changed by %s.</c>
	/// SharpMUSH echoed the password it had just been given and told the player nothing.
	/// </summary>
	/// <remarks>
	/// Was skipped for "state pollution from other tests": it reset God's password. It now resets
	/// the password of a player it creates itself.
	/// </remarks>
	[Test]
	public async ValueTask NewpasswordCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var name = $"Npw{Guid.NewGuid():N}"[..12];
		var created = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=passwordNewpw")))
			.Message.ToPlainText();
		var objid = created.TrimStart('#').Split(':');
		var victim = new DBRef(int.Parse(objid[0]), long.Parse(objid[1]));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@newpassword {name}=changedNewpw"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			NotifyService, nameof(ErrorMessages.Notifications.NewPasswordSetFormat),
			$"Password for {name} changed.", executor)).IsTrue();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.NewPasswordChangedByFormat), victim)).IsTrue();
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask PasswordCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@password oldpassPassword=newpassPassword"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessageEquals(msg, "Password changed.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("TestInfrastructure")]
	[Skip("Test infrastructure issue - state pollution from other tests")]
	public async ValueTask PoorCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poor #1001"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "The quota system is disabled") ||
				TestHelpers.MessageEquals(msg, "I don't see that here.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
