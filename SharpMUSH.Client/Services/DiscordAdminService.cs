using SharpMUSH.Library.Models.Portal.Discord;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The Discord plugin's admin surface (<c>api/discord/admin</c>): the bot's connection, linked channels, role mappings
/// and linked members. Every call needs <c>discord.admin</c>; an action answers with what it did, said for the admin.
/// </summary>
public class DiscordAdminService(IHttpClientFactory httpClientFactory)
{
	private const string Base = "api/discord/admin";
	private const string NoAnswer = "The server did not say what happened.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<DiscordConnectionView>> ConnectionAsync() =>
		Client.GetApiAsync<DiscordConnectionView>($"{Base}/connection", "The server returned no connection.");

	public Task<ApiResult<DiscordAdminAnswer>> ReconnectAsync() =>
		Client.PostApiAsync<object?, DiscordAdminAnswer>($"{Base}/connection", null, NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> MirrorBansAsync(bool on) =>
		Client.PutApiAsync<DiscordSettingsRequest, DiscordAdminAnswer>($"{Base}/settings", new DiscordSettingsRequest(on), NoAnswer);

	public Task<ApiResult<List<DiscordChannelView>>> ChannelsAsync() =>
		Client.GetApiAsync<List<DiscordChannelView>>($"{Base}/channels", "The server returned no channels.");

	public Task<ApiResult<List<DiscordTextChannelView>>> AvailableAsync() =>
		Client.GetApiAsync<List<DiscordTextChannelView>>($"{Base}/channels/available", "The server returned no channels.");

	public Task<ApiResult<DiscordAdminAnswer>> LinkAsync(string channelId) =>
		Client.PostApiAsync<DiscordLinkChannelRequest, DiscordAdminAnswer>($"{Base}/channels", new DiscordLinkChannelRequest(channelId), NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> UnlinkAsync(string channelId) =>
		Client.DeleteApiAsync<DiscordAdminAnswer>($"{Base}/channels/{Uri.EscapeDataString(channelId)}", NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> ResumeAsync(string channelId) =>
		Client.PostApiAsync<object?, DiscordAdminAnswer>($"{Base}/channels/{Uri.EscapeDataString(channelId)}/resume", null, NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> GateAsync(string channelId) =>
		Client.PostApiAsync<object?, DiscordAdminAnswer>($"{Base}/channels/{Uri.EscapeDataString(channelId)}/gate", null, NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> ChannelAccessAsync(string channelId, bool unrestricted) =>
		Client.PutApiAsync<DiscordChannelAccessRequest, DiscordAdminAnswer>($"{Base}/channels/{Uri.EscapeDataString(channelId)}/access",
			new DiscordChannelAccessRequest(unrestricted), NoAnswer);

	public Task<ApiResult<DiscordRolesView>> RolesAsync() =>
		Client.GetApiAsync<DiscordRolesView>($"{Base}/roles", "The server returned no roles.");

	public Task<ApiResult<DiscordAdminAnswer>> MapAsync(DiscordMapRequest request) =>
		Client.PostApiAsync<DiscordMapRequest, DiscordAdminAnswer>($"{Base}/roles", request, NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> UnmapAsync(string roleId) =>
		Client.DeleteApiAsync<DiscordAdminAnswer>($"{Base}/roles/{Uri.EscapeDataString(roleId)}", NoAnswer);

	public Task<ApiResult<DiscordAdminAnswer>> ModeAsync(string roleId, string mode) =>
		Client.PutApiAsync<DiscordModeRequest, DiscordAdminAnswer>($"{Base}/roles/{Uri.EscapeDataString(roleId)}/mode",
			new DiscordModeRequest(mode), NoAnswer);

	/// <summary>Puts the game's roles back where they were changed by hand: on one role, or on every one.</summary>
	public Task<ApiResult<DiscordAdminAnswer>> SettleAsync(string? roleId) =>
		Client.PostApiAsync<object?, DiscordAdminAnswer>(
			roleId is null ? $"{Base}/roles/settle" : $"{Base}/roles/settle/{Uri.EscapeDataString(roleId)}", null, NoAnswer);

	public Task<ApiResult<List<DiscordMemberView>>> MembersAsync(string? search) =>
		Client.GetApiAsync<List<DiscordMemberView>>(
			search is { Length: > 0 } ? $"{Base}/members?search={Uri.EscapeDataString(search)}" : $"{Base}/members",
			"The server returned no members.");

	public Task<ApiResult<DiscordAdminAnswer>> UnlinkMemberAsync(string userId) =>
		Client.DeleteApiAsync<DiscordAdminAnswer>($"{Base}/members/{Uri.EscapeDataString(userId)}", NoAnswer);
}
