using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
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
	/// <paramref name="ThemeId"/>, <paramref name="Accent"/>, <paramref name="Vision"/>, <paramref name="ImageFilters"/> and <paramref name="ReduceMotion"/> are the character's portal look
	/// (<see cref="IPortalThemeService.GetAppearanceAsync"/>), so a tab themes itself from the roster it already reads.
	/// <paramref name="Locale"/> is the character's <c>LOCALE</c> attribute, which <c>@locale</c> and the portal's
	/// language picker both write, so a tab in a new browser takes the character's language from the roster too.
	/// </summary>
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags, bool IsActing = false,
		string? ThemeId = null, string? Accent = null, string? Vision = null, string? Locale = null, bool ImageFilters = true,
		bool ReduceMotion = false);

	/// <param name="themes">Reads each character's look; without it the summaries carry none.</param>
	/// <param name="mediator">Reads each character's <c>LOCALE</c>; without it the summaries carry none.</param>
	public static async Task<IReadOnlyList<CharacterSummary>> BuildSummariesAsync(
		IReadOnlyList<SharpPlayer> characters, CancellationToken ct = default,
		int? actingKey = null, long? actingCreationTime = null, IPortalThemeService? themes = null,
		IMediator? mediator = null) =>
		await characters.ToAsyncEnumerable()
			.Select(async (c, innerCt) =>
			{
				var look = themes is null ? null : await themes.GetAppearanceAsync(c.Object);
				var locale = mediator is null ? null : await LocaleAsync(mediator, c.Object.DBRef, innerCt);
				return new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name,
					string.Join(" ", (await c.Object.ReadFlagsAsync(innerCt)).Flags.Select(f => f.Name)),
					c.Object.Key == actingKey && c.Object.CreationTime == actingCreationTime,
					look?.ThemeId, look?.Accent, look?.Vision, locale, look?.ImageFilters ?? true,
					look?.ReduceMotion ?? false);
			})
			.ToListAsync(ct);

	/// <summary>The character's <c>LOCALE</c>, read through the Mediator as <c>@locale</c> reads it, or null when unset.</summary>
	private static async ValueTask<string?> LocaleAsync(IMediator mediator, DBRef character, CancellationToken ct)
		=> await mediator.CreateStream(new GetAttributeQuery(character, ["LOCALE"]), ct)
			.Select(attr => attr.Value.ToPlainText())
			.FirstOrDefaultAsync(value => !string.IsNullOrWhiteSpace(value), ct);
}
