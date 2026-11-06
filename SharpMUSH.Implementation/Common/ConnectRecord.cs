using System.Globalization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Common;

/// <summary>A player's <c>LAST</c>, <c>LASTSITE</c>, <c>LASTIP</c> and <c>LASTFAILED</c>, which logins report and update.</summary>
public static class ConnectRecord
{
	/// <summary>The time a connect is recorded at, as PennMUSH's <c>show_time</c> writes it.</summary>
	public static string Now()
		=> DateTimeOffset.UtcNow.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);

	/// <summary>
	/// PennMUSH <c>make_player</c> (<c>src/player.c</c>) records a new player's creation as its last connect: from the
	/// connecting host for <c>create</c> at the connect screen, from <c>None</c> for <c>@pcreate</c> and
	/// <c>pcreate()</c> (<c>src/wiz.c</c>), so the first login reports it as any later one does. God's writes, as a
	/// login's are: the attributes are wizard-flagged.
	/// </summary>
	public static async ValueTask CreatedAsync(IMediator mediator, DBRef player, string site = "None", string ip = "None")
	{
		if (await HelperFunctions.GetGod(mediator) is not SharpPlayer god) return;
		await mediator.Send(new SetAttributeCommand(player, ["LAST"], MarkupText.Plain(Now()), god));
		await mediator.Send(new SetAttributeCommand(player, ["LASTSITE"], MarkupText.Plain(site), god));
		await mediator.Send(new SetAttributeCommand(player, ["LASTIP"], MarkupText.Plain(ip), god));
		await mediator.Send(new SetAttributeCommand(player, ["LASTFAILED"], MarkupText.Plain(" "), god));
	}
}
