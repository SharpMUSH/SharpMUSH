using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The connection's address and host name, as the sitelock gates read them.</summary>
	private (string IpAddress, string HostName) ConnectionOrigin(long handle)
	{
		var connectionData = ConnectionService.Get(handle);
		var ipAddress = connectionData?.Metadata.TryGetValue("InternetProtocolAddress", out var ip) == true ? ip : "unknown";
		return (ipAddress, connectionData?.HostName ?? ipAddress);
	}

	/// <summary>
	/// PennMUSH <c>connect_player</c>'s sitelock check once the player is known (<c>src/player.c:277-290</c>):
	/// a rule with <c>!god</c>, <c>!wizard</c> or <c>!admin</c> refuses that class of player from the site.
	/// </summary>
	private async ValueTask<bool> SitelockRefusesPlayerAsync(long handle, SharpPlayer player)
	{
		var (ipAddress, hostName) = ConnectionOrigin(handle);
		var who = new AnySharpObject(player);
		if (!SitelockMatcher.RefusesPrivileged(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName,
				who.IsGod(), await who.IsWizard(), await who.IsPriv()))
		{
			return false;
		}

		LogSitelockRefusal(player.Object.Name, ipAddress, hostName);
		await NotifyService.Notify(handle, "Access from your location is restricted.");
		return true;
	}

	/// <summary>
	/// Logs a sitelock refusal unless a matching rule carries <c>deny_silent</c>
	/// (PennMUSH <c>Deny_Silent_Site</c>, <c>src/player.c:267,279</c>).
	/// </summary>
	private void LogSitelockRefusal(string attempt, string ipAddress, string hostName)
	{
		if (SitelockMatcher.Applies(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName,
				SitelockMatcher.DenySilentOption))
		{
			return;
		}

		Logger.LogInformation("Connection to {Attempt} not allowed from {Host} ({IP})", attempt, hostName, ipAddress);
	}

	/// <summary>
	/// PennMUSH <c>connect_player</c> (<c>src/player.c:317-321</c>): a player who connects from a
	/// <c>suspect</c> site is set SUSPECT.
	/// </summary>
	private async ValueTask MarkSuspectSiteAsync(long handle, SharpPlayer player)
	{
		var (ipAddress, hostName) = ConnectionOrigin(handle);
		if (!SitelockMatcher.Applies(Configuration.CurrentValue.SitelockRules.Rules, ipAddress, hostName,
				SitelockMatcher.SuspectOption)
			|| await Mediator.Send(new GetObjectFlagQuery("SUSPECT")) is not { } suspect)
		{
			return;
		}

		Logger.LogInformation("Connection from Suspect site. Setting {Name}(#{Key}) suspect.", player.Object.Name, player.Object.Key);
		await Mediator.Send(new SetObjectFlagCommand(new AnySharpObject(player), suspect));
	}
}
