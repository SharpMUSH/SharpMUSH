using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// A configuration stored before the <c>Mssp</c> category existed: <c>SharpMUSHOptions.Mssp</c> is
/// <c>required</c>, so the document only reads again once the migration has added it.
/// </summary>
public class MsspOptionMigrationTests : LightningDatabaseFixture
{
	[Test]
	public async Task MigrationAddsAnEmptyMsspCategoryToAStoredConfiguration()
	{
		await Db.SetExpandedServerData(nameof(SharpMUSHOptions), SharpMUSHOptions.Default());
		var key = Keys.Str(nameof(SharpMUSHOptions));
		await Db.Store.WriteAsync(tx =>
		{
			tx.TryGet(Tables.ExpandedSrv, key, out var bytes);
			var stored = JsonNode.Parse(bytes)!.AsObject();
			stored.Remove("Mssp");
			tx.Put(Tables.ExpandedSrv, key, JsonSerializer.SerializeToUtf8Bytes(stored));
			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.MsspOptionMigrationId));
		});

		await Assert.That(async () => await Db.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))
			.Throws<JsonException>();

		await ReopenAsync();

		var options = await Db.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(options!.Mssp.Variables).IsEmpty();
		await Assert.That(options.Net.MudName).IsEqualTo(SharpMUSHOptions.Default().Net.MudName);
	}

	[Test]
	public async Task MigrationLeavesSavedMsspSettingsAlone()
	{
		var saved = SharpMUSHOptions.Default() with
		{
			Mssp = new MsspOptions(new Dictionary<string, string[]> { ["GENRE"] = ["Fantasy"] })
		};
		await Db.SetExpandedServerData(nameof(SharpMUSHOptions), saved);
		await Db.Store.WriteAsync(tx => tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.MsspOptionMigrationId)));

		await ReopenAsync();

		var options = await Db.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(options!.Mssp.Variables["GENRE"]).IsEquivalentTo(["Fantasy"]);
	}
}
