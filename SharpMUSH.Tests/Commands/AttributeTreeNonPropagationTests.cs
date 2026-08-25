using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Pins the attribute flags that deliberately do <b>not</b> propagate down attribute trees in
/// SharpMUSH, even though PennMUSH's own help text says they do. Without these tests, a future
/// contributor reading <c>help attribute flags</c> (or SharpMUSH's <c>sharpattr.md</c>, which
/// documents the same discrepancy) would "fix" this into a bug.
/// <para>
/// Penn's help is self-contradictory here: the top "ATTRIBUTE FLAGS" list states that
/// <c>no_clone</c>, <c>veiled</c>, and <c>wizard</c> "restrict access, and are inherited down
/// attribute trees," while "ATTRIBUTE TREES3" narrows the propagating set to <c>no_inherit</c>,
/// <c>no_command</c>, <c>mortal_dark</c>, <c>wizard</c> only - omitting <c>no_clone</c> and
/// <c>veiled</c>. The actual Penn source settles it in both directions: <c>atr_cpy</c>
/// (<c>src/attrib.c:1691-1709</c>) tests <c>AF_Nocopy</c> per-attribute only, no tree walk exists
/// for <c>AF_VEILED</c> anywhere, and <c>can_read_attr_internal</c> (<c>src/attrib.c:282-338</c>)
/// never tests <c>AF_Wizard</c> at all - it gates writes only. SharpMUSH matches the code, not
/// the help text, for all three.
/// </para>
/// <para>
/// There is deliberately no test here for <c>no_clone</c>. <c>IsNoCopy()</c>
/// (<c>SharpAttributeExtensions.cs:29</c>) has zero production callers, and <c>@CLONE</c>
/// (<c>BuildingCommands.cs:1445-1452</c>) iterates only <c>obj.Object().Attributes.Value</c>
/// (<c>GetTopLevelAttributesAsync</c>, depth 1) - it never walks into attribute-tree branches at
/// all, for any object. A leaf beneath any branch is never copied by <c>@CLONE</c> today,
/// regardless of flags. A test asserting "a <c>no_clone</c> branch doesn't prevent cloning a
/// leaf" would therefore pass, but vacuously - the leaf was never going to be cloned either way,
/// so the test would not actually be exercising (and could never catch a regression in) tree
/// propagation of <c>no_clone</c> specifically. <c>@CLONE</c>'s tree-blindness is a real, separate
/// parity gap; it belongs in its own fix, not folded into this one as a side effect of making this
/// test pass.
/// </para>
/// </summary>
[NotInParallel]
public class AttributeTreeNonPropagationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	/// <summary>
	/// AF_WIZARD is absent from Penn's <c>can_read_attr_internal</c> (<c>src/attrib.c:282-338</c>,
	/// both the top-level check and the branch-walk loop) - it gates writes only, via
	/// <c>can_write_attr_internal</c>/<c>Cannot_Write_This_Attr</c>. A mortal reading a leaf
	/// beneath a wizard-flagged branch must still see its value.
	/// </summary>
	[Test]
	public async ValueTask WizardBranch_DoesNotBlockReadingALeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "NPWizRead");
		var mortalDbRef = mortal.DbRef.ToString();

		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&WR{uid} me=branchvalue_{uid}"));
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&WR{uid}`LEAF me=leafvalue_{uid}"));

		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@set {mortalDbRef}/WR{uid}=wizard"));

		// Positive control: wizard on the branch is live and still blocks the mortal owner's
		// own write to the leaf beneath it (the write gate this branch already implements) -
		// so this is not a Controls failure, a broken backtick path, or a flag that silently
		// failed to apply.
		await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"&WR{uid}`LEAF me=changed_{uid}"));
		var writeCheck = await Parser.FunctionParse(MModule.single($"get({mortalDbRef}/WR{uid}`LEAF)"));
		await Assert.That(writeCheck!.Message!.ToPlainText()).IsEqualTo($"leafvalue_{uid}")
			.Because("wizard on the branch must still block the mortal owner's write to its leaf - proves the flag was actually set and is live");

		// The actual claim: reading that same leaf must not be blocked. Uses "think" through
		// the mortal's own connection handle, not FunctionParse, because FunctionParse always
		// evaluates as the parser's bound (privileged) executor and would take an early-out
		// that makes the read trivially succeed regardless of the flag under test.
		var readResult = await Parser.CommandParse(mortal.Handle, ConnectionService,
			MModule.single($"think get(me/WR{uid}`LEAF)"));
		await Assert.That(readResult.Message!.ToPlainText()).IsEqualTo($"leafvalue_{uid}")
			.Because("AF_WIZARD gates writes only, per Penn's can_read_attr_internal - it must not block reading the leaf");
	}

	/// <summary>
	/// Penn's only live use of <c>AF_VEILED</c> is cosmetic: <c>examine_helper_veiled</c>
	/// (<c>src/look.c:302-316</c>) hides an attribute's value in <c>examine</c>'s default listing,
	/// but does not gate <c>get()</c>/<c>eval()</c> at all, and no ancestor walk anywhere tests it.
	/// SharpMUSH's equivalent gate lives in <c>@examine</c>'s attribute loop
	/// (<c>GeneralCommands.cs:1115-1116</c>): a flat, per-attribute check of the attribute's own
	/// flags, with no ancestor lookup. A branch's veiled flag must suppress that branch's own
	/// value in the listing, but must not suppress an unflagged leaf beneath it.
	/// </summary>
	[Test]
	public async ValueTask VeiledBranch_DoesNotVeilALeaf()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NPVeil");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&VB{uid} {obj}=branchvalue_{uid}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&VB{uid}`LEAF {obj}=leafvalue_{uid}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/VB{uid}=veiled"));

		NotifyService.ClearReceivedCalls();
		// VB{uid}** (no backtick) matches the branch itself plus every descendant, so both rows
		// are candidates for this single examine call.
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/VB{uid}**"));

		// Positive control: the veiled branch's own value must be suppressed. Proves the flag
		// was actually set and this examine call genuinely exercises the veiled gate, rather than
		// the pattern silently matching nothing or the flag failing to apply.
		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, $"branchvalue_{uid}")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());

		// The actual claim: the unflagged leaf beneath the veiled branch must still show its
		// value. If a future change added tree-propagation for veiled (matching Penn's help
		// text instead of Penn's code), this would start failing.
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextContains(m, $"leafvalue_{uid}")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}
}
