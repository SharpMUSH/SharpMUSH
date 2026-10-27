using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled onboarding package, which a new game installs at first boot with its +help and +config bridges:
/// +welcome lists the steps, ticks off the ones nothing checks, shows at login while a step is open, and staff add
/// steps through +config. Not in parallel: the login setting and the step table are game-wide.
/// </summary>
[NotInParallel]
public class OnboardingIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private ValueTask SettleCommandsAsync() => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
		.DrainImmediateQueueForTests(TimeSpan.FromSeconds(60));
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> God(string command)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
		return result.Message.ToPlainText()?.Trim() ?? string.Empty;
	}

	private Task<string> Think(string expression) => God($"think {expression}");

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		await SettleCommandsAsync();
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await SettleCommandsAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>
	/// What <paramref name="player"/>'s connection was sent when the event handler's player`connect ran for it, as
	/// the engine runs it at login.
	/// </summary>
	private async Task<string> LogsIn(TestIsolationHelpers.TestPlayer player)
	{
		await SettleCommandsAsync();
		var before = Notifications.CountForHandle(player.Handle);
		await God($"@trigger/args #9/PLAYER`CONNECT`ONBOARDING=objid,#{player.DbRef.Number},connections,1,descriptor,{player.Handle}");
		return string.Join("\n", Notifications.ForHandle(player.Handle).Skip(before));
	}

	/// <summary>A connected player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	/// <summary>The Onboarding object, which first boot installed.</summary>
	private async Task<string> GuideAsync()
	{
		var objects = await WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync("onboarding");
		return objects.First(o => o.Ref == "guide").Objid;
	}

	[Test]
	public async Task TheListShowsTheStepsAndTicksOffTheOnesNothingChecks()
	{
		var guide = await GuideAsync();
		var ann = await Player("ObAnn");
		var number = ann.DbRef.Number;

		var list = await As(ann, "+welcome");
		await Assert.That(list).Contains("Getting started")
			.And.Contains("Learn the basics")
			.And.Contains("+welcome/done 1")
			.And.Contains("Read this game's help")
			.And.Contains("Describe your character")
			.And.Contains("Get approved")
			.And.Contains("+welcome/hide stops it showing at login.");

		await Assert.That(await As(ann, "+welcome/done BASICS")).Contains("[WELCOME] Done: Learn the basics.");
		await Assert.That(await As(ann, "+welcome/done basics")).Contains("You already ticked off Learn the basics.");
		await Assert.That(await Think($"hasattr(#{number},ONBOARDING`DONE`BASICS)")).IsEqualTo("1");

		// A step with a check ticks itself off; the player cannot.
		await Assert.That(await As(ann, "+welcome/done describe")).Contains("Describe your character ticks itself off once it is done.");
		await Assert.That(await Think($"u({guide}/FUN`STEP`DONE,DESCRIBE,#{number})")).IsEqualTo("0");
		await As(ann, "@desc me=Tall and quiet.");
		await Assert.That(await Think($"u({guide}/FUN`STEP`DONE,DESCRIBE,#{number})")).IsEqualTo("1");

		// Typed text is a step's name or nothing: never a pattern, never part of another attribute.
		await Assert.That(await As(ann, "+welcome/done nosuch")).Contains("There is no step called 'nosuch' on your list.");
		await Assert.That(await As(ann, "+welcome/done *")).Contains("There is no step called '*' on your list.");
		await Assert.That(await As(ann, "+welcome/done basics`hint")).Contains("There is no step called 'basics`hint' on your list.");
		await Assert.That(await As(ann, "+welcome/done")).Contains("Which step? +welcome/done <number or name>.");
		await Assert.That(await As(ann, "+welcome/done 99")).Contains("There is no step 99 on your list.");
		await Assert.That(await As(ann, "+welcome/done 0")).Contains("There is no step 0 on your list.");

		// A step by its number on the list, as well as by name.
		await Assert.That(await As(ann, "+welcome/undo 1")).Contains("Learn the basics is open again.");
		await Assert.That(await As(ann, "+welcome/undo basics")).Contains("You have not ticked off Learn the basics.");
		await Assert.That(await As(ann, "+welcome/bogus")).Contains("+welcome has no /bogus switch.");
		await Assert.That(await As(ann, "+welcome/bare")).Contains("+welcome has no /bare switch.");
		await Assert.That(await As(ann, "+welcome extra")).Contains("There is no step called 'extra' on your list.");
	}

	[Test]
	public async Task AStepByNumberShowsItsDetail()
	{
		var cal = await Player("ObCal");

		var basics = await As(cal, "+welcome 1");
		await Assert.That(basics).Contains("1. Learn the basics")
			.And.Contains("help gs moving for")
			.And.Contains("Done? +welcome/done 1");
		await Assert.That(await As(cal, "+welcome DESCRIBE")).Contains("3. Describe your character")
			.And.Contains("This ticks itself off once it is done");

		await As(cal, "+welcome/done 1");
		await Assert.That(await As(cal, "+welcome 1")).Contains("Ticked off: +welcome/undo 1 opens it again");
		await Assert.That(await As(cal, "+welcome 99")).Contains("There is no step 99 on your list.");
	}

	[Test]
	public async Task ItShowsAtLoginWhileAStepIsOpen()
	{
		var guide = await GuideAsync();
		var bo = await Player("ObBo");

		await Assert.That(await LogsIn(bo)).Contains("Getting started")
			.And.Contains("Type +welcome to see this list again");
		await Assert.That(await As(bo, "+welcome")).DoesNotContain("Type +welcome to see this list again");

		await Assert.That(await As(bo, "+welcome/hide")).Contains("Your checklist no longer shows at login");
		await Assert.That(await LogsIn(bo)).DoesNotContain("Getting started");
		await Assert.That(await As(bo, "+welcome")).Contains("Hidden at login: +welcome/show brings it back.");
		await Assert.That(await As(bo, "+welcome/show")).Contains("Your checklist shows again while a step is open.");
		await Assert.That(await LogsIn(bo)).Contains("Getting started");

		try
		{
			// first: once, at the first login only.
			await God($"&DATA`CONFIG`LOGIN {guide}=first");
			await Assert.That(await LogsIn(bo)).DoesNotContain("Getting started");
			var cy = await Player("ObCy");
			await Assert.That(await LogsIn(cy)).Contains("Getting started");
			await Assert.That(await LogsIn(cy)).DoesNotContain("Getting started");

			await God($"&DATA`CONFIG`LOGIN {guide}=off");
			await Assert.That(await LogsIn(await Player("ObDi"))).DoesNotContain("Getting started");
		}
		finally
		{
			await God($"&DATA`CONFIG`LOGIN {guide}=open");
		}
	}

	[Test]
	public async Task ItNeverShowsToGodAtLogin()
	{
		var guide = await GuideAsync();
		await Assert.That(await Think($"u({guide}/FUN`STEP`OPEN,#1)")).IsNotEmpty();
		await Assert.That(await Think($"u({guide}/FUN`LOGIN`SHOWS,#1)")).IsEqualTo("0");
	}

	[Test]
	public async Task AGuestStartsFreshOnceItsLastConnectionCloses()
	{
		var guest = await Player("ObGuest");
		var player = await Player("ObStays");
		await God($"@power #{guest.DbRef.Number}=Guest");

		foreach (var who in new[] { guest, player })
		{
			await As(who, "+welcome/done basics");
			await As(who, "+welcome/hide");
		}

		// Another connection is still open: nothing changes.
		await God($"@trigger/args #9/PLAYER`DISCONNECT`ONBOARDING=objid,#{guest.DbRef.Number},connections,1");
		await Assert.That(await Think($"hasattr(#{guest.DbRef.Number},ONBOARDING`DONE`BASICS)")).IsEqualTo("1");

		foreach (var who in new[] { guest, player })
		{
			await God($"@trigger/args #9/PLAYER`DISCONNECT`ONBOARDING=objid,#{who.DbRef.Number},connections,0");
		}

		await Assert.That(await Think($"lattr(#{guest.DbRef.Number}/ONBOARDING**)")).IsEmpty();
		await Assert.That(await Think($"lattr(#{player.DbRef.Number}/ONBOARDING**)"))
			.Contains("ONBOARDING`DONE`BASICS").And.Contains("ONBOARDING`HIDE");
	}

	[Test]
	public async Task StaffAddAStepWithConfig()
	{
		var staff = await Player("ObStaff");
		await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
		var ed = await Player("ObEd");

		try
		{
			await Assert.That(await As(staff, "+config/add onboarding/steps/zrules=Read the house rules")).Contains("onboarding/steps/zrules");
			await As(staff, "+config onboarding/steps/zrules/hint=The Policies page on the wiki.");
			await As(staff, "+config onboarding/steps/zrules/order=5");
			await As(staff, "+config onboarding/steps/zrules/detail=Six short rules. The one that matters most: ask before you hurt another character.");

			var list = await As(ed, "+welcome");
			await Assert.That(list).Contains("Read the house rules").And.Contains("The Policies page on the wiki.");
			await Assert.That(list.IndexOf("Read the house rules", StringComparison.Ordinal))
				.IsLessThan(list.IndexOf("Learn the basics", StringComparison.Ordinal));
			await Assert.That(await As(ed, "+welcome 1")).Contains("1. Read the house rules")
				.And.Contains("ask before you hurt another");
			await Assert.That(await As(ed, "+welcome/done zrules")).Contains("Done: Read the house rules.");
		}
		finally
		{
			await As(staff, "+config/remove onboarding/steps/zrules");
		}

		await Assert.That(await As(ed, "+welcome")).DoesNotContain("Read the house rules");
		await Assert.That(await As(ed, "+help welcome")).Contains("+welcome/done <step>");
		// The staff topic is locked to config.admin, which the settings it documents need.
		await Assert.That(await As(ed, "+help welcome staff")).DoesNotContain("+config/add onboarding/steps/rules=Read the house rules");
		await Assert.That(await As(staff, "+help welcome staff")).Contains("+config/add onboarding/steps/rules=Read the house rules");
	}
}
