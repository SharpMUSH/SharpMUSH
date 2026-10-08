using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What the Play drawer's channel browser changes: joining, leaving and gagging a channel through the same
/// <c>@channel</c> commands a player types, so a game's join locks, its messages and the <c>comm.channels</c>
/// push that follows all behave as they do in the terminal. Each asks the character's standing on the channel
/// (<c>cstatus()</c>) right after, in the same queue entry, so a refusal is told apart from a change.
/// </summary>
public sealed class ChannelBrowserService(GameCommandService commands)
{
	public Task<ApiResult<PortalCommandResponse>> JoinAsync(string channel) =>
		commands.RunAsync($"@channel/on {Argument(channel)}", Standing(channel));

	public Task<ApiResult<PortalCommandResponse>> LeaveAsync(string channel) =>
		commands.RunAsync($"@channel/off {Argument(channel)}", Standing(channel));

	public Task<ApiResult<PortalCommandResponse>> GagAsync(string channel, bool gag) =>
		commands.RunAsync($"@channel/gag {Argument(channel)}={(gag ? "yes" : "no")}", Standing(channel));

	/// <summary>
	/// The character's standing as <c>cstatus()</c> answers it: <c>OFF</c>, or <c>ON</c> followed by its flags
	/// (<c>GAG</c> among them).
	/// </summary>
	public static (bool Joined, bool Gagged) ReadStanding(string? result)
	{
		var words = (result ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return (words.Contains("ON", StringComparer.OrdinalIgnoreCase), words.Contains("GAG", StringComparer.OrdinalIgnoreCase));
	}

	private static string Standing(string channel) => $"cstatus(%#,{Argument(channel)})";

	/// <summary>
	/// A channel name as one literal argument. Names hold no spaces, but may hold characters the parser
	/// would act on (<c>[</c>, <c>%</c>, <c>=</c>, ...); each of those goes in behind a backslash.
	/// </summary>
	public static string Argument(string name) =>
		string.Concat(name.Select(c => SpecialCharacters.Contains(c) ? $"\\{c}" : c.ToString()));

	private const string SpecialCharacters = "\\[]{}()%,;=$#";
}
