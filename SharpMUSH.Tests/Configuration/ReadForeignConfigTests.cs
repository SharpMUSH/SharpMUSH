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
		"space_compress yes"
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
		await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "space_compress" });
	}

	[Test]
	public async Task RhostMushSharesTheTinyMushNamesButNotTinyMuxsOwn()
	{
		var import = await ImportAsync(ConfigurationDialect.RhostMUSH);

		await Assert.That(import.Options.Database.PlayerStart).IsEqualTo(12u);
		await Assert.That(import.Options.Cost.RoomCost).IsEqualTo(25u);
		await Assert.That(import.Unrecognized).IsEquivalentTo(new[] { "parent_recursion_limit", "sql_server", "space_compress" },
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AMushCnfDoesNotReadThem()
	{
		var import = await ImportAsync(ConfigurationDialect.PennMUSH);

		await Assert.That(import.Options.Cost.RoomCost).IsEqualTo(SharpMUSH.Configuration.Options.SharpMUSHOptions.Default().Cost.RoomCost);
		await Assert.That(import.Unrecognized).Contains("dig_cost");
	}

	private static async Task<PennMushConfigImport> ImportAsync(ConfigurationDialect dialect)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-conf-{Guid.NewGuid():N}.conf");
		await File.WriteAllLinesAsync(path, Lines);
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
