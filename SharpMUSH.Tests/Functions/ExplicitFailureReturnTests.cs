using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Every failing function says why. PennMUSH answers many failures with a bare <c>#-1</c> — sometimes
/// with the reason sent as a notification, often with no reason at all — and a bare <c>#-1</c> reads the
/// same whether the object was missing, refused, or simply had nothing to report. Softcode tests the
/// prefix (<c>strmatch(%0,#-*)</c>, and <c>t()</c> is false for anything starting <c>#-</c>), so
/// <c>#-1 NO ZONE SET</c> satisfies every check a bare <c>#-1</c> does while telling the reader what
/// happened.
/// </summary>
/// <remarks>
/// Permission cases drive a <em>mortal</em>: the fixture's handle 1 is God, and a God-driven refusal
/// test passes whether or not the gate exists.
/// </remarks>
public class ExplicitFailureReturnTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await Factory.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expr)))
			?.Message!.ToPlainText() ?? "<null>";

	private Task<string> EvalAsGod(string expr) => EvalAs(new DBRef(1), expr);

	private async Task<string> God(string command)
		=> (await Factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)))
			?.Message?.ToPlainText() ?? string.Empty;

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string label)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, label);

	private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}";

	private async Task<DBRef> Room(string prefix)
	{
		var output = await God($"@dig {Unique(prefix)}");
		return DBRef.Parse(output.Trim().Split(' ')[^1].Trim());
	}

	private async Task<DBRef> Thing(string prefix, DBRef? into = null)
	{
		var thing = DBRef.Parse((await God($"@create {Unique(prefix)}")).Trim());
		if (into is { } room) await God($"@tel #{thing.Number}=#{room.Number}");
		return thing;
	}

	private const string Missing = "NoSuchThingAnywhere00000000";

	// ---------------------------------------------------------------- zone()

	[Test]
	public async Task ZoneOfAnUnzonedObjectSaysNoZoneSet()
	{
		var thing = await Thing("ExplicitUnzoned");

		await Assert.That(await EvalAsGod($"zone({thing})")).IsEqualTo(ErrorMessages.Returns.NoZoneSet);
	}

	/// <summary>The claim the compatibility profile makes: a reason keeps the prefix softcode tests.</summary>
	[Test]
	public async Task AnExplicitErrorStillReadsAsAnErrorToSoftcode()
	{
		var thing = await Thing("ExplicitPrefix");

		await Assert.That(await EvalAsGod($"[strmatch(zone({thing}),#-*)] [t(zone({thing}))] [isdbref(zone({thing}))]"))
			.IsEqualTo("1 0 0");
	}

	[Test]
	public async Task ZoneRefusedIsPermissionDenied()
	{
		var mortal = await Mortal("ExplicitZoneSnoop");
		var thing = await Thing("ExplicitZoneTarget");

		await Assert.That(await EvalAs(mortal.DbRef, $"zone({thing})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	// ---------------------------------------------------------------- loc(), home(), rloc(), room(), where()

	[Test]
	public async Task LocAndHomeOfARoomWithoutADropToSaySo()
	{
		var room = await Room("ExplicitNoDropTo");

		await Assert.That(await EvalAsGod($"loc({room})")).IsEqualTo(ErrorMessages.Returns.NoDropTo);
		await Assert.That(await EvalAsGod($"home({room})")).IsEqualTo(ErrorMessages.Returns.NoDropTo);
	}

	[Test]
	public async Task LocOfAnUnlinkedExitSaysSo()
	{
		var room = await Room("ExplicitUnlinkedHost");
		var exit = DBRef.Parse((await God($"@open {Unique("ExplicitUnlinked")}=,#{room.Number}")).Trim().Split(' ')[^1].Trim());

		await Assert.That(await EvalAsGod($"loc({exit})")).IsEqualTo(ErrorMessages.Returns.NotLinked);
	}

	[Test]
	[Arguments("variable", ErrorMessages.Returns.VariableDestination)]
	[Arguments("home", ErrorMessages.Returns.HomeDestination)]
	public async Task LocOfASpecialExitNamesItsDestination(string link, string expected)
	{
		var room = await Room("ExplicitSpecialHost");
		var exit = DBRef.Parse((await God($"@open {Unique("ExplicitSpecial")}=,#{room.Number}")).Trim().Split(' ')[^1].Trim());
		await God($"@link #{exit.Number}={link}");

		await Assert.That(await EvalAsGod($"loc({exit})")).IsEqualTo(expected);
	}

	/// <summary>
	/// <c>fun_rloc</c> (<c>src/fundb.c:1579</c>) stops climbing at a room and returns it; it never
	/// answers "rooms have no location".
	/// </summary>
	[Test]
	public async Task RlocStopsAtTheRoom()
	{
		var room = await Room("ExplicitRlocRoom");
		var thing = await Thing("ExplicitRlocThing", room);

		// rloc() answers an objid; the dbref half is what this pins.
		await Assert.That(await EvalAsGod($"rloc({thing},5)")).StartsWith($"#{room.Number}:");
		await Assert.That(await EvalAsGod($"rloc({room},3)")).StartsWith($"#{room.Number}:");
	}

	/// <summary>
	/// <c>Can_Locate</c> gates <c>loc</c>, <c>rloc</c>, <c>room</c> and <c>where</c>
	/// (<c>src/fundb.c:1458, 1576, 1550, 1537</c>), and <c>Can_Examine</c> gates <c>home</c>
	/// (<c>:1670</c>): a mortal may not learn where something they neither control nor stand near is.
	/// </summary>
	[Test]
	[Arguments("loc({0})")]
	[Arguments("rloc({0},1)")]
	[Arguments("room({0})")]
	[Arguments("where({0})")]
	[Arguments("home({0})")]
	public async Task AMortalCannotLocateSomethingFarAway(string template)
	{
		var mortal = await Mortal("ExplicitLocSnoop");
		var faraway = await Room("ExplicitLocFar");
		var thing = await Thing("ExplicitLocHidden", faraway);

		await Assert.That(await EvalAs(mortal.DbRef, string.Format(template, thing)))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	// ---------------------------------------------------------------- the dbwalk family

	[Test]
	public async Task AWalkOfSomethingThatDoesNotExistSaysSo()
	{
		await Assert.That(await EvalAsGod($"lcon({Missing})")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await EvalAsGod($"con({Missing})")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await EvalAsGod($"ncon({Missing})")).IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	[Test]
	public async Task ARefusedWalkIsPermissionDenied()
	{
		var mortal = await Mortal("ExplicitWalkSnoop");
		var faraway = await Room("ExplicitWalkFar");

		await Assert.That(await EvalAs(mortal.DbRef, $"lcon({faraway})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, $"lexits({faraway})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, $"ncon({faraway})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task ConAndExitOfAnEmptyRoomSaySo()
	{
		var room = await Room("ExplicitEmpty");

		await Assert.That(await EvalAsGod($"con({room})")).IsEqualTo(ErrorMessages.Returns.NoContents);
		await Assert.That(await EvalAsGod($"exit({room})")).IsEqualTo(ErrorMessages.Returns.NoExits);
	}

	[Test]
	public async Task NextAfterTheLastObjectSaysSo()
	{
		var room = await Room("ExplicitNextRoom");
		var only = await Thing("ExplicitNextOnly", room);

		await Assert.That(await EvalAsGod($"next({only})")).IsEqualTo(ErrorMessages.Returns.NoNextObject);
		await Assert.That(await EvalAsGod($"next({room})")).IsEqualTo(ErrorMessages.Returns.NoNextObject);
	}

	[Test]
	public async Task LconWithAnUnknownFilterSaysSo()
		=> await Assert.That(await EvalAsGod("lcon(here,bogusfilter)")).IsEqualTo(ErrorMessages.Returns.InvalidArgument);

	// ---------------------------------------------------------------- locate()

	[Test]
	public async Task LocateSaysWhyItFoundNothing()
		=> await Assert.That(await EvalAsGod($"locate(me,{Missing},*)")).IsEqualTo(ErrorMessages.Returns.NoMatch);

	[Test]
	public async Task LocateSaysWhenAMatchIsAmbiguous()
	{
		var room = await Room("ExplicitAmbiguousRoom");
		var name = Unique("ExplicitTwin");
		await Thing("unused", room);
		foreach (var _ in Enumerable.Range(0, 2))
		{
			var twin = DBRef.Parse((await God($"@create {name}")).Trim());
			await God($"@tel #{twin.Number}=#{room.Number}");
		}

		await Assert.That(await EvalAsGod($"locate(#{room.Number},{name},T)")).IsEqualTo(ErrorMessages.Returns.AmbiguousMatch);
	}

	[Test]
	public async Task LocateRefusedIsPermissionDenied()
	{
		var mortal = await Mortal("ExplicitLocateSnoop");
		var faraway = await Room("ExplicitLocateFar");

		await Assert.That(await EvalAs(mortal.DbRef, $"locate(#{faraway.Number},anything,n)"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	// ---------------------------------------------------------------- locks

	[Test]
	public async Task ReadingAnotherPlayersLockIsPermissionDenied()
	{
		var mortal = await Mortal("ExplicitLockSnoop");
		var thing = await Thing("ExplicitLocked");
		await God($"@lock #{thing.Number}=#1");

		await Assert.That(await EvalAs(mortal.DbRef, $"elock(#{thing.Number},me)")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, $"lock(#{thing.Number})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task ALockCallOnSomethingMissingSaysSo()
	{
		var thing = await Thing("ExplicitLockVictimless");

		await Assert.That(await EvalAsGod($"elock(#{thing.Number},{Missing})")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await EvalAsGod($"lock({Missing}/Basic,#1)")).IsEqualTo(ErrorMessages.Returns.NoMatch);
		await Assert.That(await EvalAsGod($"lset({Missing}/Basic,visual)")).IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	[Test]
	public async Task TestlockOnAVictimYouCannotLocateIsPermissionDenied()
	{
		var mortal = await Mortal("ExplicitTestlockSnoop");
		var faraway = await Room("ExplicitTestlockFar");
		var thing = await Thing("ExplicitTestlockVictim", faraway);

		await Assert.That(await EvalAs(mortal.DbRef, $"testlock(#1,#{thing.Number})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	// ---------------------------------------------------------------- nearby(), rnum()

	[Test]
	public async Task NearbyOfSomethingMissingSaysSo()
		=> await Assert.That(await EvalAsGod($"nearby({Missing},me)")).IsEqualTo(ErrorMessages.Returns.NoMatch);

	[Test]
	public async Task RnumSaysWhyItFailed()
	{
		var mortal = await Mortal("ExplicitRnumSnoop");
		var faraway = await Room("ExplicitRnumFar");
		var room = await Room("ExplicitRnumRoom");
		var exit = DBRef.Parse((await God($"@open {Unique("ExplicitRnumExit")}=,#{room.Number}")).Trim().Split(' ')[^1].Trim());

		await Assert.That(await EvalAs(mortal.DbRef, $"rnum(#{faraway.Number},anything)")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAsGod($"rnum(#{exit.Number},anything)")).IsEqualTo(ErrorMessages.Returns.InvalidObjectType);
		await Assert.That(await EvalAsGod($"rnum(#{room.Number},{Missing})")).IsEqualTo(ErrorMessages.Returns.NoMatch);

		var twins = Unique("ExplicitRnumTwin");
		foreach (var _ in Enumerable.Range(0, 2))
		{
			await God($"@tel {(await God($"@create {twins}")).Trim()}=#{room.Number}");
		}

		await Assert.That(await EvalAsGod($"rnum(#{room.Number},{twins})")).IsEqualTo(ErrorMessages.Returns.AmbiguousMatch);
	}

	// ---------------------------------------------------------------- values that do not parse

	[Test]
	public async Task AnUnparseableTimeSaysSo()
	{
		await Assert.That(await EvalAsGod("convtime(not a time at all)")).IsEqualTo(ErrorMessages.Returns.InvalidTime);
		await Assert.That(await EvalAsGod("convutctime(not a time at all)")).IsEqualTo(ErrorMessages.Returns.InvalidTime);
	}

	[Test]
	public async Task JsonNullWithAValueSaysSo()
		=> await Assert.That(await EvalAsGod("json(null,5)")).IsEqualTo(ErrorMessages.Returns.InvalidArgument);

	[Test]
	public async Task RegistersWithAnUnknownKindSaysSo()
		=> await Assert.That(await EvalAsGod("registers(*,bogus)")).IsEqualTo(ErrorMessages.Returns.InvalidArgument);

	// ---------------------------------------------------------------- connections

	[Test]
	[NotInParallel(nameof(TestOptionsOverride))]
	[Arguments("addrlog(ip,*)")]
	[Arguments("connlog(all,logins,count)")]
	[Arguments("connrecord(1)")]
	public async Task TheConnectionLogFunctionsSayWhenTheLogIsOff(string call)
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Log = options.Log with { UseConnLog = false }
		});

		await Assert.That(await EvalAsGod(call)).IsEqualTo(ErrorMessages.Returns.ConnectionLogDisabled);
	}

	/// <summary>
	/// A descriptor lookup cannot separate "no such descriptor" from "not yours": telling a mortal which
	/// one it was tells them whether the descriptor exists. One explicit string covers both.
	/// </summary>
	[Test]
	[Arguments("host")]
	[Arguments("ipaddr")]
	public async Task ADescriptorLookupFailsTheSameWayWhetherOrNotItExists(string function)
	{
		var mortal = await Mortal("ExplicitDescSnoop");
		var other = await Mortal("ExplicitDescOther");

		await Assert.That(await EvalAs(mortal.DbRef, $"{function}(987654321)"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchDescriptorOrPermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, $"{function}({other.Handle})"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchDescriptorOrPermissionDenied);
	}

	[Test]
	[Arguments("host")]
	[Arguments("ipaddr")]
	public async Task APlayerLookupSaysWhyItFailed(string function)
	{
		var mortal = await Mortal("ExplicitHostSnoop");
		var other = await Mortal("ExplicitHostOther");
		var offline = Unique("ExplicitOffline")[..20];
		await God($"@pcreate {offline}=password");

		await Assert.That(await EvalAs(mortal.DbRef, $"{function}({other.Name})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAsGod($"{function}(*{offline})")).IsEqualTo(ErrorMessages.Returns.NotConnected);
	}

	[Test]
	public async Task HiddenSaysWhyInItsReturnValueAndNotifiesNothing()
	{
		var mortal = await Mortal("ExplicitHiddenSnoop");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		await Assert.That(await EvalAs(mortal.DbRef, "hidden(me)")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before)).IsEmpty();

		await Assert.That(await EvalAsGod("hidden(987654321)")).IsEqualTo(ErrorMessages.Returns.NoSuchDescriptor);
	}
}
