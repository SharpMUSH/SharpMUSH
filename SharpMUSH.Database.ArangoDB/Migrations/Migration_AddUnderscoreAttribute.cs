using Core.Arango;
using Core.Arango.Migration;
using SharpMUSH.Database;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the <c>_</c> standard attribute definition with the <c>veiled</c> flag.
/// The underscore attribute serves as a conventional scratch/anonymous attribute;
/// marking it veiled hides its value from casual examination.
/// </summary>
public class Migration_AddUnderscoreAttribute : IArangoMigration
{
	public long Id => 20260601_001;

	public string Name => "add_underscore_attribute";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		await migrator.Context.Document.CreateAsync(handle, DatabaseConstants.AttributeEntries,
			new
			{
				Name = "_",
				DefaultFlags = (string[])["veiled"]
			},
			overwriteMode: Core.Arango.Protocol.ArangoOverwriteMode.Ignore);
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
