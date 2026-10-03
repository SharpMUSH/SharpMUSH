using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Runs game commands as the tab's acting character through <c>POST api/commands</c>, and hands back
/// what the command answered.
/// </summary>
/// <remarks>
/// This is the portal's route for a command it issues itself, as opposed to one the player types into
/// the terminal: it acts as the character the account session is bound to whatever the terminal is
/// playing, needs no terminal at all, and its answer belongs to this call alone.
/// </remarks>
public class GameCommandService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>
	/// Runs <paramref name="command"/> as one typed line. <paramref name="result"/>, when given, is an
	/// expression evaluated as the character straight after it, with nothing else in the game running in
	/// between; its value comes back as <see cref="PortalCommandResponse.Result"/>.
	/// </summary>
	public Task<ApiResult<PortalCommandResponse>> RunAsync(string command, string? result = null) =>
		Client.PostApiAsync<PortalCommandRequest, PortalCommandResponse>(
			"api/commands", new PortalCommandRequest(command, result), "The server returned no command result.");
}
