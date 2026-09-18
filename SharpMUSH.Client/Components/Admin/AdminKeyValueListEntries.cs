using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Components.Admin;

/// <summary>One row of an <c>AdminKeyValueList</c>: a key and the values beside it.</summary>
/// <remarks>
/// A list of these rather than a dictionary, deliberately. The server stores these lists in ordinary
/// case-sensitive dictionaries (<c>RestrictionsController</c>, <c>SitelockController</c>,
/// <c>BannedNamesController</c>), so <c>Example.com</c> and <c>example.com</c> are two distinct rules
/// and both arrive. Re-keying the response into a case-insensitive dictionary threw
/// <see cref="ArgumentException"/> on exactly those configurations; re-keying it into a
/// case-sensitive one would only move the throw to a payload with a genuine duplicate. The list
/// renders whatever the server sent, which is what the pages this replaced did.
/// </remarks>
public sealed record AdminKeyValueEntry(string Key, IReadOnlyList<string> Values);

/// <summary>
/// Adapts what a typed client returns into the rows <c>AdminKeyValueList</c> renders, without making
/// every service agree on one collection type.
/// </summary>
public static class AdminKeyValueListEntries
{
	/// <summary>A key-to-values map, as sitelock rules and restriction tables already are.</summary>
	public static async Task<ApiResult<IReadOnlyList<AdminKeyValueEntry>>> AsEntriesAsync(
		this Task<ApiResult<Dictionary<string, string[]>>> call) =>
		await call switch
		{
			Dictionary<string, string[]> map =>
				(IReadOnlyList<AdminKeyValueEntry>)[.. map.Select(pair => new AdminKeyValueEntry(pair.Key, pair.Value))],
			ApiFailure failure => failure
		};

	/// <summary>A bare list of keys — a banned name has nothing to show beside it.</summary>
	public static async Task<ApiResult<IReadOnlyList<AdminKeyValueEntry>>> AsEntriesAsync(
		this Task<ApiResult<string[]>> call) =>
		await call switch
		{
			string[] keys =>
				(IReadOnlyList<AdminKeyValueEntry>)[.. keys.Select(key => new AdminKeyValueEntry(key, []))],
			ApiFailure failure => failure
		};
}
