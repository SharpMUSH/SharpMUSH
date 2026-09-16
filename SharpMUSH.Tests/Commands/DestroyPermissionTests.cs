using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Who may schedule an object for destruction — PennMUSH <c>what_to_destroy()</c>
/// (<c>src/destroy.c:236</c>). Control is one of three routes; an exit may also be destroyed by
/// whoever controls either end of it, and a DESTROY_OK thing by anyone passing its
/// <c>@lock/destroy</c>. SAFE is decided by <c>really_safe</c>, and destroying something you do not
/// own, or a WIZARD thing, needs <c>@nuke</c>.
/// <para>
/// Driven through mortal handles wherever permission is the subject: God controls everything, so a
/// God-only test of these gates proves nothing. Every refusal also asserts the target was left
/// unmarked. Not parallel with the other destruction classes, whose <c>@purge</c> frees any GOING
/// object in the shared world.
/// </para>
/// </summary>
[NotInParallel]
public class DestroyPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private ValueTask<CallState> AsGod(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private ValueTask<CallState> As(TestIsolationHelpers.TestPlayer who, string command) =>
		Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private Task<TestIsolationHelpers.TestPlayer> MortalAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private static DBRef Parse(CallState result) => DBRef.Parse(result.Message!.ToPlainText().Trim());

	private async Task<DBRef> CreateAsync(string prefix, TestIsolationHelpers.TestPlayer? owner = null)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		return Parse(owner is null ? await AsGod($"@create {name}") : await As(owner, $"@create {name}"));
	}

	private async Task<DBRef> DigAsync(string prefix)
		=> Parse(await AsGod($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<bool> IsGoingAsync(DBRef target)
		=> await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>().HasFlag("GOING");

	private async Task<int> OwnerOfAsync(DBRef target)
		=> (await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>()
			.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;

	private bool WasTold(DBRef who, string message)
		=> WebAppFactoryArg.Notifications.For(who).Any(m => m.Contains(message, StringComparison.Ordinal));

	private static IDisposable ReallySafe(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Command = options.Command with { ReallySafe = on }
		});

	[Test]
	public async Task Mortal_CannotDestroyAThingTheyDoNotControl()
	{
		var owner = await MortalAsync("DPT_Owner");
		var stranger = await MortalAsync("DPT_Stranger");
		var thing = await CreateAsync("DPT_NotYours", owner);

		await As(stranger, $"@nuke {thing}");

		await Assert.That(WasTold(stranger.DbRef, ErrorMessages.Notifications.PermissionDenied)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();
		await Assert.That(await OwnerOfAsync(thing)).IsEqualTo(owner.DbRef.Number);
	}

	/// <summary>
	/// GOING is wizard-only to set by hand, so marking it with the actor's own permissions left a
	/// mortal's @destroy announcing a schedule it never made, and their @undestroy sparing nothing.
	/// </summary>
	[Test]
	public async Task Mortal_SchedulesAndSparesTheirOwnThing()
	{
		var owner = await MortalAsync("DPT_RoundTrip");
		var thing = await CreateAsync("DPT_RoundTripThing", owner);

		await As(owner, $"@destroy {thing}");
		await Assert.That(await IsGoingAsync(thing)).IsTrue();

		await As(owner, $"@undestroy {thing}");
		await Assert.That(await IsGoingAsync(thing)).IsFalse();
	}

	[Test]
	public async Task DestroyOk_LetsAnyonePassingTheDestroyLockDestroyIt_WithoutNuke()
	{
		var owner = await MortalAsync("DPT_DestOkOwner");
		var stranger = await MortalAsync("DPT_DestOkStranger");
		var thing = await CreateAsync("DPT_DestOk", owner);
		await AsGod($"@set {thing}=DESTROY_OK");

		await As(stranger, $"@destroy {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue()
			.Because("DESTROY_OK with an unset destroy lock admits everyone, and exempts the @nuke-for-others rule");
	}

	[Test]
	public async Task DestroyOk_RefusesWhoeverFailsTheDestroyLock()
	{
		var owner = await MortalAsync("DPT_LockOwner");
		var stranger = await MortalAsync("DPT_LockStranger");
		var thing = await CreateAsync("DPT_Locked", owner);
		await AsGod($"@set {thing}=DESTROY_OK");
		await AsGod($"@lock/destroy {thing}={owner.DbRef}");

		await As(stranger, $"@nuke {thing}");

		await Assert.That(WasTold(stranger.DbRef, ErrorMessages.Notifications.PermissionDenied)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();
	}

	[Test]
	public async Task ExitDestination_ControllerMayNukeTheExit_ButNotPlainDestroyIt()
	{
		var builder = await MortalAsync("DPT_DestBuilder");
		var theirs = await DigAsync("DPT_TheirRoom");
		var godsRoom = await DigAsync("DPT_GodRoom");
		await AsGod($"@chown {theirs}={builder.DbRef}");
		var exit = Parse(await AsGod($"@open {TestIsolationHelpers.GenerateUniqueName("DPT_Inbound")}={theirs},{godsRoom}"));

		await As(builder, $"@destroy {exit}");

		await Assert.That(WasTold(builder.DbRef, ErrorMessages.Notifications.NotYoursUseNuke)).IsTrue();
		await Assert.That(await IsGoingAsync(exit)).IsFalse();

		await As(builder, $"@nuke {exit}");

		await Assert.That(await IsGoingAsync(exit)).IsTrue()
			.Because("controlling an exit's destination is a route to destroying it");
		await Assert.That(await OwnerOfAsync(exit)).IsEqualTo(1);
	}

	[Test]
	public async Task ExitSource_ControllerMayNukeTheExit()
	{
		var builder = await MortalAsync("DPT_SrcBuilder");
		var theirs = await DigAsync("DPT_TheirSource");
		var godsRoom = await DigAsync("DPT_GodTarget");
		await AsGod($"@chown {theirs}={builder.DbRef}");
		var exit = Parse(await AsGod($"@open {TestIsolationHelpers.GenerateUniqueName("DPT_Outbound")}={godsRoom},{theirs}"));

		await As(builder, $"@nuke {exit}");

		await Assert.That(await IsGoingAsync(exit)).IsTrue()
			.Because("controlling an exit's source is a route to destroying it");
	}

	[Test]
	public async Task ReallySafe_RefusesEvenNukeOnASafeObject()
	{
		using var _ = ReallySafe(true);
		var owner = await MortalAsync("DPT_RSOwner");
		var thing = await CreateAsync("DPT_RSSafe", owner);
		await AsGod($"@set {thing}=SAFE");

		await As(owner, $"@nuke {thing}");

		await Assert.That(WasTold(owner.DbRef, ErrorMessages.Notifications.SafeObjectMustUnset)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();
	}

	[Test]
	public async Task NotReallySafe_SafeObjectNeedsNuke_AndNukeWarns()
	{
		using var _ = ReallySafe(false);
		var owner = await MortalAsync("DPT_NRSOwner");
		var thing = await CreateAsync("DPT_NRSSafe", owner);
		await AsGod($"@set {thing}=SAFE");

		await As(owner, $"@destroy {thing}");

		await Assert.That(WasTold(owner.DbRef, ErrorMessages.Notifications.SafeObjectUseNuke)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();

		await As(owner, $"@nuke {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue();
		await Assert.That(WasTold(owner.DbRef, ErrorMessages.Notifications.SafeTargetScheduledAnyway)).IsTrue();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SafeAndDestroyOk_IsDestroyedByPlainDestroy(bool reallySafe)
	{
		using var _ = ReallySafe(reallySafe);
		var owner = await MortalAsync("DPT_SafeDestOkOwner");
		var thing = await CreateAsync("DPT_SafeDestOk", owner);
		await AsGod($"@set {thing}=SAFE");
		await AsGod($"@set {thing}=DESTROY_OK");

		await As(owner, $"@destroy {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue()
			.Because("DESTROY_OK overrides SAFE whatever really_safe says");
	}

	[Test]
	public async Task SomeoneElsesThing_NeedsNukeEvenForAWizard()
	{
		var owner = await MortalAsync("DPT_CrossOwner");
		var thing = await CreateAsync("DPT_CrossThing", owner);

		await AsGod($"@destroy {thing}");

		await Assert.That(WasTold(WebAppFactoryArg.ExecutorDBRef, ErrorMessages.Notifications.NotYoursUseNuke)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();

		await AsGod($"@nuke {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue();
		await Assert.That(await OwnerOfAsync(thing)).IsEqualTo(owner.DbRef.Number);
	}

	[Test]
	public async Task WizardThing_NeedsNuke()
	{
		var thing = await CreateAsync("DPT_WizThing");
		await AsGod($"@set {thing}=WIZARD");

		await AsGod($"@destroy {thing}");

		await Assert.That(WasTold(WebAppFactoryArg.ExecutorDBRef, ErrorMessages.Notifications.WizardThingUseNuke)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsFalse();

		await AsGod($"@nuke {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsTrue();
	}

	[Test]
	public async Task NonPlayerExecutor_CannotDestroyAPlayer_EvenAsAWizard()
	{
		var victim = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "DPT_Victim");
		var possession = await CreateAsync("DPT_VictimThing");
		await AsGod($"@chown {possession}={victim}");
		var program = await CreateAsync("DPT_Program");
		await AsGod($"@set {program}=WIZARD");

		await AsGod($"@force {program}=@nuke {victim}");

		await WebAppFactoryArg.Notifications.WaitForAsync(program, ErrorMessages.Notifications.ProgramsDontKillPeople);
		await Assert.That(await IsGoingAsync(victim)).IsFalse();
		await Assert.That(await IsGoingAsync(possession)).IsFalse();
		await Assert.That(await OwnerOfAsync(possession)).IsEqualTo(victim.Number);
	}
}
