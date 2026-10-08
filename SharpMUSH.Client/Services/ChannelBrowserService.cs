using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The Play drawer's channel browser: the channels the acting character may see (<c>GET api/comm/channels</c>),
/// and joining, leaving and gagging them through the same <c>@channel</c> commands a player types, so a game's
/// join locks, its messages and the <c>comm.channels</c> push that follows all behave as they do in the terminal.
/// </summary>
public sealed class ChannelBrowserService(IHttpClientFactory httpClientFactory, GameCommandService commands)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<IReadOnlyList<ChannelListing>>> ListAsync() =>
		Client.GetApiAsync<IReadOnlyList<ChannelListing>>("api/comm/channels", "The server returned no channels.");

	public Task<ApiResult<PortalCommandResponse>> JoinAsync(string channel) =>
		commands.RunAsync($"@channel/on {Argument(channel)}");

	public Task<ApiResult<PortalCommandResponse>> LeaveAsync(string channel) =>
		commands.RunAsync($"@channel/off {Argument(channel)}");

	public Task<ApiResult<PortalCommandResponse>> GagAsync(string channel, bool gag) =>
		commands.RunAsync($"@channel/gag {Argument(channel)}={(gag ? "yes" : "no")}");

	/// <summary>
	/// A channel name as one literal command argument. Names hold no spaces, but may hold characters the
	/// parser would act on (<c>[</c>, <c>%</c>, <c>=</c>, ...); each of those goes in behind a backslash.
	/// </summary>
	public static string Argument(string name) =>
		string.Concat(name.Select(c => SpecialCharacters.Contains(c) ? $"\\{c}" : c.ToString()));

	private const string SpecialCharacters = "\\[]{}()%,;=$#";
}
