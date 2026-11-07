using System.Globalization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// RhostMUSH's <c>TOTCMDS</c>: the commands a player typed in every session that has ended, which
/// <c>totcmds()</c> adds the live sessions to. A session's count is added when it ends, by QUIT or LOGOUT.
/// </summary>
public static class TotalCommands
{
	public const string Attribute = "TOTCMDS";

	/// <summary>
	/// Adding reads the total and writes it back, so two sessions of one player ending together would each
	/// add to the same old total. One at a time, the second sees the first's sum.
	/// </summary>
	private static readonly SemaphoreSlim Gate = new(1, 1);

	/// <summary>The total kept on <paramref name="player"/>, 0 when there is none or it is not a number.</summary>
	public static async ValueTask<long> StoredAsync(IMediator mediator, DBRef player)
	{
		var stored = await mediator.CreateStream(new GetAttributeQuery(player, [Attribute])).LastOrDefaultAsync();
		return stored is not null
			&& long.TryParse(stored.Value.ToPlainText().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
				? total
				: 0L;
	}

	/// <summary>
	/// Adds a session's <paramref name="commands"/> to the player's total. Written as God, like
	/// <c>LASTLOGOUT</c>, because the attribute is wizard-only and the player is not the one writing it.
	/// </summary>
	public static async ValueTask AddSessionAsync(IMediator mediator, DBRef player, int commands)
	{
		if (commands <= 0) return;

		await Gate.WaitAsync();
		try
		{
			if (await HelperFunctions.GetGod(mediator) is not SharpPlayer god) return;

			var total = await StoredAsync(mediator, player) + commands;
			await mediator.Send(new SetAttributeCommand(player, [Attribute],
				MarkupText.Plain(total.ToString(CultureInfo.InvariantCulture)), god));
		}
		finally
		{
			Gate.Release();
		}
	}
}
