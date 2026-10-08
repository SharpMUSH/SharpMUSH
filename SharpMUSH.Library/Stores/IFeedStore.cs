using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Feeds (<c>help @feed</c>): kinds, the feeds of each, their members and their lines, and the taps on each
/// kind. Kind names and feed keys arrive lower case; the store does not change their case.
/// </summary>
public interface IFeedStore
{
	ValueTask<IReadOnlyList<SharpFeedKind>> GetFeedKindsAsync(CancellationToken cancellationToken = default);

	ValueTask<Found<SharpFeedKind>> GetFeedKindAsync(string kind, CancellationToken cancellationToken = default);

	/// <summary>Writes <paramref name="kind"/>, replacing a kind of the same name.</summary>
	ValueTask SetFeedKindAsync(SharpFeedKind kind, CancellationToken cancellationToken = default);

	/// <summary>Deletes a kind with its feeds, their members and lines, and its taps. False when there was none.</summary>
	ValueTask<bool> DeleteFeedKindAsync(string kind, CancellationToken cancellationToken = default);

	/// <summary>A kind's feeds, by key.</summary>
	ValueTask<IReadOnlyList<SharpFeed>> GetFeedsAsync(string kind, CancellationToken cancellationToken = default);

	/// <summary>What each kind's feeds hold, by kind; a kind with no feeds is not listed.</summary>
	ValueTask<IReadOnlyList<SharpFeedUsage>> GetFeedUsageAsync(CancellationToken cancellationToken = default);

	ValueTask<Found<SharpFeed>> GetFeedAsync(string kind, string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes a feed's settings and locks, creating it when it has none yet. Its line count, size and newest id
	/// are the store's own and are kept.
	/// </summary>
	ValueTask SetFeedAsync(SharpFeed feed, CancellationToken cancellationToken = default);

	/// <summary>Deletes a feed with its members and lines. False when there was none.</summary>
	ValueTask<bool> DeleteFeedAsync(string kind, string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Moves a feed to another key of its kind, with its lines (which keep their ids) and members. When a feed is
	/// at <paramref name="to"/> already, the two are merged: its settings and locks stay, and a member of both keeps
	/// that membership. False when there was nothing at <paramref name="from"/>.
	/// </summary>
	ValueTask<bool> RenameFeedAsync(string kind, string from, string to, CancellationToken cancellationToken = default);

	/// <summary>A feed's members, in the order of their dbrefs.</summary>
	ValueTask<IReadOnlyList<SharpFeedMember>> GetFeedMembersAsync(string kind, string key,
		CancellationToken cancellationToken = default);

	/// <summary>The feeds <paramref name="member"/> (an objid) is a member of, as (kind, key), of one kind or all.</summary>
	ValueTask<IReadOnlyList<(string Kind, string Key)>> GetMemberFeedsAsync(DBRef member, string? kind,
		CancellationToken cancellationToken = default);

	/// <summary>Writes a membership (the member is an objid), creating the feed when it has none yet.</summary>
	ValueTask SetFeedMemberAsync(string kind, string key, SharpFeedMember member,
		CancellationToken cancellationToken = default);

	/// <summary>Ends a membership. False when there was none.</summary>
	ValueTask<bool> RemoveFeedMemberAsync(string kind, string key, DBRef member,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores a line and, in the same write, drops the feed's oldest lines until it is inside
	/// <paramref name="limits"/> (<see cref="FeedSettings.MaxMessages"/>, <see cref="FeedSettings.MaxBytes"/> and
	/// <see cref="FeedSettings.MaxAge"/>, every one resolved). Creates the feed when it has none yet.
	/// </summary>
	ValueTask AppendFeedMessageAsync(SharpFeedMessage message, FeedSettings limits,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// A feed's newest <paramref name="count"/> lines (0 for all), oldest first; only lines with an id above
	/// <paramref name="afterId"/>.
	/// </summary>
	ValueTask<IReadOnlyList<SharpFeedMessage>> GetFeedMessagesAsync(string kind, string key, int count, long afterId,
		CancellationToken cancellationToken = default);

	ValueTask<Found<SharpFeedMessage>> GetFeedMessageAsync(long id, CancellationToken cancellationToken = default);

	/// <summary>Drops a feed's lines sent before <paramref name="before"/>, or all of them; returns how many went.</summary>
	ValueTask<int> PurgeFeedAsync(string kind, string key, DateTimeOffset? before,
		CancellationToken cancellationToken = default);

	/// <summary>The taps on <paramref name="kind"/> (or on <c>*</c>), or every tap when null.</summary>
	ValueTask<IReadOnlyList<SharpFeedTap>> GetFeedTapsAsync(string? kind, CancellationToken cancellationToken = default);

	/// <summary>Adds a tap; adding one that is there already changes nothing.</summary>
	ValueTask AddFeedTapAsync(SharpFeedTap tap, CancellationToken cancellationToken = default);

	/// <summary>Removes a tap. False when there was none.</summary>
	ValueTask<bool> RemoveFeedTapAsync(SharpFeedTap tap, CancellationToken cancellationToken = default);
}
