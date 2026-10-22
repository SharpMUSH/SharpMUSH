using System.Text.Json;

namespace SharpMUSH.Database.Lightning.Migration;

internal static class WikiCategoryAuthorityCodec
{
	internal const int MaxMarkerBytes = 1024;
	private const string InvalidAuthorityMessage = "Invalid wiki category migration authority.";

	internal static bool Validate(bool hasState, ReadOnlySpan<byte> state, bool hasCompletion,
		ReadOnlySpan<byte> completion, int maxKeySize, CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		WikiCategoryCheckpointCodec.ValidateEnvironmentMaxKeySize(maxKeySize);
		if ((!hasState && !state.IsEmpty) || (!hasCompletion && !completion.IsEmpty))
			throw InvalidAuthority();
		if (!hasState)
		{
			if (hasCompletion) throw InvalidAuthority();
			token.ThrowIfCancellationRequested();
			return false;
		}

		// Decode owns every checkpoint shape rule, including incomplete phases.
		var checkpoint = WikiCategoryCheckpointCodec.Decode(state, maxKeySize, token);
		if (checkpoint.Phase != WikiCategoryMigrationPhase.Complete)
		{
			if (hasCompletion) throw InvalidAuthority();
			token.ThrowIfCancellationRequested();
			return false;
		}
		if (!hasCompletion || completion.Length is < 1 or > MaxMarkerBytes)
			throw InvalidAuthority();

		try
		{
			ValidateMarker(completion, checkpoint.CompletionUnixMs, token);
		}
		catch (JsonException)
		{
			// JSON diagnostics can include stored input. Never retain that exception.
			throw InvalidAuthority();
		}
		token.ThrowIfCancellationRequested();
		return true;
	}

	private static void ValidateMarker(ReadOnlySpan<byte> completion, long completionUnixMs,
		CancellationToken token)
	{
		var reader = new Utf8JsonReader(completion);
		if (!Read(ref reader, token) || reader.TokenType != JsonTokenType.StartObject)
			throw InvalidAuthority();

		var hasId = false;
		var hasTimestamp = false;
		while (Read(ref reader, token))
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				if (!hasId || !hasTimestamp || Read(ref reader, token))
					throw InvalidAuthority();
				return;
			}
			if (reader.TokenType != JsonTokenType.PropertyName)
				throw InvalidAuthority();

			if (reader.ValueTextEquals("Id"u8))
			{
				if (hasId || !Read(ref reader, token)
					|| reader.TokenType != JsonTokenType.String
					|| !reader.ValueTextEquals(WikiCategoryCheckpointCodec.MigrationId))
					throw InvalidAuthority();
				hasId = true;
			}
			else if (reader.ValueTextEquals("AppliedUnixMs"u8))
			{
				if (hasTimestamp || !Read(ref reader, token)
					|| reader.TokenType != JsonTokenType.Number
					|| !reader.TryGetInt64(out var timestamp)
					|| timestamp <= 0 || timestamp != completionUnixMs)
					throw InvalidAuthority();
				hasTimestamp = true;
			}
			else
				throw InvalidAuthority();
		}
		throw InvalidAuthority();
	}

	private static bool Read(ref Utf8JsonReader reader, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		return reader.Read();
	}

	private static InvalidDataException InvalidAuthority() => new(InvalidAuthorityMessage);
}
