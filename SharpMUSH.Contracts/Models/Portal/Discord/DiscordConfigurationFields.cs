using SharpMUSH.Library.Models.Portal.Applications;

namespace SharpMUSH.Library.Models.Portal.Discord;

/// <summary>The Discord setting's UI and discovery share one field definition.</summary>
public static class DiscordConfigurationFields
{
	public static readonly ConfigurationOption Token = new("discord.token", "Discord bot token",
		"The Discord bot's token, from the Bot page of its application in the Discord developer portal.",
		["bot", "credential"], Route: "/admin/config/secrets");

	public static readonly ConfigurationOption MirrorBans = new("MirrorBans", "Mirror account bans", "Mirror game account bans to Discord.",
		["ban", "moderation"], LabelResourceKey: "AdmDiscordMirrorBans", DescriptionResourceKey: "AdmDiscordMirrorBansSub");
}
