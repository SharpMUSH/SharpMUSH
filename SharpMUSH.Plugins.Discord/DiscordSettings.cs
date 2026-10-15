using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord;

/// <summary>The bot's options, set by staff with <c>discord.admin</c>.</summary>
/// <param name="MirrorBans">Whether a game ban also bans the account's Discord member in every server the bot is in.</param>
public sealed record DiscordSettings(bool MirrorBans = false);

public interface IDiscordSettingsStore
{
	/// <summary>The options as stored, or the defaults when none are.</summary>
	DiscordSettings Get();

	ValueTask SaveAsync(DiscordSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>One record in the plugin-owned table <c>discord.settings</c>.</summary>
public sealed class LightningDiscordSettingsStore(ILightningStorageAccessor accessor) : IDiscordSettingsStore
{
	private const ulong Key = 0;

	private readonly DiscordTable<DiscordSettings> _settings = new(accessor, "discord.settings");

	public DiscordSettings Get() => _settings.Get(Key) ?? new DiscordSettings();

	public ValueTask SaveAsync(DiscordSettings settings, CancellationToken cancellationToken = default)
		=> _settings.SaveAsync(Key, settings, cancellationToken);
}
