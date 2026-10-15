namespace SharpMUSH.Library.Models.Portal.Discord;

// The admin page's views (api/discord/admin/*). Discord ids are strings: they pass 2^53, which a browser's
// numbers cannot hold.

/// <summary>One item of the setup checklist.</summary>
/// <param name="Todo">What the admin does next, when it is not done.</param>
/// <param name="Link">Where they do it.</param>
public sealed record DiscordSetupStepView(string Key, string Title, bool Done, string? Todo, string? Link);

/// <summary>The bot's connection (<c>GET api/discord/admin/connection</c>).</summary>
/// <param name="Status">noToken, connecting, connected, badToken, intentsOff or unreachable.</param>
/// <param name="InviteUrl">The link that adds the bot to a server, once Discord accepted the token.</param>
/// <param name="Guilds">How many servers the bot is in, while connected.</param>
public sealed record DiscordConnectionView(
	string Status,
	string? BotName,
	string? InviteUrl,
	int Guilds,
	IReadOnlyList<DiscordSetupStepView> Steps);

/// <summary>A linked Discord channel (<c>GET api/discord/admin/channels</c>).</summary>
/// <param name="Feed">The feed it is, <c>discord/&lt;channel id&gt;</c>.</param>
/// <param name="State">ok, backlogged or stopped.</param>
/// <param name="Waiting">How many lines are waiting to be posted.</param>
/// <param name="Fault">Why it stopped, while it is stopped.</param>
/// <param name="OpenTo">Who besides linked members may post there: empty when nobody, null when the bot cannot tell.</param>
public sealed record DiscordChannelView(
	string ChannelId,
	string ChannelName,
	string GuildName,
	string Feed,
	string State,
	int Waiting,
	string? Fault,
	IReadOnlyList<string>? OpenTo,
	DateTimeOffset LinkedAt);

/// <summary>A text channel the bot can see, to link (<c>GET api/discord/admin/channels/available</c>).</summary>
/// <param name="Missing">The permissions a link needs there that the bot lacks.</param>
/// <param name="Linked">Whether it is linked already.</param>
public sealed record DiscordTextChannelView(string ChannelId, string Name, string GuildName, IReadOnlyList<string> Missing, bool Linked);

/// <summary>Links a Discord channel (<c>POST api/discord/admin/channels</c>).</summary>
public sealed record DiscordLinkChannelRequest(string ChannelId);

/// <summary>What an admin action did, said for the admin.</summary>
/// <param name="Kind">ok, warn or error.</param>
public sealed record DiscordAdminAnswer(string Message, string Kind);

/// <summary>A game role mapped to a Discord role.</summary>
/// <param name="Slug">The game role.</param>
/// <param name="Mode">report or enforce: what the bot does with a change made by hand on Discord.</param>
/// <param name="Problem">Why the bot cannot give or take the role now, or null when it can.</param>
/// <param name="Drift">How many members were changed by hand and not yet settled.</param>
public sealed record DiscordRoleMappingView(
	string RoleId,
	string RoleName,
	string Slug,
	string Mode,
	string? Problem,
	int Drift,
	DateTimeOffset MappedAt);

/// <summary>A mapped role given or taken by hand on Discord, against the game's roles.</summary>
/// <param name="Added">Whether the member was given the role (without the game role), or stripped of it (with it).</param>
public sealed record DiscordDriftView(
	string RoleId,
	string RoleName,
	string Slug,
	string UserId,
	string UserName,
	bool Added,
	DateTimeOffset At);

/// <summary>Role mappings and the changes made to them by hand (<c>GET api/discord/admin/roles</c>).</summary>
public sealed record DiscordRolesView(IReadOnlyList<DiscordRoleMappingView> Mappings, IReadOnlyList<DiscordDriftView> Drift);

/// <summary>Maps a game role to a Discord role (<c>POST api/discord/admin/roles</c>).</summary>
/// <param name="Role">The game role's slug.</param>
/// <param name="DiscordRole">The Discord role's id or name.</param>
/// <param name="Confirm">Maps <c>wizard</c> or <c>royalty</c>, which shows on Discord who is staff.</param>
public sealed record DiscordMapRequest(string Role, string DiscordRole, bool Confirm = false);

/// <summary>Sets what the bot does with changes made by hand (<c>PUT api/discord/admin/roles/{id}/mode</c>).</summary>
/// <param name="Mode">report or enforce.</param>
public sealed record DiscordModeRequest(string Mode);

/// <summary>A Discord user linked to a game account (<c>GET api/discord/admin/members</c>).</summary>
/// <param name="AccountName">The game account's name, or null when the account is gone.</param>
public sealed record DiscordMemberView(string UserId, string Username, string AccountId, string? AccountName, DateTimeOffset LinkedAt);
