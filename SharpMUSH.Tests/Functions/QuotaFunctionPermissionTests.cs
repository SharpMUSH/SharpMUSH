using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// PennMUSH's <c>fun_quota</c> (<c>src/wiz.c:1864-1896</c>): match a <em>player</em>, refuse
/// unless <c>Do_Quotas(executor) || See_All(executor) || controls(executor, who)</c>, answer
/// <c>99999</c> for a No_Quota holder, and otherwise return one integer — the player's limit
/// (<c>owned + get_current_quota(who)</c>, which is what SharpMUSH stores as the quota itself).
/// Failures depart from Penn on purpose: Penn returns a bare <c>#-1</c> and notifies the reason,
/// where this returns the reason — <c>#-1 PERMISSION DENIED</c>, or the match's own error.
/// </summary>
/// <remarks>
/// Every permission case drives a <em>mortal</em> handle. The fixture's handle 1 is God, so a
/// God-driven case passes whether or not the gate exists (#1131, #1153).
/// </remarks>
public class QuotaFunctionPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	private async Task<string> EvalAs(DBRef executor, string expr)
		=> (await Factory.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expr)))
			?.Message!.ToPlainText() ?? "<null>";

	private Task God(string command)
		=> Factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string label)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, label);

	[Test]
	public async Task AMortalCannotReadAnotherPlayersQuota()
	{
		var mortal = await Mortal("QuotaFnSnoop");
		var target = await Mortal("QuotaFnSubject");
		await God($"@quota/set {target.Name}=37");

		await Assert.That(await EvalAs(mortal.DbRef, $"quota({target.Name})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("wiz.c:1876 refuses a mortal who neither controls the player nor holds See_All or Quotas");
	}

	/// <summary>
	/// The refusal is carried by the return value, so the function does not also notify — Penn's
	/// "You can't see someone else's quota!" would land once per call from inside an <c>iter()</c>.
	/// <para>A function notifies its executor as the executor or with no sender. The mortal stands in
	/// the shared starting room, so a parallel test's player leaving it is heard as "&lt;name&gt; has
	/// left." from that player; only deliveries this call could have made are counted.</para>
	/// </summary>
	[Test]
	public async Task TheRefusalIsTheReturnValueNotANotification()
	{
		var mortal = await Mortal("QuotaFnTold");
		var target = await Mortal("QuotaFnToldSubject");

		var before = Factory.Notifications.DeliveryCountFor(mortal.DbRef);
		await EvalAs(mortal.DbRef, $"quota({target.Name})");

		var fromTheCall = Factory.Notifications.DeliveriesFor(mortal.DbRef).Skip(before)
			.Where(delivery => delivery.Sender is not { } sender || sender.Number == mortal.DbRef.Number)
			.Select(delivery => delivery.Message);
		await Assert.That(fromTheCall).IsEmpty();
	}

	[Test]
	public async Task ANameThatMatchesNoPlayerSaysSo()
	{
		var mortal = await Mortal("QuotaFnMiss");

		await Assert.That(await EvalAs(mortal.DbRef, $"quota(NoSuchQuotaPlayer{Guid.NewGuid():N})"))
			.IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	/// <summary>
	/// <c>MAT_ABSOLUTE</c> is not in fun_quota's flags, but <c>MAT_PMATCH</c> reaches
	/// <c>lookup_player</c>, which accepts a <c>#dbref</c> naming a player (<c>src/plyrlist.c:169</c>).
	/// </summary>
	[Test]
	public async Task APlayersDbrefMatches()
	{
		var mortal = await Mortal("QuotaFnByDbref");
		await God($"@quota/set {mortal.Name}=71");

		await Assert.That(await EvalAs(mortal.DbRef, $"quota({mortal.DbRef})")).IsEqualTo("71");
	}

	[Test]
	public async Task AMortalReadsTheirOwnQuotaAsOneInteger()
	{
		var mortal = await Mortal("QuotaFnSelf");
		await God($"@quota/set {mortal.Name}=41");

		await Assert.That(await EvalAs(mortal.DbRef, "quota(me)")).IsEqualTo("41")
			.Because("the documented result is the player's limit alone, safe in a numerical comparison");
	}

	[Test]
	public async Task TheQuotasPowerLetsAMortalReadAnotherPlayersQuota()
	{
		var mortal = await Mortal("QuotaFnPowered");
		var target = await Mortal("QuotaFnPoweredSubject");
		await God($"@quota/set {target.Name}=43");
		await God($"@power {mortal.DbRef}=Quotas");

		await Assert.That(await EvalAs(mortal.DbRef, $"quota({target.Name})")).IsEqualTo("43");
	}

	[Test]
	public async Task TheSeeAllPowerLetsAMortalReadAnotherPlayersQuota()
	{
		var mortal = await Mortal("QuotaFnSeer");
		var target = await Mortal("QuotaFnSeerSubject");
		await God($"@quota/set {target.Name}=47");
		await God($"@power {mortal.DbRef}=See_All");

		await Assert.That(await EvalAs(mortal.DbRef, $"quota({target.Name})")).IsEqualTo("47");
	}

	[Test]
	public async Task ANoQuotaHolderReads99999()
	{
		var mortal = await Mortal("QuotaFnUnlimited");
		await God($"@quota/set {mortal.Name}=53");
		await God($"@power {mortal.DbRef}=No_Quota");

		await Assert.That(await EvalAs(mortal.DbRef, "quota(me)")).IsEqualTo("99999")
			.Because("wiz.c:1887 answers 99999 for No_Quota so the result stays safe to compare");
	}

	/// <summary>
	/// <c>noisy_match_result(..., TYPE_PLAYER, MAT_TYPE | ...)</c> matches players only. Accepting any
	/// object and reporting its owner's quota was a second way round the gate: find something the
	/// target owns and ask about that.
	/// </summary>
	[Test]
	public async Task AThingIsNotAPlayerAndYieldsNoQuota()
	{
		var mortal = await Mortal("QuotaFnThingAsker");
		var target = await Mortal("QuotaFnThingOwner");
		await God($"@quota/set {target.Name}=59");
		var thing = await CreateThingOwnedByAsync(target, "QuotaFnWidget");

		var result = await EvalAs(mortal.DbRef, $"quota({thing})");

		await Assert.That(result).IsEqualTo(ErrorMessages.Returns.NoMatch);
	}

	/// <summary>
	/// The one reachable non-self <c>controls</c> case: a TRUST thing controls its owner
	/// (<c>src/predicat.c:402</c>, <c>Inheritable</c> at <c>hdrs/dbdefs.h:219</c>).
	/// </summary>
	[Test]
	public async Task ATrustedThingReadsItsOwnersQuota()
	{
		var owner = await Mortal("QuotaFnTrustOwner");
		await God($"@quota/set {owner.Name}=61");
		var thing = await CreateThingOwnedByAsync(owner, "QuotaFnTrusted");
		await God($"@set {thing}=TRUST");

		await Assert.That(await EvalAs(thing, $"quota({owner.DbRef})")).IsEqualTo("61");
	}

	[Test]
	public async Task APlainThingCannotReadItsOwnersQuota()
	{
		var owner = await Mortal("QuotaFnPlainOwner");
		await God($"@quota/set {owner.Name}=67");
		var thing = await CreateThingOwnedByAsync(owner, "QuotaFnPlain");

		await Assert.That(await EvalAs(thing, $"quota({owner.DbRef})")).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("predicat.c:405 refuses controls(thing, player) without TRUST");
	}

	private async Task<DBRef> CreateThingOwnedByAsync(TestIsolationHelpers.TestPlayer owner, string namePrefix)
	{
		var name = $"{namePrefix}{Guid.NewGuid():N}";
		var created = await Factory.CommandParser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@create {name}"));

		var message = created.Message?.ToPlainText() ?? string.Empty;
		await Assert.That(DBRef.TryParse(message, out _)).IsTrue()
			.Because($"@create {name} must return a dbref, got: \"{message}\"");

		return DBRef.Parse(message);
	}
}
