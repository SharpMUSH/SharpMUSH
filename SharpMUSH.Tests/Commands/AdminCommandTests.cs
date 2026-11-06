using Mediator;
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
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

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

	/// <summary>
	/// PennMUSH src/player.c <c>do_password</c>: a wrong old password is refused with
	/// <c>The old password that you entered was incorrect.</c> and changes nothing; the right one is
	/// answered <c>You have changed your password.</c>
	/// </summary>
	/// <remarks>
	/// Was skipped for "state pollution from other tests": it changed God's password and read God's
	/// notifications. It now changes the password of a player it creates and reads that player's.
	/// </remarks>
	[Test]
	public async ValueTask PasswordCommand()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Pw");

		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@password wrongPassword=newpassPassword"));
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef))
			.Contains("The old password that you entered was incorrect.");
		await Assert.That(await Evaluate($"checkpass({player.DbRef},{TestIsolationHelpers.TestPassword})")).IsEqualTo("1");

		await Parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@password {TestIsolationHelpers.TestPassword}=newpassPassword"));
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef)).Contains("You have changed your password.");
		await Assert.That(await Evaluate($"checkpass({player.DbRef},newpassPassword)")).IsEqualTo("1");
	}

	/// <summary>
	/// <c>@poor &lt;player&gt;</c> sets that player's quota to 0 and tells both sides.
	/// </summary>
	/// <remarks>
	/// Was skipped for "state pollution from other tests": it named a fixed #1001, which another test
	/// may or may not have created, and accepted either of two answers. It now names a player it
	/// creates.
	/// </remarks>
	[Test]
	public async ValueTask PoorCommand()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "Poor");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@poor {player.DbRef}"));

		await Assert.That(WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef))
			.Contains($"{player.Name} has been set to poor status (quota: 0).");
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef))
			.Contains($"Your building quota has been set to 0 by {await Evaluate("name(#1)")}.");
		await Assert.That(await Evaluate($"quota({player.DbRef})")).IsEqualTo("0");
	}

	private async Task<string> Evaluate(string expression)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
}
