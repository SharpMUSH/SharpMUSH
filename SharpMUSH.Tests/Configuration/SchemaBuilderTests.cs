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
		return SchemaBuilder.BuildSchema();
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

	/// <summary>
	/// A property reports the default its own declaration gives it, independently of its siblings. This
	/// used to be all-or-nothing per category: defaults came from a default-constructed instance of the
	/// category record, so a record with any parameter lacking a default could not be constructed at all
	/// and every one of its properties reported null — including those declaring a default. Only NetOptions
	/// and WikiOptions gave every parameter one, so only they reported defaults.
	/// </summary>
	[Test]
	public async Task DefaultValue_ComesFromThePropertysOwnDeclaration()
	{
		var schema = BuildSchema();

		await Assert.That(schema.Properties["Net.MudName"].DefaultValue).IsEqualTo("SharpMUSH");
		await Assert.That(schema.Properties["Net.Port"].DefaultValue).IsEqualTo(4201u);

		// DebugOptions is the mixed case that used to report null for both: one parameter declares a
		// default, the other does not, and each now answers for itself.
		await Assert.That(schema.Properties["Debug.ParserPredictionMode"].DefaultValue).IsEqualTo(2);
		await Assert.That(schema.Properties["Debug.DebugSharpParser"].DefaultValue).IsNull();

		await Assert.That((bool?)schema.Properties["Database.AllowBrowserCode"].DefaultValue).IsFalse();

		// CommandOptions declares no defaults at all, so its properties still have none to report.
		await Assert.That(schema.Properties["Command.NoisyWhisper"].DefaultValue).IsNull();
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
