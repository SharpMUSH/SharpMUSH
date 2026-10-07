using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

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
	/// <paramref name="ThemeId"/> and <paramref name="Accent"/> are the character's portal look
	/// (<see cref="IPortalThemeService.GetAppearanceAsync"/>), so a tab themes itself from the roster it already reads.
	/// </summary>
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing = false,
		string? ThemeId = null, string? Accent = null);

	/// <param name="themes">Reads each character's look; without it the summaries carry none.</param>
	public static async Task<IReadOnlyList<CharacterSummary>> BuildSummariesAsync(
		IReadOnlyList<SharpPlayer> characters, CancellationToken ct = default,
		int? actingKey = null, long? actingCreationTime = null, IPortalThemeService? themes = null) =>
		await characters.ToAsyncEnumerable()
			.Select(async (c, innerCt) =>
			{
				var look = themes is null ? null : await themes.GetAppearanceAsync(c.Object);
				return new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name,
					string.Join(" ", (await c.Object.ReadFlagsAsync(innerCt)).Flags.Select(f => f.Name)),
					c.Object.Key == actingKey && c.Object.CreationTime == actingCreationTime,
					look?.ThemeId, look?.Accent);
			})
			.ToListAsync(ct);
}
