using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Shared character-summary shape and mapping used by both <see cref="AuthController"/>'s
/// account-login/register responses and <see cref="AccountController"/>'s character-list
/// endpoint. The two controllers previously carried byte-identical private
/// <c>CharacterSummary</c> records and near-identical build helpers; consolidated here.
/// The record's member names/order are preserved exactly so the JSON shape of both
/// endpoints is unchanged.
/// </summary>
public static class CharacterSummaryMapper
{
	/// <summary>
	/// <paramref name="IsActing"/> marks the character the caller's session is bound to. The roster is
	/// how a reloaded tab learns who it is: the acting identity lives in the session token, which is
	/// opaque to the client, so the server has to say. Defaults false for callers that don't resolve it.
	/// </summary>
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing = false);

	public static async Task<IReadOnlyList<CharacterSummary>> BuildSummariesAsync(
		IReadOnlyList<SharpPlayer> characters, CancellationToken ct = default,
		int? actingKey = null, long? actingCreationTime = null) =>
		await characters.ToAsyncEnumerable()
			.Select(async (c, innerCt) => new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name,
				string.Join(" ", await c.Object.Flags.Value.Select(f => f.Name).ToListAsync(innerCt)),
				c.Object.Key == actingKey && c.Object.CreationTime == actingCreationTime))
			.ToListAsync(ct);
}
