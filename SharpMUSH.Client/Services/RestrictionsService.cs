using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Typed client for <c>@restrict</c>'s command and function restriction lists.
/// </summary>
/// <remarks>
/// A name goes into the path, so it is escaped. Command names are not identifiers — <c>@EMIT</c>,
/// <c>+who</c>, <c>WHO/ALL</c> — and an unescaped slash silently addresses a different route.
/// </remarks>
public class RestrictionsService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<Dictionary<string, string[]>>> GetCommandRestrictionsAsync() =>
		Client.GetApiAsync<Dictionary<string, string[]>>(
			"api/restrictions/commands", "The server returned no command restrictions.");

	public Task<ApiResult<Success>> AddCommandRestrictionAsync(string commandName, string[] restrictions) =>
		Client.PostApiAsync($"api/restrictions/commands/{Uri.EscapeDataString(commandName)}", restrictions);

	public Task<ApiResult<Success>> DeleteCommandRestrictionAsync(string commandName) =>
		Client.DeleteApiAsync($"api/restrictions/commands/{Uri.EscapeDataString(commandName)}");

	public Task<ApiResult<Dictionary<string, string[]>>> GetFunctionRestrictionsAsync() =>
		Client.GetApiAsync<Dictionary<string, string[]>>(
			"api/restrictions/functions", "The server returned no function restrictions.");

	public Task<ApiResult<Success>> AddFunctionRestrictionAsync(string functionName, string[] restrictions) =>
		Client.PostApiAsync($"api/restrictions/functions/{Uri.EscapeDataString(functionName)}", restrictions);

	public Task<ApiResult<Success>> DeleteFunctionRestrictionAsync(string functionName) =>
		Client.DeleteApiAsync($"api/restrictions/functions/{Uri.EscapeDataString(functionName)}");
}
