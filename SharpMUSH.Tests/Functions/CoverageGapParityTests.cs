using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// pos(), art(), ordinal(), player(), parent(), lparent() and children(), which no test dispatched
/// until the #974 coverage inventory, and which all differed from PennMUSH (GRA-126).
///
/// <para>Every expected value is what PennMUSH 1.8.8 (80a1d5b9) printed for the same call: the parity
/// harness's <c>sc.coverage-gaps</c> case, or a live oracle run of the same shape. The parent-family
/// tests run as a fresh mortal, so their notices land in that player's own bucket and the
/// permission checks are real.</para>
/// </summary>
public class CoverageGapParityTests : ServerTestBase
{
	[Test]
	[Arguments("pos(b,abc)", "2")]
	[Arguments("pos(bc,abcbc)", "2")]
	[Arguments("pos(z,abc)", "#-1")]
	[Arguments("pos(,abc)", "1")]
	[Arguments("pos(c,abcd)", "3")]
	[Arguments("pos(a,)", "#-1")]
	[Arguments("pos(B,abc)", "#-1")]
	[Arguments("art(unicorn)", "an")]
	[Arguments("art(hour)", "a")]
	[Arguments("art(Apple)", "an")]
	[Arguments("art()", "a")]
	[Arguments("ordinal(100)", "one hundredth")]
	[Arguments("ordinal(-1)", "negative first")]
	[Arguments("ordinal(abc)", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments("ordinal(1.5)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("ordinal(a.5)", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments("ordinal(1e3)", "#-1 ARGUMENT MUST BE NUMBER")]
	// fun_spellnum answers INTEGER at the first '.', before it has looked at what follows.
	[Arguments("ordinal(1.2.3)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("ordinal(1.x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("ordinal(x.1)", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments("ordinal(101)", "one hundred first")]
	[Arguments("ordinal(112)", "one hundred twelfth")]
	[Arguments("ordinal(90)", "ninetieth")]
	[Arguments("ordinal(1000001)", "one million first")]
	[Arguments("ordinal(-12)", "negative twelfth")]
	[Arguments("ordinal(-0)", "zeroth")]
	[Arguments("ordinal(+5)", "fifth")]
	[Arguments("ordinal()", "zeroth")]
	[Arguments("ordinal(999999999999999)", "nine hundred ninety-nine trillion nine hundred ninety-nine billion nine hundred ninety-nine million nine hundred ninety-nine thousand nine hundred ninety-ninth")]
	[Arguments("ordinal(1000000000000000)", "#-1 OUT OF RANGE")]
	[Arguments("player(-1)", "#-1")]
	[Arguments("player(abc)", "#-1")]
	public async Task AgreesWithPennMUSH(string call, string expected)
	{
		await Assert.That(await Eval(call)).IsEqualTo(expected);
	}

	[Test]
	public async Task PlayerAnswersForYourOwnConnectionOnly()
	{
		var mortal = await Mortal("PlayerFn");
		var self = $"#{mortal.DbRef.Number}";

		await Assert.That(await EvalAs(mortal.DbRef, $"player({mortal.Handle})")).IsEqualTo(self);
		await Assert.That(await EvalAs(mortal.DbRef, "player(me)")).IsEqualTo(self);
		await Assert.That(await EvalAs(mortal.DbRef, "player(1)")).IsEqualTo("#-1")
			.Because("a descriptor number that is not your own answers only for See_All");
		await Assert.That(await Eval($"player({mortal.Handle})")).IsEqualTo(self);
		await Assert.That(await Eval($"player({self})")).IsEqualTo(self);
	}

	[Test]
	public async Task ParentLparentAndChildrenAgreeWithPennMUSH()
	{
		var mortal = await Mortal("ParentFn");
		var child = await Create(mortal, "ParentFnChild");
		var parent = await Create(mortal, "ParentFnParent");
		var (c, p) = ($"#{child.Number}", $"#{parent.Number}");

		await Assert.That(await EvalWithNotices(mortal, $"parent({c},{p})")).IsEqualTo((p, "Parent changed."));
		await Assert.That(await EvalAs(mortal.DbRef, $"lparent({c})")).IsEqualTo($"{c} {p}");
		await Assert.That(await EvalAs(mortal.DbRef, $"lparent({p})")).IsEqualTo(p);
		await Assert.That(await EvalAs(mortal.DbRef, $"children({p})")).IsEqualTo(c);
		await Assert.That(await EvalWithNotices(mortal, $"children({c})")).IsEqualTo(("", "Nothing found."));

		await Assert.That(await EvalWithNotices(mortal, $"parent({c},{c})"))
			.IsEqualTo((p, "A thing cannot be its own ancestor!"));
		await Assert.That(await EvalWithNotices(mortal, $"parent({c},none)")).IsEqualTo(("#-1", "Parent changed."));
		await Assert.That(await EvalAs(mortal.DbRef, $"lparent({c})")).IsEqualTo(c);
	}

	/// <summary>
	/// <c>children()</c> takes a dbref, as <c>lsearch()</c>'s PARENT class does; a name is not searched.
	/// </summary>
	[Test]
	public async Task ChildrenOfANameIsAnUnknownParent()
	{
		var mortal = await Mortal("ChildrenName");

		await Assert.That(await EvalWithNotices(mortal, "children(me)")).IsEqualTo(("#-1", "Unknown parent."));
	}

	/// <summary>
	/// A mortal sees the parent chain only as far as the objects it can examine, and only the children it
	/// owns; God sees all of both.
	/// </summary>
	[Test]
	public async Task AMortalSeesOnlyWhatItCanExamineOrOwns()
	{
		var mortal = await Mortal("ParentMortal");
		var godChild = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "GodChild");
		var godParent = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "GodParent");
		await Cmd($"@parent {godChild}={godParent}");
		await Cmd($"@set {godParent}=LINK_OK");
		var mortalChild = await Create(mortal, "MortalChild");
		await CmdAs(mortal.DbRef, mortal.Handle, $"@parent {mortalChild}={godParent}");
		var (gc, gp, mc) = ($"#{godChild.Number}", $"#{godParent.Number}", $"#{mortalChild.Number}");

		await Assert.That(await EvalAs(mortal.DbRef, $"lparent({gc})")).IsEqualTo(gc);
		await Assert.That(await EvalAs(mortal.DbRef, $"lparent({mc})")).IsEqualTo($"{mc} {gp}");
		await Assert.That(await EvalAs(mortal.DbRef, $"parent({gc})")).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(await EvalAs(mortal.DbRef, $"children({gp})")).IsEqualTo(mc);
		await Assert.That(await Eval($"children({gp})")).IsEqualTo($"{gc} {mc}");
	}

	private readonly List<long> _handles = [];

	[After(Test)]
	public async Task DisconnectMortals()
	{
		foreach (var handle in _handles) await ConnectionService.Disconnect(handle);
	}

	/// <summary>
	/// A connected mortal alone in a room of its own, so the only notices it gets are the ones its own
	/// calls produce, not other tests' players coming and going in the shared starting room.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix + "Room"), god));
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
		_handles.Add(mortal.Handle);
		return mortal;
	}

	private async Task<DBRef> Create(TestIsolationHelpers.TestPlayer owner, string name)
		=> DBRef.Parse(await CmdAs(owner.DbRef, owner.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName(name)}"));

	/// <summary>The call's result, and what it notified the caller of, one notice per line.</summary>
	private async Task<(string Result, string Notices)> EvalWithNotices(TestIsolationHelpers.TestPlayer who, string call)
	{
		var before = Notifications.CountFor(who.DbRef);
		var result = await EvalAs(who.DbRef, call);
		return (result, string.Join("\n", Notifications.For(who.DbRef).Skip(before)));
	}
}
