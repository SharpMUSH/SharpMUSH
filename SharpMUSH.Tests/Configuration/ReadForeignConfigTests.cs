using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A TinyMUX <c>netmux.conf</c> or RhostMUSH <c>netrhost.conf</c> names some options differently from PennMUSH. Read
/// as that server's file, each is the SharpMUSH option it means; read as a <c>mush.cnf</c>, none of them is.
/// </summary>
public class ReadForeignConfigTests
{
	private static readonly string[] Lines =
	[
		"mud_name Foreign",
		"player_starting_room 12",
		"money_name_singular Credit",
		"money_name_plural Credits",
		"dig_cost 25",
		"quotas yes",
		"room_parent 40",
		"player_parent -1",
		"conn_timeout 120",
		"parent_recursion_limit 7",
		"sql_server db.example",
		"space_compress no"
	];

	[Test]
	public async Task TinyMuxNamesReadAsTheirOptions()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX);
		var options = import.Options;

		await Assert.That(options.Net.MudName).IsEqualTo("Foreign");
		await Assert.That(options.Database.PlayerStart).IsEqualTo(12u);
		await Assert.That(options.Cosmetic.MoneySingular).IsEqualTo("Credit");
		await Assert.That(options.Cosmetic.MoneyPlural).IsEqualTo("Credits");
		await Assert.That(options.Cost.RoomCost).IsEqualTo(25u);
		await Assert.That(options.Limit.UseQuota).IsTrue();
		await Assert.That(options.Database.AncestorRoom).IsEqualTo(40u);
		await Assert.That(options.Database.AncestorPlayer).IsNull();
		await Assert.That(options.Limit.UnconnectedIdleTimeout).IsEqualTo(120u);
		await Assert.That(options.Limit.MaxParents).IsEqualTo(7u);
		await Assert.That(options.Net.SqlHost).IsEqualTo("db.example");
		await Assert.That(options.Compatibility.SpaceCompress).IsFalse();
		await Assert.That(import.Unrecognized).IsEmpty();
	}

	[Test]
	public async Task RhostMushSharesTheTinyMushNamesButNotTinyMuxsOwn()
	{
		var import = await ImportAsync(ConfigurationDialect.RhostMUSH);

		await Assert.That(import.Options.Database.PlayerStart).IsEqualTo(12u);
		await Assert.That(import.Options.Cost.RoomCost).IsEqualTo(25u);
		await Assert.That(import.Options.Compatibility.SpaceCompress).IsFalse();
		await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "parent_recursion_limit", "sql_server" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AMushCnfDoesNotReadThem()
	{
		var import = await ImportAsync(ConfigurationDialect.PennMUSH);

		await Assert.That(import.Options.Cost.RoomCost).IsEqualTo(SharpMUSH.Configuration.Options.SharpMUSHOptions.Default().Cost.RoomCost);
		await Assert.That(import.Unrecognized).Contains("dig_cost");
	}

	/// <summary>
	/// RhostMUSH names its reality levels and the masks of an object with none of its own in <c>netrhost.conf</c>, in
	/// decimal or hexadecimal; a level with no usable mask takes the first bit, and a repeated name keeps the first.
	/// </summary>
	[Test]
	public async Task RhostMushRealityLevelsAreRead()
	{
		var import = await ImportAsync(ConfigurationDialect.RhostMUSH,
			"reality_level Real 0x00000001",
			"reality_level Umbra 2 UMBRADESC",
			"reality_level Odd nonsense",
			"reality_level Umbra 8",
			"reality_level All 0xFFFFFFFF",
			"def_player_rx 3",
			"def_room_tx -1");

		await Assert.That(import.Reality.Levels).IsEquivalentTo(new[]
		{
			new RealityLevelDirective("Real", 1, "DESC"),
			new RealityLevelDirective("Umbra", 2, "UMBRADESC"),
			new RealityLevelDirective("Odd", 1, "DESC"),
			new RealityLevelDirective("All", uint.MaxValue, "DESC")
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(import.Reality.Default("player", "rx")).IsEqualTo(3u);
		await Assert.That(import.Reality.Default("player", "tx")).IsEqualTo(1u);
		await Assert.That(import.Reality.Default("room", "tx")).IsEqualTo(uint.MaxValue);
		await Assert.That(import.Unrecognized).DoesNotContain("reality_level").And.DoesNotContain("def_player_rx");

		var penn = await ImportAsync(ConfigurationDialect.PennMUSH, "reality_level Real 1");
		await Assert.That(penn.Reality.Levels).IsEmpty();
	}

	/// <summary>TinyMUX's netmux.conf names its reality levels and default masks with the same directives.</summary>
	[Test]
	public async Task TinyMuxRealityLevelsAreRead()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX,
			"reality_level Real 1",
			"reality_level Ghost 2 GHOSTDESC",
			"def_exit_rx 3");

		await Assert.That(import.Reality.Levels).IsEquivalentTo(new[]
		{
			new RealityLevelDirective("Real", 1, "DESC"),
			new RealityLevelDirective("Ghost", 2, "GHOSTDESC")
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(import.Reality.Default("exit", "rx")).IsEqualTo(3u);
		await Assert.That(import.Reality.Default("room", "tx")).IsEqualTo(uint.MaxValue);
	}

	/// <summary>
	/// netmux.conf turns command hooks on at startup: hook_obj names the object, each hook_cmd line a command and its
	/// hooks, and global_error_obj the object standing in for Huh?. None of them is reported as unrecognized.
	/// </summary>
	[Test]
	public async Task TinyMuxHooksAreRead()
	{
		var import = await ImportAsync(ConfigurationDialect.TinyMUX,
			"hook_obj #12",
			"hook_cmd look before after",
			"hook_cmd @pemit permit",
			"hook_cmd LOOK ignore !before",
			"hook_cmd @emit before",
			"hook_cmd @emit !before",
			"global_error_obj 15");

		await Assert.That(import.Hooks.HookObject).IsEqualTo(12);
		await Assert.That(import.Hooks.GlobalErrorObject).IsEqualTo(15);
		await Assert.That(import.Hooks.Commands.Count).IsEqualTo(2);
		await Assert.That(import.Hooks.Commands[0].Command).IsEqualTo("look");
		await Assert.That(import.Hooks.Commands[0].Hooks).IsEquivalentTo(["after", "ignore"]);
		await Assert.That(import.Hooks.Commands[1].Hooks).IsEquivalentTo(["permit"]);
		await Assert.That(import.Unrecognized).DoesNotContain("hook_cmd").And.DoesNotContain("hook_obj")
			.And.DoesNotContain("global_error_obj");

		var penn = await ImportAsync(ConfigurationDialect.PennMUSH, "hook_obj #12");
		await Assert.That(penn.Hooks.Any).IsFalse();
	}

	private static async Task<PennMushConfigImport> ImportAsync(ConfigurationDialect dialect, params string[] more)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-conf-{Guid.NewGuid():N}.conf");
		await File.WriteAllLinesAsync(path, [.. Lines, .. more]);
		try
		{
			return ReadPennMushConfig.Import(path, followIncludes: false, dialect);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
