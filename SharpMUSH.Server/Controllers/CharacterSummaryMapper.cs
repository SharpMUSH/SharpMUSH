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
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	public static async Task<IReadOnlyList<CharacterSummary>> BuildSummariesAsync(
		IReadOnlyList<SharpPlayer> characters, CancellationToken ct = default) =>
		await characters.ToAsyncEnumerable()
			.Select(async (c, innerCt) => new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name,
				string.Join(" ", await c.Object.Flags.Value.Select(f => f.Name).ToListAsync(innerCt))))
			.ToListAsync(ct);
}
