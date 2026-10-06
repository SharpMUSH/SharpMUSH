using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Adds the default <c>Mssp</c> category to a stored configuration written before it existed, once.
/// <c>SharpMUSHOptions.Mssp</c> is <c>required</c>, so the document cannot be read without it.
/// </summary>
public partial class LightningDatabase
{
	internal const string MsspOptionMigrationId = "0014_mssp_option";

	private async ValueTask AddMsspOptionAsync(CancellationToken cancellationToken)
	{
		var marker = Keys.Str("mig:" + MsspOptionMigrationId);
		if (Store.Read(tx => tx.TryGet(Tables.Meta, marker, out _))) return;

		await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.Meta, marker, out _)) return;

			var key = Keys.Str("SharpMUSHOptions");
			if (tx.TryGet(Tables.ExpandedSrv, key, out var bytes)
				&& JsonNode.Parse(bytes) is JsonObject options
				&& !options.ContainsKey("Mssp"))
			{
				options["Mssp"] = JsonSerializer.SerializeToNode(SharpMUSHOptions.Default().Mssp);
				tx.Put(Tables.ExpandedSrv, key, JsonSerializer.SerializeToUtf8Bytes(options));
			}

			tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
			{
				Id = MsspOptionMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			}));
		}, cancellationToken);
	}
}
