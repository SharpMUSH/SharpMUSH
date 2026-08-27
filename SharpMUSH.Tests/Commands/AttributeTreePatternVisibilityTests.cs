using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A mortal_dark branch must hide its leaves even when the pattern names only
/// the leaf, and a visual leaf under a non-visual branch must stay hidden.
/// The pre-existing MortalDark_HidesFromLattrForMortal passes only because
/// lattr(me/**) happens to pull the ancestor into the result set; a leaf-only
/// pattern never did, so the ancestor's flags were never consulted.
/// </summary>
public class AttributeTreePatternVisibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Runs <paramref name="expression"/> as the player behind <paramref name="handle"/>.
	/// FunctionParse always evaluates as the parser's bound executor (God), which would
	/// take the isPrivileged early-out, so every viewer-sensitive check goes through think.
	/// </summary>
	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MModule.single($"think {expression}"));
		return result?.Message?.ToPlainText() ?? string.Empty;
	}

	[Test]
	public async ValueTask MortalDarkBranch_HidesLeaf_WhenLattrPatternNamesOnlyTheLeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatDarkL");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PD{uid} {mortalDbRef}=branchvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PD{uid}`LEAF {mortalDbRef}=leafvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {mortalDbRef}/PD{uid}=mortal_dark"));

		// Control: a leaf-only pattern on an unflagged branch is listed, so a miss below
		// is the mortal_dark branch and not a broken pattern or a failed lookup.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PO{uid} {mortalDbRef}=openvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PO{uid}`LEAF {mortalDbRef}=openleaf"));
		var control = await Eval(mortal.Handle, $"lattr(me/PO{uid}`LEAF)");
		await Assert.That(control).Contains($"PO{uid}`LEAF")
			.Because("a leaf under an unflagged branch must still be listed");

		var result = await Eval(mortal.Handle, $"lattr(me/PD{uid}`LEAF)");
		await Assert.That(result).DoesNotContain($"PD{uid}`LEAF")
			.Because("a mortal_dark branch must hide its leaf even when the pattern names only the leaf");
	}

	[Test]
	public async ValueTask MortalDarkBranch_HidesLeaf_FromNattrLeafOnlyPattern()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatDarkN");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PN{uid} {mortalDbRef}=branchvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&PN{uid}`LEAF {mortalDbRef}=leafvalue"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {mortalDbRef}/PN{uid}=mortal_dark"));

		var beforeFlagged = await Eval(1, $"nattr({mortalDbRef}/PN{uid}`LEAF)");
		await Assert.That(beforeFlagged).IsEqualTo("1")
			.Because("God bypasses the walk and still counts the leaf");

		var result = await Eval(mortal.Handle, $"nattr(me/PN{uid}`LEAF)");
		await Assert.That(result).IsEqualTo("0")
			.Because("nattr must not count a leaf whose branch is mortal_dark");
	}

	[Test]
	public async ValueTask VisualLeaf_UnderNonVisualBranch_IsNotListedByLattr()
	{
		// Penn requires AF_VISUAL on EVERY level. The viewer must not own or control
		// the target, or CanExamine short-circuits and the All(IsVisual) grant branch
		// never executes - which is why no pre-existing test exercises it at all.
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatVisOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatVisViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PV{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PV{uid}`LEAF me=leafvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PVOK{uid} me=okvalue"));
		// Leaf is visual; its branch deliberately is not. The top-level control is visual.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PV{uid}`LEAF=visual"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PVOK{uid}=visual"));

		// Control: proves the viewer can reach the target and that a visual top-level
		// attribute is granted, so the miss below is the branch and not a locate failure.
		var control = await Eval(viewer.Handle, $"lattr({ownerDbRef}/PVOK{uid})");
		await Assert.That(control).Contains($"PVOK{uid}")
			.Because("a visual top-level attribute is readable by a non-owner");

		var result = await Eval(viewer.Handle, $"lattr({ownerDbRef}/PV{uid}`LEAF)");
		await Assert.That(result).DoesNotContain($"PV{uid}`LEAF")
			.Because("visual on the leaf alone does not grant access - every level must be visual");
	}

	/// <summary>
	/// Characterisation: get() already resolves the whole path through
	/// GetAttributeWithInheritanceQuery, so it enforces the every-level rule today.
	/// Pinned here so the pattern path and the direct path cannot drift apart.
	/// </summary>
	[Test]
	public async ValueTask VisualLeaf_UnderNonVisualBranch_IsNotReadableByGet()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatGetOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatGetViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PG{uid} me=branchvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PG{uid}`LEAF me=leafvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PGOK{uid} me=okvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PG{uid}`LEAF=visual"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PGOK{uid}=visual"));

		var control = await Eval(viewer.Handle, $"get({ownerDbRef}/PGOK{uid})");
		await Assert.That(control).IsEqualTo("okvalue")
			.Because("a visual top-level attribute is readable by a non-owner");

		var result = await Eval(viewer.Handle, $"get({ownerDbRef}/PG{uid}`LEAF)");
		await Assert.That(result).DoesNotContain("leafvalue")
			.Because("visual on the leaf alone does not grant access - every level must be visual");
	}

	/// <summary>
	/// Penn's read gate (attrib.c can_read_attr_internal) tests AF_VISUAL alone. AF_PUBLIC is
	/// a distinct flag that overrides SAFER_UFUN for evaluation, unrelated to reading. Before
	/// the fix, IsVisual() conflated "visual" and "public", so a public-only attribute was
	/// wrongly readable by a viewer who cannot examine the object.
	/// </summary>
	[Test]
	public async ValueTask PublicAlone_DoesNotGrantRead()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatPubOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatPubViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PBOK{uid} me=okvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PBOK{uid}=visual"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PB{uid} me=pubvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PB{uid}=public"));

		// Control: a visual (not public) attribute is readable by a non-owner, so the miss
		// below is the public/visual distinction and not a locate or CanExamine failure.
		var control = await Eval(viewer.Handle, $"get({ownerDbRef}/PBOK{uid})");
		await Assert.That(control).IsEqualTo("okvalue")
			.Because("a visual attribute is readable by a non-owner");

		var result = await Eval(viewer.Handle, $"get({ownerDbRef}/PB{uid})");
		await Assert.That(result).DoesNotContain("pubvalue")
			.Because("public alone must not grant read - Penn's read gate tests AF_VISUAL only");
	}

	/// <summary>
	/// Penn's can_read_attr_internal (attrib.c:305-310) ANDs the AF_VISUAL grant with
	/// <c>!AF_Nearby(atr) || canlook</c>: a nearby-flagged attribute's visual grant only
	/// applies when the viewer could look at the object (same room, or one location away
	/// through a non-opaque room, or Long_Fingers). IsNearby had zero callers before this.
	/// </summary>
	[Test]
	public async ValueTask NearbyVisualAttribute_IsHiddenFromRemoteViewer()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatNearOwner");
		var viewer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatNearViewer");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PN{uid} me=nearbyvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PN{uid}=visual nearby"));

		// Control: both players start in the same room, so the attribute is readable while
		// nearby - a miss here would mean the test never reached the nearby gate at all.
		var control = await Eval(viewer.Handle, $"get({ownerDbRef}/PN{uid})");
		await Assert.That(control).IsEqualTo("nearbyvalue")
			.Because("visual+nearby is readable while the viewer is nearby the owner");

		var roomName = TestIsolationHelpers.GenerateUniqueName("PatNearRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();
		// /QUIET: a plain @teleport queues a "look" command for the target (GeneralCommands.cs's
		// Teleport, QueueCommandListRequest) rather than running it inline, so its arrival autolook
		// can land at an unpredictable later tick - inside some OTHER, unrelated test's notification
		// capture window, since the shared NotifyService substitute is session-wide. /QUIET skips
		// that queued look entirely.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@teleport/quiet {viewer.DbRef}={roomDbRef}"));

		// The room dbref is scraped out of @dig's message text: if that wording ever changes,
		// the @teleport above silently does nothing and the negative assertion below passes
		// because the viewer never moved, not because the nearby gate fired. This is the only
		// test covering that gate, so prove the move happened first.
		var viewerLocation = await Eval(viewer.Handle, "loc(me)");
		await Assert.That(viewerLocation).IsEqualTo(roomDbRef)
			.Because("the nearby assertion is vacuous unless the viewer actually left the owner's room");

		var result = await Eval(viewer.Handle, $"get({ownerDbRef}/PN{uid})");
		await Assert.That(result).DoesNotContain("nearbyvalue")
			.Because("nearby overrides visual once the viewer is no longer nearby the owner");
	}

	/// <summary>
	/// PennMUSH's <c>Can_Read_Attr</c> macro (<c>hdrs/mushdb.h:100-101</c>) reads
	/// <c>!AF_Internal(a) &amp;&amp; (See_All(p) || can_read_attr_internal(...))</c>: the
	/// <c>AF_Internal</c> check happens BEFORE the <c>See_All(p) ||</c> easy-out that lets a
	/// wizard skip the rest of the gate. Unlike <c>mortal_dark</c> (which lives entirely inside
	/// the See_All-bypassed <c>can_read_attr_internal</c>), there is no privileged escape from
	/// <c>internal</c> - a wizard viewing an object it neither owns nor controls must still be
	/// denied.
	/// </summary>
	[Test]
	public async ValueTask InternalBranch_HidesLeafFromEveryone()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatIntOwner");
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatIntWiz");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wizard.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PI{uid} me=leafvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PI{uid}=internal"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PIOK{uid} me=okvalue"));

		// Control: a wizard's CanExamine short-circuit (IsSee_All) grants read of ANY
		// non-internal attribute regardless of ownership, so the miss below is the internal
		// flag itself and not a locate failure or a wizard who somehow lacks the usual escape.
		var control = await Eval(wizard.Handle, $"get({ownerDbRef}/PIOK{uid})");
		await Assert.That(control).IsEqualTo("okvalue")
			.Because("a wizard can read any non-internal attribute on an object it doesn't own");

		var result = await Eval(wizard.Handle, $"get({ownerDbRef}/PI{uid})");
		await Assert.That(result).DoesNotContain("leafvalue")
			.Because("AF_INTERNAL denies reads to everyone, even wizards - there is no See_All easy-out for it");
	}

	/// <summary>
	/// Guards Task 5's planned rewrite of <c>CanSet</c> (explicit per-flag ancestor tests): if
	/// that rewrite drops the internal write-denial, this test catches it. PennMUSH's
	/// <c>Cannot_Write_This_Attr</c> (<c>src/attrib.c:364</c>) reads
	/// <c>!God(p) &amp;&amp; (AF_Internal(a) || ...)</c> - only God bypasses; a wizard does not.
	/// </summary>
	[Test]
	public async ValueTask InternalBranch_BlocksWritingALeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatIntWOwner");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {owner.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PIW{uid} me=original"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PIW{uid}=internal"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PIWOK{uid} me=original"));

		// Control: the wizard owner can overwrite its own unflagged sibling attribute, so a
		// no-op on the internal one below is the flag denial, not a Controls/locate failure.
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me=PIWOK{uid}:changed"));
		var control = await Eval(owner.Handle, $"get(me/PIWOK{uid})");
		await Assert.That(control).IsEqualTo("changed")
			.Because("a wizard owner can overwrite its own unflagged attribute");

		// The internal attribute's own read is ALSO denied (even to this wizard owner - see
		// InternalBranch_HidesLeafFromEveryone), so a post-write get() can't distinguish "write
		// was blocked" from "write succeeded but read is blocked too". Assert directly on the
		// @set command's own result instead.
		var attempt = await Parser.CommandParse(owner.Handle, ConnectionService,
			MModule.single($"@set me=PIW{uid}:changed"));
		await Assert.That(attempt.Message?.ToPlainText() ?? string.Empty).Contains("NO PERMISSION")
			.Because("AF_INTERNAL blocks writes for everyone but God - a wizard owner must not be able to overwrite it");
	}

	/// <summary>
	/// <c>GetAttributePatternAsync</c>'s privileged early-out returned every match unfiltered.
	/// <c>Can_Read_Attr</c> (<c>hdrs/mushdb.h:100-101</c>) tests <c>!AF_Internal(a)</c> BEFORE the
	/// <c>See_All(p) ||</c> easy-out, so the one denial a wizard cannot skip is exactly the one
	/// that early-out skipped: <c>lattr</c>/<c>@decompile</c> as a wizard listed internal
	/// attributes. (<c>See_All</c> does short-circuit the rest of <c>can_read_attr_internal</c>,
	/// so <c>mortal_dark</c> and the ancestor walk stay bypassed for a wizard - only the leaf's
	/// own <c>internal</c> flag survives the easy-out.)
	/// </summary>
	[Test]
	public async ValueTask InternalAttribute_IsNotListedByLattr_EvenForAWizard()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatLIntOwner");
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PatLIntWiz");
		var ownerDbRef = owner.DbRef.ToString();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {wizard.DbRef}=WIZARD"));

		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PLIA{uid} me=secretvalue"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"@set me/PLIA{uid}=internal"));
		await Parser.CommandParse(owner.Handle, ConnectionService, MModule.single($"&PLIB{uid} me=okvalue"));

		var result = await Eval(wizard.Handle, $"lattr({ownerDbRef}/PLI*{uid})");

		// Control: the unflagged sibling matched the same wildcard and IS listed, so the miss
		// below is the internal flag and not a locate failure or a pattern that matched nothing.
		await Assert.That(result).Contains($"PLIB{uid}")
			.Because("a wizard lists an unflagged attribute on an object it doesn't own");

		await Assert.That(result).DoesNotContain($"PLIA{uid}")
			.Because("AF_INTERNAL is tested before See_All, so even a wizard must not see an internal attribute listed");
	}
}
