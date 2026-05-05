using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for attribute tree inheritance through @parent and ancestor objects.
/// Ported from PennMUSH testatree.t: atree.parent.* tests.
/// </summary>
public class AttributeTreeParentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Child inherits attribute values from parent via get().
	/// PennMUSH testatree.t: atree.parent.14-17
	/// </summary>
	[Test]
	public async ValueTask Parent_ChildInheritsAttributeFromParent()
	{
		var parentDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InhPar");
		var childDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InhChild");

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Set attributes on parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

		// Child should inherit
		var fooResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO)")))?.Message!;
		var barResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;

		await Assert.That(fooResult.ToPlainText()).IsEqualTo("wibble")
			.Because("child should inherit FOO from parent");
		await Assert.That(barResult.ToPlainText()).IsEqualTo("gleep")
			.Because("child should inherit FOO`BAR from parent");
	}

	/// <summary>
	/// Setting a leaf on child should shadow parent's entire tree branch.
	/// PennMUSH testatree.t: atree.parent.18-20
	/// When child sets FOO`BAR`BAZ, get(child/FOO) and get(child/FOO`BAR) should be empty
	/// because the child now owns that branch.
	/// </summary>
	[Test]
	public async ValueTask Parent_ChildLeafShadowsParentBranch()
	{
		var parentDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ShadPar");
		var childDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ShadChild");

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Set tree on parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

		// Set a deeper leaf on child — this shadows the entire parent branch
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {childDbRef}=boom"));

		// Child's get(FOO) and get(FOO`BAR) should now be empty (shadowed)
		var fooResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO)")))?.Message!;
		var barResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;

		await Assert.That(fooResult.ToPlainText()).IsEqualTo("")
			.Because("child owning a subtree should shadow parent's branch value");
		await Assert.That(barResult.ToPlainText()).IsEqualTo("")
			.Because("child owning a subtree should shadow parent's intermediate value");
	}

	/// <summary>
	/// @set parent/attr=no_inherit stops inheritance of that attribute.
	/// PennMUSH testatree.t: atree.parent.22-26
	/// KNOWN GAP: no_inherit attribute flag not respected by GetAttributeAsync parent traversal.
	/// HIGH PRIORITY — this is a critical PennMUSH compatibility feature.
	/// </summary>
	[Test]
	public async ValueTask Parent_NoInheritStopsInheritance()
	{
		var parentDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NoInhPar");
		var childDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NoInhChild");

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Set tree on parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

		// Verify inheritance works before setting no_inherit
		var beforeResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;
		await Assert.That(beforeResult.ToPlainText()).IsEqualTo("gleep")
			.Because("should inherit before no_inherit is set");

		// Set no_inherit on foo`bar
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentDbRef}/FOO`BAR=no_inherit"));

		// Child should still get FOO but not FOO`BAR
		var fooResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO)")))?.Message!;
		var barResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;

		await Assert.That(fooResult.ToPlainText()).IsEqualTo("wibble")
			.Because("FOO should still be inherited");
		await Assert.That(barResult.ToPlainText()).IsEqualTo("")
			.Because("no_inherit should block FOO`BAR inheritance");
	}

	/// <summary>
	/// lattr(child/tree`) shows parent's tree attributes via lattrp behavior.
	/// PennMUSH testatree.t: atree.lattrp.1-3
	/// KNOWN GAP: lattrp() parent tree traversal not implemented.
	/// HIGH PRIORITY — this is a critical PennMUSH compatibility feature.
	/// </summary>
	[Test]
	public async ValueTask Lattrp_ShowsParentTreeAttributes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parentDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"LatPar_{uid}");
		var childDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"LatChild_{uid}");
		var pfx = $"LP{uid}";

		// Set parent
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Set tree leaf on parent, tree root on child
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{pfx}`LEAF {parentDbRef}=X"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{pfx} {childDbRef}=Y"));

		// Verify parent link works
		var parentCheck = (await Parser.FunctionParse(MModule.single($"parent({childDbRef})")))?.Message!.ToPlainText();
		await Assert.That(parentCheck).IsEqualTo(parentDbRef.ToString())
			.Because("@parent should be set");

		// Verify the attr exists on parent directly
		var parentAttr = (await Parser.FunctionParse(MModule.single($"get({parentDbRef}/{pfx}`LEAF)")))?.Message!.ToPlainText();
		await Assert.That(parentAttr).IsEqualTo("X")
			.Because("parent should have the tree leaf attr");

		// lattrp(child/pfx`) should show pfx`LEAF from parent
		var result = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}`)")))?.Message!.ToPlainText();
		await Assert.That(result).Contains($"{pfx}`LEAF")
			.Because("lattrp should show parent's tree leaves");
	}
}
