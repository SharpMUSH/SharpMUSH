using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for lattr(), lattr(obj/pattern), and wildcard attribute matching.
/// Based on PennMUSH testatree.t atree.lattr.* and atree.matching.* tests.
/// </summary>
public class AttributeTreeLattrTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task<string> Eval(string expr)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		return result?.Message?.ToPlainText() ?? "";
	}

	private async Task Cmd(string cmd)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(cmd));
	}

	/// <summary>
	/// lattr(obj/tree**) shows all tree-pattern attributes recursively.
	/// lattr(obj/tree`) shows only direct children of the tree branch.
	/// PennMUSH testatree.t: atree.lattr.1-5
	/// </summary>
	[Test]
	public async ValueTask Lattr_TreePatterns()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"LatrObj_{uid}");
		var d = $"#{dbRef.Number}";
		var pfx = $"LT{uid}";

		await Cmd($"&{pfx} {d}=X");
		await Cmd($"&{pfx}`LEAF {d}=Y");
		await Cmd($"&{pfx}`LEAF`DEEP {d}=Z");

		var allTree = await Eval($"lattr({d}/{pfx}**)");
		await Assert.That(allTree).Contains(pfx);
		await Assert.That(allTree).Contains($"{pfx}`LEAF");

		var directChildren = await Eval($"lattr({d}/{pfx}`)");
		await Assert.That(directChildren).Contains($"{pfx}`LEAF");
		await Assert.That(directChildren).DoesNotContain($"{pfx}`LEAF`DEEP");
	}

	/// <summary>
	/// lattr(obj/pattern) with glob wildcards.
	/// PennMUSH testatree.t: atree.matching.1-10
	/// </summary>
	[Test]
	public async ValueTask Lattr_WildcardPatterns()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"WildObj_{uid}");
		var d = $"#{dbRef.Number}";
		var pfx = $"WP{uid}";

		await Cmd($"&{pfx} {d}=1");
		await Cmd($"&{pfx}`BAR {d}=2");
		await Cmd($"&{pfx}`BAZ {d}=3");
		await Cmd($"&{pfx}D {d}=4");

		var star = await Eval($"lattr({d}/{pfx}*)");
		await Assert.That(star).Contains(pfx);

		var backtick = await Eval($"lattr({d}/{pfx}`)");
		await Assert.That(backtick).Contains($"{pfx}`BAR");
		await Assert.That(backtick).Contains($"{pfx}`BAZ");
		await Assert.That(backtick).DoesNotContain($"{pfx}D");
	}

	/// <summary>
	/// hasattr with tree attributes.
	/// </summary>
	[Test]
	public async ValueTask Hasattr_TreeAttributes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"HasObj_{uid}");
		var d = $"#{dbRef.Number}";
		var pfx = $"HA{uid}";

		await Cmd($"&{pfx} {d}=hello");
		await Cmd($"&{pfx}`SUB {d}=world");

		await Assert.That(await Eval($"hasattr({d},{pfx})")).IsEqualTo("1");
		await Assert.That(await Eval($"hasattr({d},{pfx}`SUB)")).IsEqualTo("1");
		await Assert.That(await Eval($"hasattr({d},{pfx}`NOPE)")).IsEqualTo("0");
	}

	/// <summary>
	/// @wipe obj/tree removes tree and all children.
	/// PennMUSH testatree.t: atree.basic.14-17
	/// </summary>
	[Test]
	public async ValueTask Wipe_RemovesTreeBranch()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"WipeObj_{uid}");
		var d = $"#{dbRef.Number}";
		var pfx = $"WI{uid}";

		await Cmd($"&{pfx} {d}=1");
		await Cmd($"&{pfx}`BAR {d}=2");
		await Cmd($"&{pfx}`BAR`BAZ {d}=3");
		await Cmd($"&{pfx}OTHER {d}=keep");

		await Cmd($"@wipe {d}/{pfx}");

		await Assert.That(await Eval($"hasattr({d},{pfx})")).IsEqualTo("0");
		await Assert.That(await Eval($"hasattr({d},{pfx}`BAR)")).IsEqualTo("0");
		await Assert.That(await Eval($"hasattr({d},{pfx}`BAR`BAZ)")).IsEqualTo("0");
		await Assert.That(await Eval($"get({d}/{pfx}OTHER)")).IsEqualTo("keep");
	}

	/// <summary>
	/// nattr() counts attributes on a fresh object.
	/// </summary>
	[Test]
	public async ValueTask Nattr_CountsTreeAttributes()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var dbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"NatObj_{uid}");
		var d = $"#{dbRef.Number}";
		var pfx = $"NA{uid}";

		await Cmd($"&{pfx} {d}=1");
		await Cmd($"&{pfx}`B {d}=2");
		await Cmd($"&{pfx}`C {d}=3");

		await Assert.That(await Eval($"nattr({d})")).IsEqualTo("1");
	}
}
