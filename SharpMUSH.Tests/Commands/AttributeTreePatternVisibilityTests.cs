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
}
