using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Components.Admin;

/// <summary>
/// Adapts what a typed client returns into the entry map <c>AdminKeyValueList</c> renders, without
/// making every service agree on one collection type.
/// </summary>
public static class AdminKeyValueListEntries
{
	/// <summary>A key-to-values map, as sitelock rules and restriction tables already are.</summary>
	public static async Task<ApiResult<IReadOnlyDictionary<string, string[]>>> AsEntriesAsync(
		this Task<ApiResult<Dictionary<string, string[]>>> call) =>
		await call switch
		{
			Dictionary<string, string[]> map => (IReadOnlyDictionary<string, string[]>)map,
			ApiFailure failure => failure
		};

	/// <summary>A bare list of keys — a banned name has nothing to show beside it.</summary>
	public static async Task<ApiResult<IReadOnlyDictionary<string, string[]>>> AsEntriesAsync(
		this Task<ApiResult<string[]>> call) =>
		await call switch
		{
			string[] keys => (IReadOnlyDictionary<string, string[]>)keys
				.ToDictionary(key => key, _ => Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
			ApiFailure failure => failure
		};
}
