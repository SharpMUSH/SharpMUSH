using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled roster package, and roster-jobs with it, installed from the catalogue and driven with typed
/// commands: characters added, given to accounts and returned, the roster kept right when an account is changed
/// some other way, and applications decided in the job queue. Each test removes what it installed. Not in
/// parallel: +roster is global, and the job numbers start again with each jobs install.
/// </summary>
[NotInParallel]
public class RosterIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected player in a room of its own, holding <paramref name="role"/> when one is named.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, string? role = null)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (role is not null) await God($"@role/assign #{player.DbRef.Number}={role}");
		return player;
	}

	/// <summary>A new account holding <paramref name="characters"/>.</summary>
	private async Task<SharpAccount> Account(string prefix, params TestIsolationHelpers.TestPlayer[] characters)
	{
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName(prefix), null,
			"Roster-Passw0rd!long")).Expect<SharpAccount>();
		foreach (var character in characters) await Accounts.LinkCharacterAsync(account.Id!, character.DbRef);
		await WebAppFactoryArg.QueueBarrierAsync();
		return account;
	}

	private string Roster { get; set; } = string.Empty;

	private async Task<string> State(TestIsolationHelpers.TestPlayer character)
		=> await God($"think u({Roster}/FUN`API`STATE,#{character.DbRef.Number})");

	private async Task<string> Holder(TestIsolationHelpers.TestPlayer character)
		=> (await Accounts.GetAccountForCharacterAsync(character.DbRef))?.Id ?? string.Empty;

	private async Task ApplyAsync(string package)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, package, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{package} must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the commands.
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task InstallRosterAsync()
	{
		await ApplyAsync("roster");
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("roster");
		Roster = objects.First(o => o.Ref == "roster").Objid;
	}

	private async Task UninstallAsync(params string[] packages)
	{
		foreach (var package in packages) await Installer.UninstallAsync(package, force: true, CancellationToken.None);
	}

	[Test]
	public async Task ACharacterIsAddedGivenToAnAccountAndReturned()
	{
		try
		{
			await InstallRosterAsync();
			var staff = await Player("RosStaff", "roster-admin");
			var player = await Player("RosPlayer");
			var character = await Player("RosChar");
			var account = await Account("rosacct", player);

			await Assert.That(await As(player, $"+roster/add {character.Name}")).Contains("Only roster staff can use +roster/add.");

			await Assert.That(await As(staff, $"+roster/add {character.Name}=Wardens"))
				.Contains($"{character.Name} is on the roster (Wardens), available.");
			await Assert.That(await State(character)).IsEqualTo("available");
			await Assert.That(await As(player, "+roster")).Contains(character.Name).And.Contains("Wardens");

			await As(staff, $"+roster/set {character.Name}/SUMMARY=A scout from the north.");
			await As(staff, $"+roster/set {character.Name}/NOTES=Staff eyes only.");
			await Assert.That(await As(player, $"+roster {character.Name}")).Contains("A scout from the north.")
				.And.DoesNotContain("Staff eyes only.").Because("notes are for roster staff");
			await Assert.That(await As(staff, $"+roster/set {character.Name}/STATE=x")).Contains("The roster keeps STATE itself.");

			var given = await As(staff, $"+roster/assign {character.Name}={player.Name}");
			await Assert.That(given).Contains($"{character.Name} is now held by account {account.Username}.");
			await Assert.That(await Holder(character)).IsEqualTo(account.Id!);
			await Assert.That(await State(character)).IsEqualTo("held");
			await Assert.That(string.Join("\n", Notifications.For(player.DbRef))).Contains($"{character.Name} is yours now.")
				.Because("the account's connected characters are told");

			await Assert.That(await As(staff, $"+roster/return {character.Name}=Gone quiet"))
				.Contains($"{character.Name} is back on the roster, taken from account {account.Username}.");
			await Assert.That(await Holder(character)).IsEqualTo(string.Empty);
			await Assert.That(await State(character)).IsEqualTo("available");

			var history = await As(staff, $"+roster/history {character.Name}");
			await Assert.That(history).Contains("returned").And.Contains("Gone quiet").And.Contains("assigned").And.Contains("added");

			await Assert.That(await As(staff, $"+roster/remove {character.Name}")).Contains($"{character.Name} is off the roster.");
			await Assert.That(await State(character)).IsEqualTo(string.Empty);
		}
		finally
		{
			await UninstallAsync("roster");
		}
	}

	[Test]
	public async Task AnAccountHoldsNoMoreThanItsLimitAndStaffAreRefused()
	{
		try
		{
			await InstallRosterAsync();
			var staff = await Player("RosLimStaff", "roster-admin");
			var player = await Player("RosLimPlayer");
			var first = await Player("RosLimA");
			var second = await Player("RosLimB");
			await Account("roslim", player);

			await As(staff, $"+roster/add {first.Name}");
			await As(staff, $"+roster/add {second.Name}");
			await As(staff, $"+roster/assign {first.Name}={player.Name}");
			await Assert.That(await As(staff, $"+roster/assign {second.Name}={player.Name}"))
				.Contains("already holds 1 roster character, the most one account may.");
			await Assert.That(await Holder(second)).IsEqualTo(string.Empty);

			await Assert.That(await As(staff, "+roster/add #1")).Contains("Staff characters never go on the roster.");
			await Assert.That(await As(staff, $"+roster/assign {second.Name}=nobody-{Guid.NewGuid():N}"))
				.Contains("There is no account called");
		}
		finally
		{
			await UninstallAsync("roster");
		}
	}

	[Test]
	public async Task IdleTakesAWholeNumberOfDays()
	{
		try
		{
			await InstallRosterAsync();
			var staff = await Player("RosIdleStaff", "roster-admin");

			await Assert.That(await As(staff, "+roster/idle 0")).Contains("+roster/idle <days>: a whole number of days.")
				.Because("0 days is no number of days, not the setting");
			await Assert.That(await As(staff, "+roster/idle x")).Contains("+roster/idle <days>: a whole number of days.");

			var character = await Player("RosIdleChar");
			await As(staff, $"+roster/add {character.Name}=Wardens");
			await Assert.That(await As(staff, "+roster/all/1")).Contains(character.Name).Because("a list's page is a last switch");
			await Assert.That(await As(staff, "+roster/all/2")).Contains("There is one page - you asked for page 2.");
			await Assert.That(await As(staff, $"+roster/2 {character.Name}")).Contains("+roster <name> takes no page number.");
			await Assert.That(await As(staff, $"+roster/history/1 {character.Name}")).Contains("added");

			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			await Assert.That(await As(staff, "+config roster/idle-days=0")).DoesNotContain("is now");
			await Assert.That(await As(staff, "+config roster/how-to-get=Page a roster admin.")).Contains("roster/how-to-get is now");
			await Assert.That(await As(staff, "+roster")).Contains("Page a roster admin.");
		}
		finally
		{
			await UninstallAsync("roster");
		}
	}

	[Test]
	public async Task LinkingOutsideTheRosterKeepsItRight()
	{
		try
		{
			await InstallRosterAsync();
			var staff = await Player("RosLnkStaff", "roster-admin");
			var character = await Player("RosLnkChar");
			await As(staff, $"+roster/add {character.Name}");

			var account = await Account("roslnk", character);
			await Assert.That(await State(character)).IsEqualTo("held").Because("the player`link event reaches the roster");

			await Accounts.UnlinkCharacterAsync(account.Id!, character.DbRef);
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(await State(character)).IsEqualTo("available");

			var history = await As(staff, $"+roster/history {character.Name}");
			await Assert.That(history).Contains("Linked outside the roster.").And.Contains("Unlinked outside the roster.");
		}
		finally
		{
			await UninstallAsync("roster");
		}
	}

	[Test]
	public async Task AnApplicationIsDecidedInTheJobQueue()
	{
		try
		{
			await InstallRosterAsync();
			await ApplyAsync("jobs");
			await ApplyAsync("roster-jobs");
			var staff = await Player("RosAppStaff", "roster-admin");
			var judge = await Player("RosAppJudge", "job-admin");
			var winner = await Player("RosAppWin");
			var loser = await Player("RosAppLose");
			var loner = await Player("RosAppLoner");
			var character = await Player("RosAppChar");
			var account = await Account("rosappw", winner);
			await Account("rosappl", loser);
			await As(staff, $"+roster/add {character.Name}");

			await Assert.That(await As(loner, $"+roster/apply {character.Name}=Please."))
				.Contains("Apply from a character on your account").Because("a character on no account cannot be given one");
			await Assert.That(await As(winner, $"+roster/apply {character.Name}=I would like to play them."))
				.Contains($"Your application for {character.Name} is job 1.");
			await Assert.That(await As(winner, $"+roster/apply {character.Name}=Again."))
				.Contains("already has an application open");
			await Assert.That(await As(loser, $"+roster/apply {character.Name}=Me too."))
				.Contains($"Your application for {character.Name} is job 2.");

			await As(judge, "+job/approve 1=Welcome.");
			await Assert.That(await Holder(character)).IsEqualTo(account.Id!);
			await Assert.That(await God("think [job(1,outcome)] [job(2,outcome)]")).IsEqualTo("approved denied")
				.Because("the other application closes when the character is given");
		}
		finally
		{
			await UninstallAsync("roster-jobs", "jobs", "roster");
		}
	}
}
