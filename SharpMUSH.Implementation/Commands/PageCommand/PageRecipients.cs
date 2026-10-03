using System.Buffers;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.PageCommand;

/// <summary>
/// How <c>page</c> reads its list of recipients and finds each one. <c>page/recall</c> and
/// <c>pagerecall()</c> name the people in a conversation the same way, so the names that reached someone
/// also find the conversation with them.
/// </summary>
public static class PageRecipients
{
	/// <summary>
	/// plyrlist.c <c>lookup_player</c> (:163) and then bsd.c <c>short_page</c> (:6376), which is how
	/// <c>do_page</c> (speech.c:908-910) finds a recipient. Neither is a room-local match, so a page
	/// reaches a player standing anywhere and never reaches anything that is not a player.
	/// </summary>
	public static async ValueTask<PageRecipient> ResolveAsync(IMediator mediator, IConnectionService connections,
		string name)
	{
		if (await LookupPlayer(mediator, name) is AnySharpObject player)
		{
			return player;
		}

		return await ShortPage.MatchAsync(mediator, connections, name);
	}

	/// <summary>
	/// plyrlist.c <c>lookup_player</c>: a <c>#dbref</c> or objid that names a player, a leading
	/// <c>*</c> (<c>LOOKUP_TOKEN</c>) stripped, and otherwise the whole name or alias — matched
	/// case-insensitively and never partially.
	/// </summary>
	private static async ValueTask<Found<AnySharpObject>> LookupPlayer(IMediator mediator, string name)
	{
		if (name.Length == 0)
		{
			return new NotFound();
		}

		if (name[0] == '#')
		{
			return DBRef.TryParse(name, out var dbref)
					&& await mediator.Send(new GetObjectNodeQuery(dbref!.Value)) is AnySharpObject and SharpPlayer known
				? new Found<AnySharpObject>(known)
				: new NotFound();
		}

		var lookup = name[0] == '*' ? name[1..] : name;

		return await mediator.CreateStream(new GetPlayerQuery(lookup)).FirstOrDefaultAsync() is { } found
			? new Found<AnySharpObject>(found)
			: new NotFound();
	}

	private static readonly SearchValues<char> NextInListBreaks = SearchValues.Create(" \"");

	/// <summary>
	/// strutil.c <c>next_in_list</c>: spaces separate names, a leading <c>"</c> takes everything up to
	/// the next <c>"</c> as one name, and an unquoted name also stops at a <c>"</c>. Nothing else splits
	/// a name — <c>#12Lamp</c> is one token, which <c>parse_dbref</c> then refuses as a whole. <c>whisper</c>
	/// reads its targets the same way.
	/// </summary>
	public static IEnumerable<string> NextInList(string list)
	{
		var head = 0;
		while (true)
		{
			while (head < list.Length && list[head] == ' ') head++;
			if (head >= list.Length) yield break;

			if (list[head] == '"')
			{
				var close = list.IndexOf('"', head + 1);
				var end = close < 0 ? list.Length : close;
				var quoted = list[(head + 1)..end];
				head = close < 0 ? list.Length : close + 1;
				if (quoted.Length > 0) yield return quoted;
				continue;
			}

			var stop = list.AsSpan(head).IndexOfAny(NextInListBreaks);
			var next = stop < 0 ? list.Length : head + stop;
			yield return list[head..next];
			head = next;
		}
	}
}
