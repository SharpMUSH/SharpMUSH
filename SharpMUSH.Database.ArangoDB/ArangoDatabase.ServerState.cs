using System.Text.Json;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	private const string ServerStateDocKey = "state";

	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", ServerStateDocKey }
			}, cancellationToken: cancellationToken);

		if (result.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } elem)
			return new SharpServerState();

		return new SharpServerState
		{
			SetupCompleted = elem.TryGetProperty("SetupCompleted", out var sc)
				&& sc.ValueKind == JsonValueKind.True
		};
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.ServerState },
				{ "key", ServerStateDocKey },
				{ "doc", new Dictionary<string, object?> { ["_key"] = ServerStateDocKey, ["SetupCompleted"] = value } }
			}, cancellationToken: cancellationToken);
	}
}
