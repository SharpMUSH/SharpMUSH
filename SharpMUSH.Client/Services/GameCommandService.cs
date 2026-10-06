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
	/// <paramref name="character"/>, an objid, pins the command to that character: if the session has
	/// moved on to another, the server refuses it rather than running it as the other one.
	/// </summary>
	public Task<ApiResult<PortalCommandResponse>> RunAsync(string command, string? result = null, string? character = null) =>
		Client.PostApiAsync<PortalCommandRequest, PortalCommandResponse>(
			"api/commands", new PortalCommandRequest(command, result, character), "The server returned no command result.");

	/// <summary>
	/// Evaluates <paramref name="expression"/> with <paramref name="arguments"/> as <c>%0</c>-<c>%9</c>, as the
	/// tab's character or, given <paramref name="objectDbref"/>, as that object (which the character must
	/// control): <c>%!</c> and <c>me</c> are then the object and <c>%#</c> the character, as under <c>u()</c>.
	/// The value comes back as <see cref="PortalCommandResponse.Result"/>, and anything the character was
	/// told meanwhile as <see cref="PortalCommandResponse.Output"/>.
	/// </summary>
	public Task<ApiResult<PortalCommandResponse>> EvaluateAsync(string expression, IReadOnlyList<string>? arguments = null,
		int? objectDbref = null, string? character = null) =>
		Client.PostApiAsync<PortalEvalRequest, PortalCommandResponse>(
			"api/commands/eval", new PortalEvalRequest(expression, arguments, objectDbref, character),
			"The server returned no evaluation result.");
}
