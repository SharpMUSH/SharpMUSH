using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Where a game account stands, as far as Discord is concerned.</summary>
public enum DiscordStanding
{
	Active,

	/// <summary>Banned in the game: disabled, with a ban record.</summary>
	Banned,

	/// <summary>Gone, disabled without a ban, or closed.</summary>
	Inactive,
}

public interface IDiscordStandings
{
	Task<DiscordStanding> StandingAsync(string accountId, CancellationToken cancellationToken);
}

/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordStandings(IServiceProvider services) : IDiscordStandings
{
	public async Task<DiscordStanding> StandingAsync(string accountId, CancellationToken cancellationToken)
	{
		var accounts = services.GetRequiredService<IAccountService>();
		return await accounts.GetByIdAsync(accountId, cancellationToken) switch
		{
			null => DiscordStanding.Inactive,
			{ IsActive: true } => DiscordStanding.Active,
			_ when await accounts.GetBanAsync(accountId, cancellationToken) is not null => DiscordStanding.Banned,
			_ => DiscordStanding.Inactive,
		};
	}
}

/// <summary>What the bot does for one Discord user after their account's standing changes.</summary>
/// <param name="HoldLinked">Whether they hold the Linked role; null when they are not linked, which the unlink already settled.</param>
/// <param name="Ban">The servers to ban them in.</param>
/// <param name="Unban">The servers to lift the bot's own ban in.</param>
public sealed record DiscordStandingPlan(bool? HoldLinked, IReadOnlyList<ulong> Ban, IReadOnlyList<ulong> Unban)
{
	/// <summary>
	/// A linked member holds the Linked role only while their account is active. With bans mirrored, a member whose
	/// account is banned is banned in every server the bot is in that it has not banned them in already, so a ban staff
	/// lift by hand on Discord stays lifted. Once the game ban ends, the bot lifts the bans it made, and only those, whether
	/// or not bans are still mirrored.
	/// </summary>
	/// <param name="guilds">The servers the bot is in now.</param>
	/// <param name="bannedIn">The servers the bot already banned this member in.</param>
	public static DiscordStandingPlan For(DiscordStanding standing, bool linked, bool mirrorBans, IReadOnlyList<ulong> guilds,
		IReadOnlyList<ulong> bannedIn)
	{
		bool? holdLinked = linked ? standing == DiscordStanding.Active : null;
		return standing == DiscordStanding.Banned
			? new DiscordStandingPlan(holdLinked, mirrorBans && linked ? [.. guilds.Except(bannedIn)] : [], [])
			: new DiscordStandingPlan(holdLinked, [], bannedIn);
	}
}
