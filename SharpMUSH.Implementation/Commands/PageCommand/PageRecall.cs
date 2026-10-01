using Mediator;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Commands.PageCommand;

/// <summary>
/// Reading one's own page log in game: <c>page/recall</c>, <c>page/conversations</c>, <c>pagerecall()</c>
/// and <c>pageconversations()</c>, a SharpMUSH extension (PennMUSH keeps no page log). The command and the
/// function share the parsing, the matching and the reads, and differ only in how they answer.
/// </summary>
/// <remarks>
/// A recalled page is shown as the line its reader was shown when it was delivered, the default line
/// <see cref="PageText"/> builds: one's own page as the pager read it, anyone else's as a recipient read
/// it. <c>PAGEFORMAT</c> and <c>OUTPAGEFORMAT</c> are not run again: recall is display only, and runs no
/// softcode and raises no event.
/// </remarks>
public static class PageRecall
{
	/// <summary>How many pages a recall shows when not asked for a number, as <c>@channel/recall</c>.</summary>
	public const int DefaultLines = 10;

	/// <summary>The most a recall shows, the portal's recall cap.</summary>
	public const int MaxLines = CommLimits.PageRecallMaxLines;

	/// <summary>The most conversations a listing shows, the portal's listing cap.</summary>
	public const int MaxConversations = CommLimits.PageConversationListMax;

	/// <summary>
	/// The number of pages to show: <see cref="DefaultLines"/> for nothing, a whole number up to
	/// <see cref="MaxLines"/>, and 0 for as many as allowed (<c>@channel/recall</c>'s "the whole buffer").
	/// Anything else is refused.
	/// </summary>
	public static bool TryParseLines(string text, out int lines)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			lines = DefaultLines;
			return true;
		}

		if (!int.TryParse(text.Trim(), out var requested) || requested < 0)
		{
			lines = 0;
			return false;
		}

		lines = requested == 0 ? MaxLines : Math.Min(requested, MaxLines);
		return true;
	}

	/// <summary>
	/// Finds the people <paramref name="list"/> names, for <paramref name="viewer"/>'s own log. The first name
	/// that finds nobody, or more than one, is the answer.
	/// </summary>
	/// <remarks>
	/// A name is matched first as <c>page</c> finds a recipient. A conversation can outlive its partner's
	/// name, though: they were renamed, destroyed, their dbref went to someone new, or they were an object
	/// (which <c>page</c> never finds). So a name that finds no one, or finds a player in none of the
	/// viewer's conversations, is then looked for among the viewer's own conversation partners: a full
	/// objid (<c>#dbref:ctime</c>, what <c>pageconversations()</c> returns) matches that partner exactly, and
	/// a whole name matches the name the log gave a partner. A bare <c>#dbref</c> never matches a logged
	/// partner, so a recycled dbref's new holder never reaches the old holder's pages. Only the viewer's own
	/// conversations are searched, so this reveals nothing of anyone else's log.
	/// </remarks>
	public static async ValueTask<PagePartnerMatch> MatchAsync(IMediator mediator, IConnectionService connections,
		DBRef viewer, string list)
	{
		Dictionary<DBRef, string>? logged = null;
		async ValueTask<Dictionary<DBRef, string>> LoggedPartners()
			=> logged ??= (await mediator.Send(new GetPageConversationsQuery(viewer)))
				.SelectMany(conversation => conversation.With.Zip(conversation.Names))
				.DistinctBy(member => member.First)
				.ToDictionary(member => member.First, member => member.Second);

		var partners = new List<PagePartner>();
		foreach (var name in PageRecipients.NextInList(list))
		{
			var live = await PageRecipients.ResolveAsync(mediator, connections, name);
			if (live is AnySharpObject found)
			{
				var objid = found.Object().DBRef;
				var known = await LoggedPartners();
				partners.Add(objid == viewer || known.ContainsKey(objid)
					? new PagePartner(objid, found.Object().Name)
					: InLog(known, name) is [var remembered]
						? remembered
						: new PagePartner(objid, found.Object().Name));
				continue;
			}

			switch (InLog(await LoggedPartners(), name))
			{
				case [var remembered]:
					partners.Add(remembered);
					break;
				case []:
					return new UnmatchedPartner(name, Ambiguous: live is AmbiguousName);
				default:
					return new UnmatchedPartner(name, Ambiguous: true);
			}
		}

		return new MatchedPartners([.. partners.DistinctBy(partner => partner.Objid)]);
	}

	/// <summary>
	/// The viewer's logged partners <paramref name="name"/> names: the one with that full objid, or every one
	/// the log gave that whole name (case-insensitively).
	/// </summary>
	private static PagePartner[] InLog(Dictionary<DBRef, string> logged, string name)
	{
		if (name.StartsWith('#'))
		{
			return DBRef.TryParse(name, out var objid) && objid!.Value.CreationMilliseconds is not null
					&& logged.TryGetValue(objid.Value, out var loggedName)
				? [new PagePartner(objid.Value, loggedName)]
				: [];
		}

		var lookup = name.StartsWith('*') ? name[1..] : name;
		return [.. logged
			.Where(member => member.Value.Equals(lookup, StringComparison.OrdinalIgnoreCase))
			.Select(member => new PagePartner(member.Key, member.Value))];
	}

	/// <summary>
	/// The last <paramref name="lines"/> pages of <paramref name="viewer"/>'s own conversation with
	/// <paramref name="partners"/>, or across all of their conversations when there are none; oldest first.
	/// </summary>
	public static async ValueTask<IReadOnlyList<SharpPage>> ReadAsync(IMediator mediator, DBRef viewer,
		IReadOnlyList<PagePartner> partners, int lines)
		=> partners.Count == 0
			? await mediator.Send(new GetRecentPagesQuery(viewer, lines))
			: await mediator.Send(new GetPageLogQuery(viewer, [.. partners.Select(partner => partner.Objid)], lines));

	/// <summary>
	/// <paramref name="page"/> as <paramref name="viewer"/> was shown it: as the pager when they sent it,
	/// and otherwise as a recipient. A page to oneself is shown once, as sent.
	/// </summary>
	public static MString Line(SharpPage page, DBRef viewer)
		=> page.Sender == viewer
			? PageText.Outgoing(page.SenderPlainName ?? page.SenderName, page.RecipientNames, page.Style,
				MarkupText.Plain(page.Message))
			: PageText.Incoming(page.SenderName, page.RecipientNames, page.Style, MarkupText.Plain(page.Message));

	/// <summary>
	/// <see cref="Line"/> with the time it was sent, stamped as <c>@channel/recall</c> stamps a line
	/// (<see cref="ChannelCommand.ChannelRecall.Stamped"/>).
	/// </summary>
	public static MString Stamped(SharpPage page, DBRef viewer)
		=> MarkupText.Concat(
			MarkupText.Plain($"[{TimeFormatting.ShowTime(page.Timestamp)}] "),
			Line(page, viewer));

	/// <summary>The others in a conversation, named for its reader: "yourself" for pages to oneself.</summary>
	public static string Describe(IReadOnlyList<string> names, bool toSelf)
		=> toSelf ? "yourself" : Library.Common.MessageFormatting.FormatWithOxfordComma(names);
}

/// <summary>One person a <c>page/recall</c> list named: their objid, and the name to call them by.</summary>
/// <param name="Name">Their name now when they were found live, or the name the log gave them.</param>
public readonly record struct PagePartner(DBRef Objid, string Name);

/// <summary>The people a <c>page/recall</c> list named, each found once.</summary>
public readonly record struct MatchedPartners(PagePartner[] Partners);

/// <summary>A name in a <c>page/recall</c> list that found nobody, or more than one connected player.</summary>
public readonly record struct UnmatchedPartner(string Name, bool Ambiguous);

/// <summary>What a <c>page/recall</c> list of names came to.</summary>
public union PagePartnerMatch(MatchedPartners, UnmatchedPartner);
