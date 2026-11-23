using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A player with no <c>TZ</c> of its own is in its account's: <c>time()</c>, <c>timefmt()</c>, <c>convsecs()</c>,
/// <c>convtime()</c> and <c>dateparse()</c> read <c>#acct:&lt;key&gt;/TZ</c> after the character's. Every zone here is
/// Pacific/Kiritimati (UTC+14 since 1995, no summer time), so its hour differs from the server's in every zone a test host runs in.
/// </summary>
public class AccountTimeZoneTests : ServerTestBase
{
	private const string Zone = "Pacific/Kiritimati";
	private const string Instant = "1800000000";
	private const string InstantInZone = "Fri Jan 15 22:00:00 2027";

	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private string _holder = null!;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task LinkTwoCharactersToAnAccountWithAZone()
	{
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "TzFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "TzSecond");
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "TzStranger");
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "TzWiz");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var username = TestIsolationHelpers.GenerateUniqueName("tz").ToLowerInvariant();
		var account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { _first, _second })
			await Accounts.AttachCharacterAsync(account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
		_holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
		await AsWizard($"&TZ {_holder}={Zone}");
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _first, _second, _stranger, _wizard })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private Task<string> AsWizard(string command) => CmdAs(_wizard.DbRef, _wizard.Handle, command);

	[Test]
	public async Task EveryCharacterOnTheAccountIsInTheAccountsZone()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"convsecs({Instant},{Ref(_first)})")).IsEqualTo(InstantInZone);
		await Assert.That(await EvalAs(_stranger.DbRef, $"convsecs({Instant},*{_second.Name})")).IsEqualTo(InstantInZone);
	}

	[Test]
	public async Task TheCharactersOwnZoneComesFirst()
	{
		await CmdAs(_first.DbRef, _first.Handle, "&TZ me=UTC");

		await Assert.That(await EvalAs(_stranger.DbRef, $"convsecs({Instant},{Ref(_first)})")).IsEqualTo("Fri Jan 15 08:00:00 2027");
		await Assert.That(await EvalAs(_stranger.DbRef, $"convsecs({Instant},{Ref(_second)})")).IsEqualTo(InstantInZone);
	}

	[Test]
	public async Task TheAccountNamesAZoneByItself()
		=> await Assert.That(await EvalAs(_first.DbRef, $"convsecs({Instant},{_holder})")).IsEqualTo(InstantInZone);

	[Test]
	public async Task TimefmtConvtimeAndTimeReadTheAccountsZone()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"timefmt($H,{Instant},{Ref(_first)})")).IsEqualTo("22");
		await Assert.That(await EvalAs(_stranger.DbRef, $"convtime({InstantInZone},{Ref(_first)})")).IsEqualTo(Instant);
		// time() reads the wall clock, so read it back as seconds in the zone and check it lies between two clock samples.
		await Assert.That(await EvalAs(_stranger.DbRef, $"[setq(0,secs())][setq(1,convtime(time({Ref(_first)}),{Zone}))][and(gte(%q1,%q0),lte(%q1,secs()))]")).IsEqualTo("1");
	}

	[Test]
	public async Task AWindowsZoneNameIsStillAZone()
		=> await Assert.That(await EvalAs(_stranger.DbRef, $"convsecs({Instant},Eastern Standard Time)")).IsEqualTo("Fri Jan 15 03:00:00 2027");

	[Test]
	public async Task DateparseReadsTheExecutorsAccountWhenGivenNoZone()
		=> await Assert.That(await EvalAs(_first.DbRef, "dateparse(tomorrow 7pm,,1792152000,normalized)"))
			.EndsWith($"({Zone})");

	[Test]
	public async Task TimezoneNamesTheZoneInUse()
	{
		await CmdAs(_second.DbRef, _second.Handle, "&TZ me=UTC");

		await Assert.That(await EvalAs(_stranger.DbRef, $"timezone({Ref(_first)})")).IsEqualTo(Zone);
		await Assert.That(await EvalAs(_stranger.DbRef, $"timezone({Ref(_second)})")).IsEqualTo("UTC");
		await Assert.That(await EvalAs(_stranger.DbRef, $"timezone({Ref(_stranger)})")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_first.DbRef, $"timezone({_holder})")).IsEqualTo(Zone);
	}

	/// <summary>
	/// A zone hidden from the reader is not there for it: the server's zone, as a hidden character <c>TZ</c> gives.
	/// The account's own characters and staff still read it.
	/// </summary>
	[Test]
	public async Task AHiddenAccountZoneIsTheServersToOthers()
	{
		await AsWizard($"@set {_holder}/TZ=!visual");

		await Assert.That(await EvalAs(_stranger.DbRef, $"strmatch(convsecs({Instant},{Ref(_first)}),convsecs({Instant}))")).IsEqualTo("1");
		await Assert.That(await EvalAs(_stranger.DbRef, $"timezone({Ref(_first)})")).IsEqualTo("#-1 NO PERMISSION TO GET ATTRIBUTE")
			.Because("timezone() is refused as get() would be");
		await Assert.That(await EvalAs(_second.DbRef, $"timezone({Ref(_first)})")).IsEqualTo(Zone);
		await Assert.That(await EvalAs(_second.DbRef, $"convsecs({Instant},{Ref(_first)})")).IsEqualTo(InstantInZone);
		await Assert.That(await EvalAs(_wizard.DbRef, $"convsecs({Instant},{Ref(_first)})")).IsEqualTo(InstantInZone);
	}

	/// <summary>A character TZ the reader may not see is not passed over for the account's.</summary>
	[Test]
	public async Task AHiddenCharacterZoneDoesNotFallThroughToTheAccount()
	{
		await CmdAs(_first.DbRef, _first.Handle, "&TZ me=UTC");
		await CmdAs(_first.DbRef, _first.Handle, "@set me/TZ=!visual");

		await Assert.That(await EvalAs(_stranger.DbRef, $"strmatch(convsecs({Instant},{Ref(_first)}),convsecs({Instant}))")).IsEqualTo("1");
		await Assert.That(await EvalAs(_stranger.DbRef, $"timezone({Ref(_first)})")).IsEqualTo("#-1 NO PERMISSION TO GET ATTRIBUTE");
		await Assert.That(await EvalAs(_first.DbRef, $"timezone({Ref(_first)})")).IsEqualTo("UTC");
	}

	/// <summary>An object with no TZ anywhere answers as get() does for an unset TZ: empty, since TZ is visual.</summary>
	[Test]
	public async Task NoZoneAnywhereIsEmptyToAnyone()
		=> await Assert.That(await EvalAs(_stranger.DbRef, $"[timezone({Ref(_wizard)})]|[get({Ref(_wizard)}/TZ)]"))
			.IsEqualTo("|");

	[Test]
	public async Task AnObjectIsNotInItsOwnersAccountsZone()
	{
		var thing = await CmdAs(_first.DbRef, _first.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("TzThing")}");
		var dbref = thing[thing.LastIndexOf('#')..].TrimEnd('.');

		await Assert.That(await EvalAs(_first.DbRef, $"strmatch(convsecs({Instant},{dbref}),convsecs({Instant}))")).IsEqualTo("1");
	}
}
