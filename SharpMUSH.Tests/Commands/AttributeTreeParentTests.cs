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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR`BAZ {childDbRef}=boom"));

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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO {parentDbRef}=wibble"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&FOO`BAR {parentDbRef}=gleep"));

		var beforeResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;
		await Assert.That(beforeResult.ToPlainText()).IsEqualTo("gleep")
			.Because("should inherit before no_inherit is set");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {parentDbRef}/FOO`BAR=no_inherit"));

		var fooResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO)")))?.Message!;
		var barResult = (await Parser.FunctionParse(MModule.single($"get({childDbRef}/FOO`BAR)")))?.Message!;

		await Assert.That(fooResult.ToPlainText()).IsEqualTo("wibble")
			.Because("FOO should still be inherited");
		await Assert.That(barResult.ToPlainText()).IsEqualTo("")
			.Because("no_inherit should block FOO`BAR inheritance");
	}

	/// <summary>
	/// lattrp() shows parent-inherited tree attributes.
	/// PennMUSH testatree.t: atree.lattrp.1-6
	/// </summary>
	[Test]
	public async ValueTask Lattrp_ShowsParentTreeAttributes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var parentDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"LatPar_{uid}");
		var childDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"LatChild_{uid}");
		var pfx = $"LP{uid}";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@parent {childDbRef}={parentDbRef}"));

		// Setup: tree`leaf on parent, tree on child (matches oracle setup)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{pfx}`LEAF {parentDbRef}=X"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{pfx} {childDbRef}=Y"));

		// lattrp.1: lattrp(child/pfx`) — direct children of pfx` from parent
		var r1 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}`)")))?. Message!.ToPlainText();
		await Assert.That(r1).IsEqualTo($"{pfx}`LEAF")
			.Because("lattrp(child/tree`) should show parent's tree`leaf");

		// lattrp.2: lattrp(child/pfx`**) — recursive under pfx` from parent
		var r2 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}`**)")))?. Message!.ToPlainText();
		await Assert.That(r2).IsEqualTo($"{pfx}`LEAF")
			.Because("lattrp(child/tree`**) should show parent's tree`leaf");

		// lattrp.3: lattrp(child/pfx**) — pfx and all descendants (child's own + parent's)
		var r3 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}**)")))?. Message!.ToPlainText();
		await Assert.That(r3).IsEqualTo($"{pfx} {pfx}`LEAF")
			.Because("lattrp(child/tree**) should show child's tree + parent's tree`leaf");

		// Now set tree`leaf on child too (oracle: &tree`leaf child=Z)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{pfx}`LEAF {childDbRef}=Z"));

		// lattrp.4: same as .1 but now local override exists
		var r4 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}`)")))?. Message!.ToPlainText();
		await Assert.That(r4).IsEqualTo($"{pfx}`LEAF")
			.Because("lattrp(child/tree`) should show local tree`leaf (overrides parent)");

		// lattrp.5: same as .2 with local override
		var r5 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}`**)")))?. Message!.ToPlainText();
		await Assert.That(r5).IsEqualTo($"{pfx}`LEAF")
			.Because("lattrp(child/tree`**) should show local tree`leaf");

		// lattrp.6: same as .3 with local override
		var r6 = (await Parser.FunctionParse(MModule.single($"lattrp({childDbRef}/{pfx}**)")))?. Message!.ToPlainText();
		await Assert.That(r6).IsEqualTo($"{pfx} {pfx}`LEAF")
			.Because("lattrp(child/tree**) should show tree + tree`leaf");
	}
}
