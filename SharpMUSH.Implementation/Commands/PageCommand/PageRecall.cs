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
	/// Finds the people <paramref name="list"/> names, each as <c>page</c> finds a recipient. The first name
	/// that finds nobody, or more than one, is the answer.
	/// </summary>
	public static async ValueTask<PagePartnerMatch> MatchAsync(IMediator mediator, IConnectionService connections,
		string list)
	{
		var partners = new List<AnySharpObject>();
		foreach (var name in PageRecipients.NextInList(list))
		{
			switch (await PageRecipients.ResolveAsync(mediator, connections, name))
			{
				case AnySharpObject partner:
					partners.Add(partner);
					break;
				case AmbiguousName:
					return new UnmatchedPartner(name, Ambiguous: true);
				default:
					return new UnmatchedPartner(name, Ambiguous: false);
			}
		}

		return new MatchedPartners([.. partners]);
	}

	/// <summary>
	/// The last <paramref name="lines"/> pages of <paramref name="viewer"/>'s own conversation with
	/// <paramref name="partners"/>, or across all of their conversations when there are none; oldest first.
	/// </summary>
	public static async ValueTask<IReadOnlyList<SharpPage>> ReadAsync(IMediator mediator, DBRef viewer,
		IReadOnlyList<AnySharpObject> partners, int lines)
		=> partners.Count == 0
			? await mediator.Send(new GetRecentPagesQuery(viewer, lines))
			: await mediator.Send(new GetPageLogQuery(viewer, [.. partners.Select(partner => partner.Object().DBRef)], lines));

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

/// <summary>The people a <c>page/recall</c> list named, each found.</summary>
public readonly record struct MatchedPartners(AnySharpObject[] Partners);

/// <summary>A name in a <c>page/recall</c> list that found nobody, or more than one connected player.</summary>
public readonly record struct UnmatchedPartner(string Name, bool Ambiguous);

/// <summary>What a <c>page/recall</c> list of names came to.</summary>
public union PagePartnerMatch(MatchedPartners, UnmatchedPartner);
