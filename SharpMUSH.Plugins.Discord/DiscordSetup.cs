using Discord;

namespace SharpMUSH.Plugins.Discord;

/// <summary>Where the bot's connection to Discord stands.</summary>
public enum DiscordGatewayStatus
{
	/// <summary>No <c>discord.token</c> is set.</summary>
	NoToken,
	Connecting,
	Connected,
	/// <summary>Discord refused the token.</summary>
	BadToken,
	/// <summary>The application lacks a privileged intent the bot needs.</summary>
	IntentsOff,
	/// <summary>Discord could not be reached, or answered with an error the setup cannot act on.</summary>
	Unreachable,
}

/// <param name="ApplicationId">The bot's application, once Discord accepted the token.</param>
/// <param name="MissingIntents">The privileged intents turned off in the developer portal, by their names there.</param>
/// <param name="PublicBot">Whether anyone, not only the application's owner, may add the bot to a server.</param>
/// <param name="Guilds">How many servers the bot is in, while connected.</param>
/// <param name="Detail">What Discord said, when it is not one of the cases above.</param>
public sealed record DiscordGatewayState(
	DiscordGatewayStatus Status,
	ulong? ApplicationId = null,
	string? BotName = null,
	IReadOnlyList<string>? MissingIntents = null,
	bool? PublicBot = null,
	int Guilds = 0,
	string? Detail = null);

/// <summary>A text channel the bot can see.</summary>
/// <param name="Missing">The permissions a link uses there that the bot lacks, by the names Discord's settings use.</param>
public sealed record DiscordTextChannel(ulong ChannelId, string Name, ulong GuildId, string GuildName, IReadOnlyList<string> Missing);

/// <summary>One item of the admin page's checklist.</summary>
/// <param name="Todo">What the admin does next, when the item is not done.</param>
/// <param name="Link">Where they do it.</param>
public sealed record DiscordSetupStep(string Key, string Title, bool Done, string? Todo = null, string? Link = null);

public sealed record DiscordSetupView(
	DiscordGatewayStatus Status,
	string? BotName,
	string? InviteUrl,
	IReadOnlyList<DiscordSetupStep> Steps);

/// <summary>
/// What setting up the bot takes, as far as Discord lets a program do it. The one step it cannot do is create
/// the application: Discord has no API for that.
/// </summary>
public static class DiscordSetup
{
	public const string DeveloperPortal = "https://discord.com/developers/applications";

	/// <summary>
	/// The gateway intents the bot asks for. Message Content and Server Members are privileged: the admin turns
	/// them on in the developer portal, or Discord closes the connection. Guild Webhooks says when a linked
	/// channel's webhook is deleted.
	/// </summary>
	public const GatewayIntents Intents =
		GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildWebhooks
		| GatewayIntents.MessageContent | GatewayIntents.GuildMembers;

	/// <summary>
	/// What the bot does in a server, and nothing more: read and post in linked channels and their threads, post
	/// as speakers through its webhooks, replace a member's message with the line as the game delivered it (which
	/// deletes the original, so Manage Messages), give the Linked and mapped roles, and publish scenes as Scheduled
	/// Events. Creating events also lets it edit and delete its own.
	/// </summary>
	public const GuildPermission Permissions =
		GuildPermission.ViewChannel | GuildPermission.SendMessages | GuildPermission.SendMessagesInThreads
		| GuildPermission.ReadMessageHistory | GuildPermission.ManageMessages | GuildPermission.ManageWebhooks
		| GuildPermission.ManageRoles | GuildPermission.CreateEvents;

	/// <summary>The link that adds the bot to a server, with its slash commands and exactly <see cref="Permissions"/>.</summary>
	public static string InviteUrl(ulong applicationId)
		=> $"https://discord.com/oauth2/authorize?client_id={applicationId}&permissions={(ulong)Permissions}&scope=bot+applications.commands";

	/// <summary>
	/// What a linked channel needs of the bot, by the names Discord's settings use: seeing it, making its webhook,
	/// and replacing a member's message with the game's version.
	/// </summary>
	public static IReadOnlyList<string> MissingInChannel(ChannelPermissions permissions)
	{
		List<string> missing = [];
		if (!permissions.ViewChannel)
		{
			missing.Add("View Channel");
		}

		if (!permissions.ManageWebhooks)
		{
			missing.Add("Manage Webhooks");
		}

		if (!permissions.ManageMessages)
		{
			missing.Add("Manage Messages");
		}

		return missing;
	}

	/// <summary>
	/// The privileged intents <paramref name="flags"/> leave off, by their names in the developer portal. A bot
	/// in fewer than 100 servers has the <c>…Limited</c> flag instead of the full one.
	/// </summary>
	public static IReadOnlyList<string> MissingIntents(ApplicationFlags flags)
	{
		List<string> missing = [];
		if (!flags.HasFlag(ApplicationFlags.GatewayMessageContent) && !flags.HasFlag(ApplicationFlags.GatewayMessageContentLimited))
		{
			missing.Add("Message Content Intent");
		}

		if (!flags.HasFlag(ApplicationFlags.GatewayGuildMembers) && !flags.HasFlag(ApplicationFlags.GatewayGuildMembersLimited))
		{
			missing.Add("Server Members Intent");
		}

		return missing;
	}

	public static DiscordSetupView View(DiscordGatewayState state)
	{
		var invite = state.ApplicationId is { } id ? InviteUrl(id) : null;
		return new DiscordSetupView(state.Status, state.BotName, invite, [Token(state), IntentsStep(state), Private(state), Gateway(state), Server(state, invite)]);
	}

	private static DiscordSetupStep Token(DiscordGatewayState state) => state.Status switch
	{
		DiscordGatewayStatus.NoToken => new("token", "Bot token", false,
			"Create an application in the Discord developer portal, add a bot to it, and set its token as discord.token.", DeveloperPortal),
		DiscordGatewayStatus.BadToken => new("token", "Bot token", false,
			"Discord refused the token. Reset it on the application's Bot page and set the new one.", BotPage(state)),
		_ => new("token", "Bot token", true),
	};

	private static DiscordSetupStep IntentsStep(DiscordGatewayState state) => state switch
	{
		{ ApplicationId: null } => new("intents", "Privileged intents", false),
		{ MissingIntents: [_, ..] missing } => new("intents", "Privileged intents", false,
			$"Turn on {string.Join(" and ", missing)} under Privileged Gateway Intents on the application's Bot page, then connect again.", BotPage(state)),
		_ => new("intents", "Privileged intents", true),
	};

	/// <summary>Advice rather than a requirement: a public bot can be added by anyone who finds its invite.</summary>
	private static DiscordSetupStep Private(DiscordGatewayState state) => state.PublicBot switch
	{
		false => new("private", "Only you can add the bot", true),
		true => new("private", "Only you can add the bot", false,
			"Untick Public Bot on the application's Bot page, so nobody else can add the bot to their server.", BotPage(state)),
		null => new("private", "Only you can add the bot", false),
	};

	private static DiscordSetupStep Gateway(DiscordGatewayState state) => state.Status switch
	{
		DiscordGatewayStatus.Connected => new("gateway", "Connected to Discord", true),
		DiscordGatewayStatus.Connecting => new("gateway", "Connected to Discord", false, "Connecting."),
		DiscordGatewayStatus.Unreachable => new("gateway", "Connected to Discord", false, $"Discord could not be reached: {state.Detail}"),
		_ => new("gateway", "Connected to Discord", false),
	};

	private static DiscordSetupStep Server(DiscordGatewayState state, string? invite) => state switch
	{
		{ Guilds: > 0 } => new("server", "In a server", true),
		{ Status: DiscordGatewayStatus.Connected } => new("server", "In a server", false,
			"Open the invite link and pick your server. You need Manage Server there. If the server requires two-factor "
			+ "authentication for moderation, the application's owner needs it on as well, or Discord refuses the bot "
			+ "its Manage permissions.", invite),
		_ => new("server", "In a server", false),
	};

	private static string BotPage(DiscordGatewayState state)
		=> state.ApplicationId is { } id ? $"{DeveloperPortal}/{id}/bot" : DeveloperPortal;
}
