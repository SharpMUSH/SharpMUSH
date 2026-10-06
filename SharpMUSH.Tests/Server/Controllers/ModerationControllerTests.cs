using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The moderation page's actions (#1565): a ban disables the account, keeps why, by whom and until when,
/// refuses every way of signing in, and lifts by hand or when it runs out; a warning fires the game's
/// <c>PLAYER`WARN</c> event; and each action is audited once, with its reason.
/// </summary>
/// <remarks>
/// Each test makes its own staff member and its own victim, each on an account of its own, and reads the
/// audit log by the victim's account id or objid, so tests running alongside never see each other's entries.
/// </remarks>
public class ModerationControllerTests : ServerTestBase
{
	private const string VictimPassword = TestIsolationHelpers.TestPassword;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	private sealed record Person(TestIsolationHelpers.TestPlayer Player, SharpAccount Account);

	/// <summary>A connected character on an account of its own; a wizard when <paramref name="wizard"/>.</summary>
	private async Task<Person> PersonAsync(string prefix, bool wizard = false)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, prefix);
		var account = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName(prefix + "Acct"), null,
			"account-pass-1")).Expect<SharpAccount>();
		await Accounts.LinkCharacterAsync(account.Id!, player.DbRef);
		if (wizard)
		{
			var node = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
			await Assert.That(await Mediator.Send(new SetObjectFlagCommand(node,
				(await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
		}

		return new Person(player, account);
	}

	private async Task<string> Objid(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<SharpPlayer>().Object.DBRef.ToString();

	/// <summary>A request signed in as <paramref name="staff"/>'s account, acting as its character.</summary>
	private async Task<ControllerContext> SignedInAs(Person staff) => new()
	{
		HttpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, staff.Account.Id!),
				new Claim(GameHub.CharacterDbrefClaim, await Objid(staff.Player.DbRef))
			], "Test"))
		}
	};

	private async Task<AdminBansController> BansAs(Person staff) => new(
		Accounts,
		WebAppFactoryArg.Services.GetRequiredService<IPermissionService>(),
		WebAppFactoryArg.Services.GetRequiredService<IVisibleWorldProjection>(),
		WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
		WebAppFactoryArg.Services.GetRequiredService<IAuditLog>())
	{
		ControllerContext = await SignedInAs(staff)
	};

	private async Task<AdminCharactersController> CharactersAs(Person staff, IEventService events) => new(
		Mediator,
		Accounts,
		ConnectionService,
		Substitute.For<Microsoft.AspNetCore.Authorization.IAuthorizationService>(),
		WebAppFactoryArg.Services.GetRequiredService<IVisibleWorldProjection>(),
		WebAppFactoryArg.Services.GetRequiredService<IEngineCommandInvoker>(),
		events,
		WebAppFactoryArg.Services.GetRequiredService<IAuditLog>())
	{
		ControllerContext = await SignedInAs(staff)
	};

	private static string Key(SharpAccount account) => account.Id!.Split('/')[^1];

	private async Task<IReadOnlyList<AuditEntry>> AuditAbout(string text)
		=> (await Mediator.Send(new GetAuditEntriesQuery(new AuditFilter(Text: text)))).Entries;

	/// <summary>Runs <c>connect</c> on a fresh connection and answers whether it logged in.</summary>
	private async Task<bool> ConnectsAsync(TestIsolationHelpers.TestPlayer player)
	{
		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService, MString.Plain($"connect {player.Name} {VictimPassword}"));
			return ConnectionService.Get(handle)?.Ref is not null;
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	private async Task CleanupAsync(params Person[] people)
	{
		foreach (var person in people)
			await ConnectionService.Disconnect(person.Player.Handle);
	}

	[Test]
	public async Task ABanDisablesTheAccountAndRefusesEverySignIn()
	{
		var staff = await PersonAsync("ModStaff", wizard: true);
		var victim = await PersonAsync("ModVictim");
		try
		{
			await Assert.That(await ConnectsAsync(victim.Player)).IsTrue().Because("the password works before the ban");
			// Whole milliseconds: the store keeps no finer.
			var until = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds());

			var result = await (await BansAs(staff)).Ban(new AdminBanRequest(Key(victim.Account), "  spamming the newbie channel  ", until),
				CancellationToken.None);

			await Assert.That(result).IsTypeOf<NoContentResult>();
			await Assert.That((await Accounts.GetByIdAsync(victim.Account.Id!))!.Status).IsEqualTo(AccountStatus.Disabled);
			var ban = await Accounts.GetBanAsync(victim.Account.Id!);
			await Assert.That(ban!.Reason).IsEqualTo("spamming the newbie channel");
			await Assert.That(ban.BannedBy).IsEqualTo(staff.Account.Id);
			await Assert.That(ban.ExpiresAt).IsEqualTo(until);

			await Assert.That(await ConnectsAsync(victim.Player)).IsFalse().Because("telnet connect refuses a banned account");
			var refused = (await Accounts.AuthenticateAsync(victim.Account.Username, "account-pass-1")).Expect<AccountUnavailable>();
			await Assert.That(refused.Message).IsEqualTo($"This account is banned until {until.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");

			var entry = (await AuditAbout(victim.Account.Id!)).Single(e => e.Action == AuditActions.BanAdd);
			await Assert.That(entry.Source).IsEqualTo(AuditSource.Portal);
			await Assert.That(entry.Actor.AccountId).IsEqualTo(staff.Account.Id);
			await Assert.That(entry.Details!).Contains("reason: spamming the newbie channel");
		}
		finally
		{
			await CleanupAsync(staff, victim);
		}
	}

	[Test]
	public async Task LiftingABanLetsTheAccountBackInAndIsAudited()
	{
		var staff = await PersonAsync("ModLiftStaff", wizard: true);
		var victim = await PersonAsync("ModLiftVictim");
		try
		{
			var bans = await BansAs(staff);
			await bans.Ban(new AdminBanRequest(Key(victim.Account), "cooling off", null), CancellationToken.None);

			await Assert.That(await bans.Lift(Key(victim.Account), CancellationToken.None)).IsTypeOf<NoContentResult>();

			(await Accounts.AuthenticateAsync(victim.Account.Username, "account-pass-1")).Expect<SharpAccount>();
			await Assert.That(await ConnectsAsync(victim.Player)).IsTrue();
			await Assert.That((await AuditAbout(victim.Account.Id!)).Count(e => e.Action == AuditActions.BanLift)).IsEqualTo(1);
			await Assert.That(await bans.Lift(Key(victim.Account), CancellationToken.None)).IsTypeOf<NotFoundObjectResult>();
		}
		finally
		{
			await CleanupAsync(staff, victim);
		}
	}

	[Test]
	public async Task ATimedBanLiftsOnceItRunsOut()
	{
		var victim = await PersonAsync("ModTimedVictim");
		try
		{
			var now = DateTimeOffset.UtcNow;
			(await Accounts.BanAsync(new AccountBan(victim.Account.Id!, "timed", null, now, now.AddHours(1))))
				.Expect<Success>();

			var early = new BanExpiryService(Accounts, WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
				NullLogger<BanExpiryService>.Instance, new FixedClock(now.AddMinutes(30)));
			await Assert.That((await early.SweepAsync(CancellationToken.None)).Select(b => b.AccountId))
				.DoesNotContain(victim.Account.Id!);
			await Assert.That(await Accounts.GetBanAsync(victim.Account.Id!)).IsNotNull();

			var late = new BanExpiryService(Accounts, WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
				NullLogger<BanExpiryService>.Instance, new FixedClock(now.AddHours(2)));
			await Assert.That((await late.SweepAsync(CancellationToken.None)).Select(b => b.AccountId))
				.Contains(victim.Account.Id!);

			await Assert.That((await Accounts.GetByIdAsync(victim.Account.Id!))!.Status).IsEqualTo(AccountStatus.Active);
			await Assert.That(await Accounts.GetBanAsync(victim.Account.Id!)).IsNull();
			var entry = (await AuditAbout(victim.Account.Id!)).Single(e => e.Action == AuditActions.BanExpired);
			await Assert.That(entry.Source).IsEqualTo(AuditSource.System);
		}
		finally
		{
			await CleanupAsync(victim);
		}
	}

	[Test]
	public async Task OnlyStaffWhoControlEveryCharacterMayBanOrUnbanTheAccount()
	{
		var mortal = await PersonAsync("ModMortalStaff");
		var wizard = await PersonAsync("ModWizardVictim", wizard: true);
		try
		{
			var result = await (await BansAs(mortal)).Ban(new AdminBanRequest(Key(wizard.Account), "no", null), CancellationToken.None);

			await Assert.That(result).IsTypeOf<ObjectResult>();
			await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
			await Assert.That(await Accounts.GetBanAsync(wizard.Account.Id!)).IsNull();
			await Assert.That((await Accounts.GetByIdAsync(wizard.Account.Id!))!.Status).IsEqualTo(AccountStatus.Active);

			// Nor may they undo a ban someone else put on it.
			(await Accounts.BanAsync(new AccountBan(wizard.Account.Id!, "by God", null, DateTimeOffset.UtcNow, null)))
				.Expect<Success>();
			var lift = await (await BansAs(mortal)).Lift(Key(wizard.Account), CancellationToken.None);
			await Assert.That(lift).IsTypeOf<ObjectResult>();
			await Assert.That(((ObjectResult)lift).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
			await Assert.That(await Accounts.GetBanAsync(wizard.Account.Id!)).IsNotNull();
			(await Accounts.LiftBanAsync(wizard.Account.Id!)).Expect<None>();

			var self = await (await BansAs(wizard)).Ban(new AdminBanRequest(Key(wizard.Account), "oops", null), CancellationToken.None);
			await Assert.That(self).IsTypeOf<ConflictObjectResult>();
			await Assert.That((await AuditAbout(wizard.Account.Id!)).Where(e => e.Action == AuditActions.BanAdd)).IsEmpty();
		}
		finally
		{
			await CleanupAsync(mortal, wizard);
		}
	}

	[Test]
	public async Task ABanNeedsAReasonAndAFutureEnd()
	{
		var staff = await PersonAsync("ModArgsStaff", wizard: true);
		var victim = await PersonAsync("ModArgsVictim");
		try
		{
			var bans = await BansAs(staff);
			await Assert.That(await bans.Ban(new AdminBanRequest(Key(victim.Account), " ", null), CancellationToken.None))
				.IsTypeOf<BadRequestObjectResult>();
			await Assert.That(await bans.Ban(new AdminBanRequest(Key(victim.Account), "late", DateTimeOffset.UtcNow.AddMinutes(-1)),
				CancellationToken.None)).IsTypeOf<BadRequestObjectResult>();
			await Assert.That(await Accounts.GetBanAsync(victim.Account.Id!)).IsNull();
		}
		finally
		{
			await CleanupAsync(staff, victim);
		}
	}

	[Test]
	public async Task AWarningFiresPlayerWarnAndIsAudited()
	{
		var staff = await PersonAsync("ModWarnStaff", wizard: true);
		var victim = await PersonAsync("ModWarnVictim");
		try
		{
			var events = Substitute.For<IEventService>();
			var victimObjid = await Objid(victim.Player.DbRef);

			var result = await (await CharactersAs(staff, events)).Warn(victim.Player.DbRef.Number, null,
				new AdminWarnRequest("  off-topic on +public "), CancellationToken.None);

			await Assert.That(result).IsTypeOf<NoContentResult>();
			await events.Received(1).TriggerEventAsync("PLAYER`WARN",
				Arg.Is<DBRef?>(enactor => enactor!.Value.Number == staff.Player.DbRef.Number),
				Arg.Is<string[]>(args => args.SequenceEqual(new[] { victimObjid, "off-topic on +public", staff.Account.Username })));
			var entry = (await AuditAbout(victimObjid)).Single(e => e.Action == AuditActions.PlayerWarn);
			await Assert.That(entry.Details).IsEqualTo("reason: off-topic on +public");
		}
		finally
		{
			await CleanupAsync(staff, victim);
		}
	}

	[Test]
	public async Task ABootKeepsItsReasonInTheAuditLog()
	{
		var staff = await PersonAsync("ModBootStaff", wizard: true);
		var victim = await PersonAsync("ModBootVictim");
		try
		{
			var result = await (await CharactersAs(staff, Substitute.For<IEventService>())).Boot(victim.Player.DbRef.Number, null,
				"idling in the OOC room", CancellationToken.None);

			await Assert.That(result).IsTypeOf<NoContentResult>();
			await Assert.That(ConnectionService.Get(victim.Player.Handle)?.Ref).IsNull();
			var entry = (await AuditAbout(await Objid(victim.Player.DbRef))).Single(e => e.Action == AuditActions.PlayerBoot);
			await Assert.That(entry.Source).IsEqualTo(AuditSource.Portal);
			await Assert.That(entry.Details!).EndsWith("; reason: idling in the OOC room");
		}
		finally
		{
			await CleanupAsync(staff, victim);
		}
	}

	[Test]
	public async Task LinkingACharacterAttachesItToTheNamedAccountAndIsAudited()
	{
		var staff = await PersonAsync("ModLinkStaff", wizard: true);
		var holder = await PersonAsync("ModLinkHolder");
		var character = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "ModLinkLoose");
		try
		{
			var result = await (await CharactersAs(staff, Substitute.For<IEventService>())).Link(character.DbRef.Number, null,
				new AdminLinkCharacterRequest(holder.Account.Username), CancellationToken.None);

			await Assert.That(result).IsTypeOf<NoContentResult>();
			await Assert.That((await Accounts.GetAccountForCharacterAsync(character.DbRef))?.Id).IsEqualTo(holder.Account.Id);
			var entry = (await AuditAbout(await Objid(character.DbRef))).Single(e => e.Action == AuditActions.CharacterLink);
			await Assert.That(entry.Source).IsEqualTo(AuditSource.Portal);
			await Assert.That(entry.Details).IsEqualTo(holder.Account.Username);
		}
		finally
		{
			await CleanupAsync(staff, holder);
			await ConnectionService.Disconnect(character.Handle);
		}
	}

	[Test]
	public async Task LinkingRefusesACharacterOnAnotherAccountAndAWizardForAModerator()
	{
		var staff = await PersonAsync("ModLinkMod");
		var wizard = await PersonAsync("ModLinkWiz", wizard: true);
		var other = await PersonAsync("ModLinkOther");
		try
		{
			var controller = await CharactersAs(staff, Substitute.For<IEventService>());

			var held = await controller.Link(other.Player.DbRef.Number, null,
				new AdminLinkCharacterRequest(staff.Account.Username), CancellationToken.None);
			var outranked = await controller.Link(wizard.Player.DbRef.Number, null,
				new AdminLinkCharacterRequest(staff.Account.Username), CancellationToken.None);

			await Assert.That(held).IsTypeOf<ConflictObjectResult>();
			await Assert.That((await Accounts.GetAccountForCharacterAsync(other.Player.DbRef))?.Id).IsEqualTo(other.Account.Id);
			await Assert.That(((ObjectResult)outranked).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
			await Assert.That((await Accounts.GetAccountForCharacterAsync(wizard.Player.DbRef))?.Id).IsEqualTo(wizard.Account.Id);
		}
		finally
		{
			await CleanupAsync(staff, wizard, other);
		}
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
