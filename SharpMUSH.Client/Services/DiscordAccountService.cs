using SharpMUSH.Library.Models.Portal.Discord;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The signed-in account's Discord link. A member starts it with <c>/link</c> in Discord, which sends them a link to
/// the confirm page with a ticket; confirming here, signed in, is what links the two.
/// </summary>
public class DiscordAccountService(IHttpClientFactory httpClientFactory)
{
	private const string NoLink = "The server returned no Discord link.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<DiscordAccountView>> GetAsync() =>
		Client.GetApiAsync<DiscordAccountView>("api/discord/account", NoLink);

	public Task<ApiResult<DiscordLinkTicketView>> TicketAsync(string ticket) =>
		Client.GetApiAsync<DiscordLinkTicketView>($"api/discord/link/{Uri.EscapeDataString(ticket)}", "The server returned no ticket.");

	public Task<ApiResult<DiscordAccountView>> ConfirmAsync(string ticket) =>
		Client.PostApiAsync<object?, DiscordAccountView>($"api/discord/link/{Uri.EscapeDataString(ticket)}", null, NoLink);

	public Task<ApiResult<DiscordAccountView>> UnlinkAsync() =>
		Client.DeleteApiAsync<DiscordAccountView>("api/discord/account", NoLink);
}
