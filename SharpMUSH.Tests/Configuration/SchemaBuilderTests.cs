using SharpMUSH.Configuration;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Verifies that <see cref="SchemaBuilder"/> classifies collection-typed configuration
/// properties (string arrays and string-array dictionaries) with dedicated UI components,
/// so the config pages render editable element lists instead of a bare "string[]" type name.
/// </summary>
public class SchemaBuilderTests
{
	private static ConfigurationSchema BuildSchema()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);
		return SchemaBuilder.BuildSchema(options);
	}

	[Test]
	public async Task StringArrayProperty_UsesStringListComponent()
	{
		var schema = BuildSchema();

		var playerFlags = schema.Properties["Flag.PlayerFlags"];

		await Assert.That(playerFlags.Type).IsEqualTo("array");
		await Assert.That(playerFlags.Component).IsEqualTo("stringlist");
	}

	[Test]
	public async Task StringArrayDictionaryProperty_UsesDictionaryComponent()
	{
		var schema = BuildSchema();

		var functionAliases = schema.Properties["Alias.FunctionAliases"];

		await Assert.That(functionAliases.Type).IsEqualTo("dictionary");
		await Assert.That(functionAliases.Component).IsEqualTo("dictionary");
	}

	[Test]
	public async Task ScalarProperties_KeepTheirExistingComponents()
	{
		var schema = BuildSchema();

		await Assert.That(schema.Properties["Cosmetic.AnnounceConnects"].Component).IsEqualTo("switch");
		await Assert.That(schema.Properties["Cosmetic.MoneySingular"].Component).IsEqualTo("text");
	}

	[Test]
	public async Task CategoryGroups_OrderedByFirstPropertyOrder_ThenEncounterOrder()
	{
		var schema = BuildSchema();

		var netGroups = string.Join("|", schema.Categories.First(c => c.Name == "Net").Groups.Select(g => g.Name));

		// First-property Orders in NetOptions: General=1, Database=1, Advanced=1,
		// Connection Settings=4, Network Protocol=4, Connection Limits=4.
		// Primary sort is that Order; ties keep property declaration order.
		await Assert.That(netGroups)
			.IsEqualTo("General|Database|Advanced|Connection Settings|Network Protocol|Connection Limits");
	}

	[Test]
	public async Task CategoryGroups_AllTiedOrders_KeepDeclarationOrder()
	{
		var schema = BuildSchema();

		var costGroups = string.Join("|", schema.Categories.First(c => c.Name == "Cost").Groups.Select(g => g.Name));

		await Assert.That(costGroups).IsEqualTo("Building Costs|Command Costs");
	}

	[Test]
	[Arguments("player_flags", "Player Flags")]
	[Arguments("_port", "Port")]
	[Arguments("port_", "Port")]
	[Arguments("port__name", "Port Name")]
	public async Task FormatPropertyDisplayName_HandlesUnderscoreEdgeCases(string input, string expected)
	{
		var method = typeof(SchemaBuilder).GetMethod(
			"FormatPropertyDisplayName",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

		var result = (string?)method.Invoke(null, [input]);

		await Assert.That(result).IsEqualTo(expected);
	}
}
