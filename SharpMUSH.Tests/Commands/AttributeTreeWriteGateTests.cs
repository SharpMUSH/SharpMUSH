using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Completes the write gate in <c>PermissionService.CanSet</c>: the <c>safe</c> attribute flag
/// (PennMUSH's <c>Cannot_Write_This_Attr</c>, <c>src/attrib.c:364-368</c>) and create-time
/// <c>nodump</c> (<c>can_create_attr</c>, <c>src/attrib.c:479-483</c>). Both flags previously had
/// zero effect on <c>CanSet</c> - the TODO comment this file replaces said as much.
/// </summary>
public class AttributeTreeWriteGateTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

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
}
