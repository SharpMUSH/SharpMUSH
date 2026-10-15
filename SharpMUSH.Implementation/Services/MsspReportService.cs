using System.Globalization;
using Mediator;
using SharpMUSH.Configuration.Mssp;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <inheritdoc />
/// <remarks>
/// The server's own variables are PennMUSH <c>report_mssp()</c>'s (<c>src/bsd.c</c>) — NAME, PLAYERS,
/// UPTIME, PORT, SSL, PUEBLO, CODEBASE, FAMILY, WEBSITE — plus the character sets and protocols the
/// connection server negotiates on every telnet connection, and the <see cref="MsspGroup.Activity"/> variables:
/// distinct players over the last day and week (<see cref="PlayerActivity"/>), the login switches, and the portal
/// pages anyone may open.
/// </remarks>
public sealed class MsspReportService(
	IOptionsWrapper<SharpMUSHOptions> options,
	IConnectionService connections,
	IMediator mediator,
	IExpandedObjectDataService serverData,
	PlayerActivity activity,
	IApplicationRegistryService applications,
	TimeProvider time) : IMsspReportService
{
	/// <summary>
	/// What the connection server negotiates on every telnet connection, so a game cannot switch it off
	/// (<c>TelnetServer</c>: GMCP, MSDP, MCCP and CHARSET are registered unconditionally, and the
	/// renderer writes ANSI, xterm 256 and 24-bit colour for any client that asks).
	/// </summary>
	private static readonly string[] AlwaysSupported = ["ANSI", "UTF-8", "XTERM 256 COLORS", "XTERM TRUE COLORS", "GMCP", "MSDP", "MCCP"];

	public async ValueTask<IReadOnlyList<MsspReportedVariable>> BuildAsync()
	{
		var current = options.CurrentValue;
		var net = current.Net;
		var uptime = await serverData.GetExpandedServerDataAsync<UptimeData>();
		var players = await ConnectedPlayers.CountAsync(connections, mediator, current.Cosmetic.CountAll);
		var (day, week) = await activity.CountAsync(time.GetUtcNow());

		var server = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["NAME"] = [net.MudName],
			["PLAYERS"] = [Number(players)],
			["UPTIME"] = [Number((uptime?.StartTime ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds())],
			["CODEBASE"] = [$"SharpMUSH {Generated.VersionInfo.SharpMUSHVersion}"],
			["FAMILY"] = ["TinyMUD"],
			["PORT"] = [Number(net.Port)],
			// Preferred last, as the CHARSET option offers them (TelnetServer.WithCharsetOrder).
			["CHARSET"] = ["ISO-8859-1", "UTF-8"],
			["MXP"] = [Flag(net.Mxp)],
			["PUEBLO"] = [Flag(net.Pueblo)],
			["PLAYERS DAY"] = [Number(day)],
			["PLAYERS WEEK"] = [Number(week)],
			["LOGINS"] = [Flag(net.Logins)],
			["PLAYER CREATION"] = [Flag(net.PlayerCreation)],
			["GUESTS"] = [Flag(net.Guests)]
		};

		if (await OpenApplicationsAsync(net.MudUrl, current.Database.AllowBrowserCode) is { Length: > 0 } open)
		{
			server["PORTAL APPLICATION"] = open;
		}

		if (net.SslPort != 0)
		{
			server["SSL"] = [Number(net.SslPort)];
		}

		if (!string.IsNullOrWhiteSpace(net.MudUrl))
		{
			server["WEBSITE"] = [net.MudUrl];
		}

		foreach (var name in AlwaysSupported)
		{
			server[name] = ["1"];
		}

		var settings = MsspCatalog.Normalize(current.Mssp.Variables, out _);

		var catalogued = MsspCatalog.All
			.Select(variable => (variable.Name, Source: variable.ReportedByServer ? server : settings))
			.Where(entry => entry.Source.ContainsKey(entry.Name))
			.Select(entry => new MsspReportedVariable(entry.Name, entry.Source[entry.Name]));
		var others = settings
			.Where(entry => MsspCatalog.Find(entry.Key) is null)
			.Select(entry => new MsspReportedVariable(entry.Key, entry.Value));

		return [.. catalogued, .. others];
	}

	/// <summary>
	/// The portal page applications anyone signed in may open, as "address name": no staff role or permission, and
	/// only schema pages unless the game runs browser code. None without a <c>mud_url</c> to make an address from.
	/// </summary>
	private async ValueTask<string[]> OpenApplicationsAsync(string? mudUrl, bool allowBrowserCode)
	{
		if (!Uri.TryCreate(mudUrl, UriKind.Absolute, out var site))
		{
			return [];
		}

		return (await applications.GetApplicationsAsync())
			.Where(app => app.Kind == ApplicationKind.Page && app.MinimumRole <= PortalRole.Player && app.Permission is null)
			.Where(app => allowBrowserCode || app.RenderKind == ApplicationRenderKind.Schema)
			.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
			.Select(app => $"{new Uri(site, $"/apps/{Uri.EscapeDataString(app.Slug)}")} {app.DisplayName}")
			.ToArray();
	}

	private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Flag(bool value) => value ? "1" : "0";
}
