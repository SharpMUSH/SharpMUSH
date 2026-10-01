using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The page log (the <c>page_log</c> option, a SharpMUSH extension; PennMUSH keeps none): each delivered
/// page, kept for every participant as that participant's own copy. A copy belongs to a character by
/// objid, so a player who takes a recycled dbref reads none of the previous holder's, and destroying a
/// character drops its copies. Every read is of one character's own copies: there is no read across
/// characters, for staff or anyone else.
/// </summary>
public interface IPageLogStore
{
	/// <summary>
	/// Keeps <paramref name="page"/> for each of <paramref name="owners"/> (objids, each a participant), in
	/// one write.
	/// </summary>
	/// <exception cref="ArgumentException">An owner is not an objid, or not in the page.</exception>
	ValueTask RecordPageAsync(SharpPage page, IReadOnlyList<DBRef> owners, CancellationToken cancellationToken = default);

	/// <summary>
	/// The last <paramref name="lines"/> pages of <paramref name="character"/>'s conversation with
	/// <paramref name="with"/> (the others in it, in any order), oldest first.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="character"/> is not an objid.</exception>
	ValueTask<IReadOnlyList<SharpPage>> GetPageLogAsync(DBRef character, IReadOnlyList<DBRef> with, int lines,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// The last <paramref name="lines"/> pages across all of <paramref name="character"/>'s conversations (0
	/// for all), oldest first.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="character"/> is not an objid.</exception>
	ValueTask<IReadOnlyList<SharpPage>> GetRecentPagesAsync(DBRef character, int lines,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// <paramref name="character"/>'s logged conversations, the latest first (by their last page's id); at
	/// most <paramref name="limit"/> of them, read without reading the rest, or all for 0.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="character"/> is not an objid.</exception>
	ValueTask<IReadOnlyList<SharpPageConversation>> GetPageConversationsAsync(DBRef character, int limit = 0,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes every logged page sent before <paramref name="before"/>, everyone's copy, and every
	/// conversation left with none; returns how many copies went.
	/// </summary>
	ValueTask<int> PurgePageLogAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
