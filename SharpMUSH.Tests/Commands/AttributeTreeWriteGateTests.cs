using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Completes the write gate in <c>PermissionService.CanSet</c>: the <c>safe</c> attribute flag
/// (PennMUSH's <c>Cannot_Write_This_Attr</c>, <c>src/attrib.c:364-368</c>) and create-time
/// <c>nodump</c> (<c>can_create_attr</c>, <c>src/attrib.c:479-483</c>). Both flags previously had
/// zero effect on <c>CanSet</c> - the TODO comment this file replaces said as much.
/// <para>
/// The <see cref="INotifyService"/> substitute is shared across the whole test session, so no
/// assertion here reads <c>ReceivedCalls()</c> and none calls <c>ClearReceivedCalls()</c> -
/// <c>[NotInParallel]</c> only serialises this class against other <c>[NotInParallel]</c> tests,
/// so clearing would delete the recorded calls of whatever parallelizable test is running
/// alongside. Notification assertions go through <see cref="MessagesWhile"/>, which reads the
/// recipient-keyed <see cref="TestHelpers.NotificationRecorder"/> and windows it to the messages
/// produced by one command.
/// </para>
/// </summary>
[NotInParallel]
public class AttributeTreeWriteGateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, in
	/// order. Reads the recipient-keyed recorder rather than the session-shared NSubstitute call
	/// list, so a concurrent test's notifications are invisible here and this test's assertions
	/// never need to clear anyone else's.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>
	/// Runs <paramref name="expression"/> as the player behind <paramref name="handle"/>.
	/// FunctionParse always evaluates as the parser's bound executor (God), which would take
	/// the isPrivileged early-out, so every viewer-sensitive check goes through think.
	/// </summary>
	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	/// <summary>
	/// Penn's <c>Cannot_Write_This_Attr</c> applies to EVERY ancestor node
	/// <c>can_write_attr_internal</c> walks (<c>src/attrib.c:383-408</c>), so a <c>safe</c>
	/// branch must block a write to its leaf even though the leaf itself carries no flag.
	/// Uses a wizard owner: <c>safe</c> has no <c>Wizard(p)</c> escape the way the
	/// wizard-attribute-lock check does, so this also proves wizard doesn't bypass it.
	/// </summary>
	[Test]
	public async ValueTask SafeBranch_BlocksWritingALeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGSafeBOwner");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WSB{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WSB{uid}`LEAF me=original"));
		// Applied by God, not the wizard owner: SetAttributeFlagAsync doesn't call CanSet yet
		// (Task 6), so a wizard could self-apply "safe" today only because of that gap - not
		// because CanSet actually grants it. Routing the flag-set through God keeps this test
		// isolated to the CanSet write gate, so it stays green once Task 6 closes that gap.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {ownerDbRef}/WSB{uid}=safe"));

		// Control: an unflagged sibling branch's leaf can be overwritten by its wizard owner,
		// so a no-op on the safe branch's leaf below is the safe flag, not a Controls failure
		// or a broken backtick path.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WSBOK{uid}`LEAF me=original"));
		await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=WSBOK{uid}`LEAF:changed"));
		var control = await Eval(owner.Handle, $"get(me/WSBOK{uid}`LEAF)");
		await Assert.That(control).IsEqualTo("changed")
			.Because("a wizard owner can overwrite a leaf under an unflagged branch");

		var attempt = await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=WSB{uid}`LEAF:changed"));
		await Assert.That(attempt.Message?.ToPlainText() ?? string.Empty).Contains("NO PERMISSION")
			.Because("AF_SAFE on the branch must block writes to its leaf, even for the wizard owner");
	}

	/// <summary>
	/// Penn's <c>Cannot_Write_This_Attr</c> tests <c>AF_Safe</c> unconditionally once
	/// <c>God(p)</c> is false - there is no <c>Wizard(p)</c> escape the way there is for the
	/// wizard-attribute-lock clause. A wizard owner must still be denied writing its own safe
	/// attribute directly.
	/// </summary>
	[Test]
	public async ValueTask SafeAttribute_BlocksWritingItself()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGSafeAOwner");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WSA{uid} me=original"));
		// Applied by God, not the wizard owner - see the comment in SafeBranch_BlocksWritingALeaf.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {ownerDbRef}/WSA{uid}=safe"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WSAOK{uid} me=original"));

		// Control: the wizard owner can overwrite its own unflagged sibling attribute, so a
		// no-op on the safe one below is the flag denial, not a Controls/locate failure.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me=WSAOK{uid}:changed"));
		var control = await Eval(owner.Handle, $"get(me/WSAOK{uid})");
		await Assert.That(control).IsEqualTo("changed")
			.Because("a wizard owner can overwrite its own unflagged attribute");

		var attempt = await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=WSA{uid}:changed"));
		await Assert.That(attempt.Message?.ToPlainText() ?? string.Empty).Contains("NO PERMISSION")
			.Because("AF_SAFE blocks writes for everyone but God - a wizard owner must not be able to overwrite it");
	}

	/// <summary>
	/// Penn's <c>can_create_attr</c> (<c>src/attrib.c:479-483</c>): "Only GOD can create an
	/// AF_NODUMP attribute (used for semaphores) or add a leaf to a tree with such an
	/// attribute." <c>player != GOD</c>, not merely non-wizard, so a wizard owner must still
	/// be denied creating a new leaf under a nodump branch, while God is allowed through.
	/// </summary>
	[Test]
	public async ValueTask NodumpAttribute_IsGodOnlyToCreateUnder()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGNodumpOwner");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WND{uid} me=branchvalue"));
		// Applied by God, not the wizard owner - see the comment in SafeBranch_BlocksWritingALeaf.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {ownerDbRef}/WND{uid}=nodump"));

		// Control: the wizard owner CAN create a new leaf under an unflagged sibling branch,
		// so a miss on the nodump branch below is the flag denial, not a broken backtick-path
		// leaf creation.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WNDOK{uid} me=okbranch"));
		await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=WNDOK{uid}`LEAF:okleaf"));
		var control = await Eval(owner.Handle, $"get(me/WNDOK{uid}`LEAF)");
		await Assert.That(control).IsEqualTo("okleaf")
			.Because("a wizard owner can create a new leaf under an unflagged branch");

		var wizardAttempt = await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=WND{uid}`LEAF:leafvalue"));
		await Assert.That(wizardAttempt.Message?.ToPlainText() ?? string.Empty).Contains("NO PERMISSION")
			.Because("only God may create a leaf under a nodump branch - a wizard owner must be denied");

		// God (dbref #1 in the seeded database) creating the same leaf must succeed.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {ownerDbRef}=WND{uid}`LEAF:godvalue"));
		var godResult = await Eval(owner.Handle, $"get(me/WND{uid}`LEAF)");
		await Assert.That(godResult).IsEqualTo("godvalue")
			.Because("God bypasses the nodump create-time gate");
	}

	/// <summary>
	/// Task 6: <c>SetAttributeFlagAsync</c>/<c>UnsetAttributeFlagAsync</c> (<c>@set obj/attr=flag</c>
	/// and <c>@set obj/attr=!flag</c>) gated on <c>AttributeMode.Execute</c> -&gt;
	/// <c>CanExecuteAttribute</c>, which tests only object privilege and <c>public</c> - never
	/// <c>wizard</c>. A mortal owner could therefore strip <c>wizard</c> off its own attribute.
	/// Penn's <c>af_helper</c> (<c>src/set.c:509-511</c>) always requires <c>Can_Write_Attr</c>
	/// (which denies non-wizard writes to an <c>AF_Wizard</c> attribute) except for the one
	/// special-cased <c>AF_SAFE</c>-clearing bypass - wizard has no such escape.
	/// </summary>
	[Test]
	public async ValueTask MortalOwner_CannotStripWizardFromOwnAttribute()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGMortalOwner");
		var ownerDbRef = owner.DbRef.ToString();
		var obj = await Mediator.Send(new GetObjectNodeQuery(owner.DbRef));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&MSW{uid} me=original"));
		// God applies WIZARD directly, so the precondition doesn't depend on the very path
		// under test (SetAttributeFlagAsync) having been trustworthy.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {ownerDbRef}/MSW{uid}=WIZARD"));

		var before = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"MSW{uid}",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(before.AsAttribute.Last().Flags.Any(f => f.Name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase)))
			.IsTrue().Because("the precondition must hold before the mortal's strip attempt means anything");

		// Control: a mortal owner CAN unset an unrelated, unprivileged flag on its own
		// attribute, so a no-op on the wizard attribute below is the wizard-flag denial, not a
		// broken @set/! parse or a Controls failure.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&MSWOK{uid} me=original"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {ownerDbRef}/MSWOK{uid}=VISUAL"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/MSWOK{uid}=!VISUAL"));
		var controlAfter = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"MSWOK{uid}",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(controlAfter.AsAttribute.Last().Flags.Any(f => f.Name.Equals("VISUAL", StringComparison.OrdinalIgnoreCase)))
			.IsFalse().Because("a mortal owner can unset an unprivileged flag on its own attribute");

		// The mortal owner attempts to strip WIZARD from its own attribute.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/MSW{uid}=!WIZARD"));

		var after = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"MSW{uid}",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(after.AsAttribute.Last().Flags.Any(f => f.Name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase)))
			.IsTrue().Because("a mortal owner must never be able to strip WIZARD off its own attribute");
	}

	/// <summary>
	/// Task 6: <c>ClearAttributeAsync</c> called <c>CanSet</c> with each matched attribute
	/// alone (<c>:852</c>), not its ancestor path, so <c>@wipe</c> could delete a leaf under a
	/// <c>wizard</c>-flagged branch even though the leaf itself carries no flag. Penn's
	/// <c>can_write_attr_internal</c> (<c>src/attrib.c:383-408</c>) walks every ancestor node on
	/// the way to the leaf and denies the whole write if any one of them fails
	/// <c>Cannot_Write_This_Attr</c> - the branch's <c>wizard</c> flag must block the leaf wipe
	/// even for the object's own mortal owner.
	/// </summary>
	[Test]
	public async ValueTask WipeUnderWizardBranch_IsRefused()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeOwner");
		var obj = await Mediator.Send(new GetObjectNodeQuery(owner.DbRef));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WWB{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WWB{uid}`LEAF me=leafvalue"));
		// God flags the branch WIZARD - not the leaf underneath it.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}/WWB{uid}=WIZARD"));

		// Control: the mortal owner CAN wipe an unflagged sibling branch's leaf directly, so a
		// no-op on the wizard branch's leaf below is the ancestor flag denial, not a Controls
		// failure or a broken @wipe/backtick path.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WWBOK{uid} me=okbranch"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WWBOK{uid}`LEAF me=okleaf"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@wipe me/WWBOK{uid}`LEAF"));
		var controlLeaf = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WWBOK{uid}`LEAF",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(controlLeaf.IsAttribute).IsFalse()
			.Because("a mortal owner can wipe a leaf under an unflagged sibling branch");

		// Target the LEAF specifically, not the branch: the branch node itself carries WIZARD
		// directly, so wiping it would be denied even by the old per-leaf-only CanSet call - that
		// would prove nothing about the ancestor walk. Only a pattern that matches solely the
		// unflagged leaf (never touching the branch node in attrArr) exercises the actual gap.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@wipe me/WWB{uid}`LEAF"));

		var leafAfter = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WWB{uid}`LEAF",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(leafAfter.IsAttribute).IsTrue()
			.Because("a wizard-flagged branch must block @wipe of its leaf from a mortal owner, even under their own object");
	}

	/// <summary>
	/// Task 6 fix round 1, H1: <c>@wipe me/BRANCH</c> matches only BRANCH in <c>attrArr</c>
	/// (its descendants aren't separate pattern matches), and the outer ancestor-path gate on
	/// BRANCH alone passes it through when BRANCH itself carries no flag. The provider-level
	/// <c>WipeAttributeCommand</c>/<c>WipeAttributeAsync</c> then deletes BRANCH's whole
	/// descendant subtree unconditionally - so a <c>wizard</c>-flagged descendant several
	/// levels down, never itself named by the pattern, was destroyed ungated. PennMUSH's
	/// <c>atr_clear_children</c> walks descendants one at a time and skips any it can't write.
	/// </summary>
	[Test]
	public async ValueTask WipeOfUnflaggedBranch_PreservesProtectedDescendant()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeDesc");
		var obj = await Mediator.Send(new GetObjectNodeQuery(owner.DbRef));

		// WPROT{uid} itself carries no flag - only its WIZLEAF child does. The outer
		// ancestor-path gate on WPROT{uid} alone would pass; only per-descendant gating
		// inside the wipe itself can catch this.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WPROT{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WPROT{uid}`WIZLEAF me=protectedvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WPROT{uid}`OKLEAF me=removablevalue"));
		// God flags only the descendant leaf WIZARD - not the branch.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}/WPROT{uid}`WIZLEAF=WIZARD"));

		// Control: a wholly-unflagged branch is fully removed by @wipe, so a survivor below is
		// the protection actually working, not @wipe silently no-op'ing on the whole subtree.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WPROTOK{uid} me=okbranch"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&WPROTOK{uid}`LEAF me=okleaf"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@wipe me/WPROTOK{uid}"));
		var controlBranch = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WPROTOK{uid}",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(controlBranch.IsAttribute).IsFalse()
			.Because("a wholly-unflagged branch is fully removed by @wipe, proving the command still works end-to-end");

		// The mortal owner wipes the branch that has one protected descendant among its children.
		var wipeMessages = await MessagesWhile(owner.DbRef, () =>
			Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@wipe me/WPROT{uid}")).AsTask());

		var protectedLeaf = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WPROT{uid}`WIZLEAF",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(protectedLeaf.IsAttribute).IsTrue()
			.Because("a wizard-flagged descendant must survive @wipe of its unflagged parent branch, even for the branch's own mortal owner");
		await Assert.That(protectedLeaf.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("protectedvalue")
			.Because("the surviving descendant's value must be untouched, not merely left present under a different value");

		// The branch itself must also survive completely untouched - PennMUSH's real_atr_clr
		// leaves a blocked branch alone entirely (value included) rather than clearing its
		// value while keeping the node the way an ordinary "still has children" clear does.
		// Fix round 1 got this wrong: it called ClearAttributeCommand on the branch whenever
		// anything below it was denied, which blanks the value even though the wipe of that
		// branch was supposed to have been refused.
		var branchAfter = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WPROT{uid}",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(branchAfter.IsAttribute).IsTrue()
			.Because("the branch itself must survive a wipe that couldn't fully clear its subtree");
		await Assert.That(branchAfter.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("branchvalue")
			.Because("a denied wipe must not silently blank the branch's own value - that is data loss on an operation that was refused");

		var removableLeaf = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WPROT{uid}`OKLEAF",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(removableLeaf.IsAttribute).IsFalse()
			.Because("an unprotected sibling under the same branch must still be removed by the wipe");

		// The outcome must reach the player, not just the database - @wipe used to discard
		// ClearAttributeAsync's result entirely and always print a generic success line.
		// Reporting now happens via NotifyLocalized (Task 6 fix round 3), not the command's own
		// CallState, so it's asserted against what the owner was actually told.
		await Assert.That(wipeMessages).Contains(string.Format(
				ErrorMessages.Notifications.AttributeCannotBeWipedChildBlocked, $"WPROT{uid}"))
			.Because("a partially-blocked wipe must tell the player, matching PennMUSH's own AE_TREE message");

		// And PennMUSH's do_wipe always ALSO prints the final tally regardless: exactly one
		// attribute (OKLEAF) was actually removable and removed here.
		await Assert.That(wipeMessages).Contains(ErrorMessages.Notifications.OneAttributeWiped)
			.Because("the tally must still be reported even when part of the wipe was blocked");
	}

	/// <summary>
	/// Task 6 fix round 4: the pre-existing <c>attrArr.Length == 0</c> early return in
	/// <c>ClearAttributeAsync</c> fires before the whole tally/notify machinery round 3 added,
	/// so a pattern that matches nothing at all - e.g. a typo'd <c>@wipe obj/NOSUCHPATTERN</c> -
	/// used to print absolutely nothing. PennMUSH's <c>do_wipe</c> (<c>set.c:1567-1577</c>)
	/// always prints its tally, even when <c>atr_iter_get</c> matched zero attributes: "No
	/// attributes wiped.", not silence. This is the state a user hits most often (a typo), so
	/// silence here is worse than round 2's wrong-but-present generic success line.
	/// </summary>
	[Test]
	public async ValueTask WipeWithNoMatches_StillReportsZero()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeNoMatch");

		// No attribute named anything like this exists on owner - the pattern matches nothing.
		var messages = await MessagesWhile(owner.DbRef, () =>
			Parser.CommandParse(owner.Handle, ConnectionService,
				MModule.single($"@wipe me/NOSUCHPATTERN{uid}*")).AsTask());

		await Assert.That(messages).Contains(ErrorMessages.Notifications.NoAttributesWiped)
			.Because("a zero-match @wipe must still report the tally, not go completely silent");
	}

	/// <summary>
	/// PennMUSH's <c>wipe_helper</c> (<c>src/set.c:1503-1504</c>) opens with
	/// <c>if (wildcard(pattern) &amp;&amp; AF_Wizard(atr) &amp;&amp; !God(player)) return 0;</c> -
	/// "for added security, only God can modify wiz-only-modifiable attributes using this command
	/// and wildcards." <c>PermissionService.CanSet</c> returns true for ANY wizard
	/// (<c>PermissionService.cs:88</c>) before the <c>AF_WIZARD</c> test ever runs, so a non-God
	/// wizard's <c>@wipe someplayer/**</c> destroyed every wizard-flagged attribute Penn protects.
	/// </summary>
	[Test]
	public async ValueTask WipeWithWildcard_SparesWizardAttribute_ForNonGodWizard()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var wiz = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeWizGuard");
		var obj = await Mediator.Send(new GetObjectNodeQuery(wiz.DbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wiz.DbRef}=WIZARD"));

		await Parser.CommandParse(wiz.Handle, ConnectionService, MModule.single($"&WZ{uid}G me=wizvalue"));
		await Parser.CommandParse(wiz.Handle, ConnectionService, MModule.single($"&WZ{uid}OK me=okvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wiz.DbRef}/WZ{uid}G=WIZARD"));

		await Parser.CommandParse(wiz.Handle, ConnectionService, MModule.single($"@wipe me/WZ{uid}*"));

		// Control: the unflagged sibling matched the same wildcard and IS gone, so the survivor
		// below is the AF_WIZARD guard and not a @wipe that silently no-op'd on the whole pattern.
		var control = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WZ{uid}OK",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(control.IsAttribute).IsFalse()
			.Because("an unflagged attribute matched by the same wildcard must still be wiped");

		var survivor = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WZ{uid}G",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(survivor.IsAttribute).IsTrue()
			.Because("only God may wipe a wizard-flagged attribute through a wildcard pattern");
		await Assert.That(survivor.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("wizvalue")
			.Because("the spared attribute must be untouched, not merely present with a blanked value");
	}

	/// <summary>
	/// The other half of the same Penn guard: <c>wildcard(pattern)</c>
	/// (<c>hdrs/externs.h:529</c> -> <c>wildcard_count(s, 0) == -1</c>) is true only when the
	/// pattern contains an unescaped <c>*</c> or <c>?</c>. "Wiping a specific attr still works,
	/// though" (<c>set.c:1499-1502</c>), so a literal name must not be caught by the guard.
	/// Without this the fix for the wildcard case would over-deny.
	/// </summary>
	[Test]
	public async ValueTask WipeByLiteralName_StillWipesWizardAttribute_ForNonGodWizard()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var wiz = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeWizLiteral");
		var obj = await Mediator.Send(new GetObjectNodeQuery(wiz.DbRef));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wiz.DbRef}=WIZARD"));

		await Parser.CommandParse(wiz.Handle, ConnectionService, MModule.single($"&WL{uid}G me=wizvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wiz.DbRef}/WL{uid}G=WIZARD"));

		var before = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WL{uid}G",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(before.IsAttribute).IsTrue()
			.Because("the wizard-flagged attribute exists before the wipe");

		await Parser.CommandParse(wiz.Handle, ConnectionService, MModule.single($"@wipe me/WL{uid}G"));

		var after = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"WL{uid}G",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(after.IsAttribute).IsFalse()
			.Because("a literal (non-wildcard) @wipe of a wizard attribute is still allowed to a wizard");
	}

	/// <summary>
	/// PennMUSH's <c>real_atr_clr</c> (<c>src/attrib.c:1100-1104</c>) tests <c>AF_Safe</c> BEFORE
	/// <c>Can_Write_Attr</c> and returns the distinct <c>AE_SAFE</c> code, which
	/// <c>wipe_helper</c> reports as "Attribute %s is SAFE. Set it !SAFE to modify it."
	/// (<c>set.c:1507-1509</c>) rather than <c>AE_ERROR</c>'s "Unable to wipe attribute %s". The
	/// two wordings tell the player different things: one names the fix, the other does not.
	/// </summary>
	[Test]
	public async ValueTask WipeOfSafeAttribute_ReportsTheSafeWording()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WGWipeSafeMsg");
		var obj = await Mediator.Send(new GetObjectNodeQuery(owner.DbRef));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&SF{uid}S me=safevalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&SF{uid}OK me=okvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}/SF{uid}S=safe"));

		var messages = await MessagesWhile(owner.DbRef, () =>
			Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@wipe me/SF{uid}*")).AsTask());

		// Control: the unflagged sibling matched the same wildcard and was wiped, so the report
		// below belongs to a wipe that actually ran rather than one that matched nothing.
		await Assert.That(messages).Contains(ErrorMessages.Notifications.OneAttributeWiped)
			.Because("exactly one of the two matched attributes was removable and removed");

		await Assert.That(messages).Contains(string.Format(
				ErrorMessages.Notifications.AttributeIsSafeSetNotSafe, $"SF{uid}S"))
			.Because("a safe attribute must be reported with Penn's AE_SAFE wording, which names the remedy");

		await Assert.That(messages).DoesNotContain(string.Format(
				ErrorMessages.Notifications.UnableToWipeAttribute, $"SF{uid}S"))
			.Because("AE_SAFE and AE_ERROR are distinct outcomes - the safe case must not also report the generic one");

		var survivor = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, $"SF{uid}S",
			IAttributeService.AttributeMode.Read, false);
		await Assert.That(survivor.IsAttribute).IsTrue()
			.Because("a safe attribute must survive the wipe that reported it");
	}
}
