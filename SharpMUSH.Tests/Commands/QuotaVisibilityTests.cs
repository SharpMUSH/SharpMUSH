using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Who may read a quota. PennMUSH gates the read half of <c>@quota</c> at <c>src/wiz.c:179</c> —
/// <c>if (!Do_Quotas(player) &amp;&amp; !See_All(player) &amp;&amp; !controls(player, who))</c> → "You can't look
/// at someone else's quota." — where <c>Do_Quotas(x)</c> is <c>Wizard(x) || has_power_by_name(x,
/// "QUOTAS", NOTYPE)</c> (<c>hdrs/mushdb.h:34</c>). Setting a quota stays wizard-only whatever the
/// power says (<c>wiz.c:175-178</c>).
/// </summary>
/// <remarks>
/// Every case here drives a <em>mortal</em> handle. The shared fixture's handle 1 is God, so a
/// God-driven case passes whether or not the gate exists at all — which is how the missing check
/// survived review (#1131).
/// </remarks>
public class QuotaVisibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task AMortalCannotReadAnotherPlayersQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaSnoop");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaSubject");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@quota {target.Name}"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains(target.Name)))
			.IsFalse()
			.Because("the target's quota and owned-object count must not reach a mortal who does not control them");
	}

	[Test]
	public async Task AMortalCanReadTheirOwnQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaSelf");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@quota"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains($"{mortal.Name}'s quota")))
			.IsTrue()
			.Because("controls(player, player) is always true, so a player always sees their own quota");
	}

	[Test]
	public async Task AMortalCanReadTheQuotaOfAPlayerTheyControl()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaOwner");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@quota {mortal.Name}"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains($"{mortal.Name}'s quota"))).IsTrue();
	}

	/// <summary>
	/// <c>@squota</c> prints the same two numbers in short form and was unguarded beside <c>@quota</c>,
	/// so the gate has to sit on both reads.
	/// </summary>
	[Test]
	public async Task AMortalCannotReadAnotherPlayersShortQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "SQuotaSnoop");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "SQuotaSubject");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@squota {target.Name}"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.StartsWith("Quota:")))
			.IsFalse()
			.Because("the short form leaks the same used/quota pair as the long one");
	}

	[Test]
	public async Task AMortalCanReadTheirOwnShortQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "SQuotaSelf");

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@squota"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.StartsWith("Quota:"))).IsTrue();
	}

	/// <summary>
	/// The no-argument form run by something that is not a player. PennMUSH resolves it as
	/// <c>Owner(player)</c> (<c>src/wiz.c:165</c>), so the target is the owner and the gate then asks
	/// <c>controls(thing, owner)</c> — which is <b>false</b> for a plain thing: <c>controls</c> falls
	/// through <c>Owns</c> (the <c>Inheritable</c> clause fails) to
	/// <c>if (Inheritable(what) || IsPlayer(what)) return 0;</c> (<c>src/predicat.c:405</c>). So the
	/// answer is a refusal — but a refusal, not the
	/// <c>InvalidOperationException("A quota belongs to a player.")</c> this used to throw out of the
	/// command, taking its queue entry with it.
	/// </summary>
	[Test]
	public async Task APlainThingIsRefusedItsOwnersQuotaRatherThanThrowing()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaOwned");
		var thing = await CreateThingOwnedByAsync(mortal, "QuotaWidget");

		var result = await Factory.CommandParserFor(thing, mortal.Handle).CommandParse(MarkupText.Plain("@quota"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("predicat.c:405 refuses controls(thing, owner) once the target is a player");
	}

	/// <summary>
	/// The <c>controls</c> term admitting a target that is not the executor — the only shape of it
	/// <c>@quota</c> can reach, since nobody owns another player. <c>Inheritable(x)</c> is
	/// <c>IsPlayer(x) || Inherit(x) || Inherit(Owner(x)) || Wizard(x)</c> (<c>hdrs/dbdefs.h:219</c>),
	/// so TRUST on the thing satisfies the <c>Owns</c> clause's second half
	/// (<c>src/predicat.c:402</c>) and <c>controls</c> returns 1 before the player check can refuse it.
	/// </summary>
	[Test]
	public async Task ATrustedThingReadsItsOwnersQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaTrusted");
		var thing = await CreateThingOwnedByAsync(mortal, "QuotaTrustedWidget");

		// TRUST is settable by the trusted only, so God sets it.
		await Factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}=TRUST"));

		var before = Factory.Notifications.CountFor(thing);
		var result = await Factory.CommandParserFor(thing, mortal.Handle).CommandParse(MarkupText.Plain("@quota"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(thing).Skip(before).Any(m => m.Contains($"{mortal.Name}'s quota")))
			.IsTrue()
			.Because("a trusted thing controls its owner, so it reads the quota the owner would read");
	}

	/// <summary>
	/// The named-target read is what the power buys; <c>@quota/set</c> stays wizard-only.
	/// </summary>
	[Test]
	public async Task TheQuotasPowerLetsAMortalReadAnotherPlayersQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaPowered");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaPoweredSubject");

		await Factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mortal.DbRef}=Quotas"));

		var before = Factory.Notifications.CountFor(mortal.DbRef);
		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@quota {target.Name}"));

		await Assert.That(result.Message?.ToPlainText() ?? string.Empty).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains($"{target.Name}'s quota"))).IsTrue();
	}

	[Test]
	public async Task TheQuotasPowerDoesNotLetAMortalSetAQuota()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaSetter");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, ConnectionService, "QuotaSetterSubject");

		await Factory.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mortal.DbRef}=Quotas"));

		var result = await Factory.CommandParser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@quota/set {target.Name}=777"));

		await Assert.That(result.Message!.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("wiz.c:175-178 restricts setting to wizards regardless of the QUOTAS power");
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
