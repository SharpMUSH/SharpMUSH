using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

// Feed reads (help @feed). Not cached, as the writes in FeedCommands say.

/// <summary>Every kind, by name.</summary>
public record GetFeedKindsQuery : IQuery<IReadOnlyList<SharpFeedKind>>;

public record GetFeedKindQuery(string Kind) : IQuery<Found<SharpFeedKind>>;

/// <summary>A kind's feeds, by key.</summary>
public record GetFeedsQuery(string Kind) : IQuery<IReadOnlyList<SharpFeed>>;

public record GetFeedQuery(string Kind, string Key) : IQuery<Found<SharpFeed>>;

/// <summary>A feed's members, by dbref.</summary>
public record GetFeedMembersQuery(string Kind, string Key) : IQuery<IReadOnlyList<SharpFeedMember>>;

/// <summary>The feeds <paramref name="Member"/> (an objid) is a member of, of <paramref name="Kind"/> or all.</summary>
public record GetMemberFeedsQuery(DBRef Member, string? Kind) : IQuery<IReadOnlyList<(string Kind, string Key)>>;

/// <summary>
/// A feed's newest <paramref name="Count"/> lines (0 for all) with ids above <paramref name="AfterId"/>, oldest
/// first.
/// </summary>
public record GetFeedMessagesQuery(string Kind, string Key, int Count, long AfterId = 0)
	: IQuery<IReadOnlyList<SharpFeedMessage>>;

/// <summary>What each kind's feeds hold. See <see cref="IFeedStore.GetFeedUsageAsync"/>.</summary>
public record GetFeedUsageQuery : IQuery<IReadOnlyList<SharpFeedUsage>>;

public record GetFeedMessageQuery(long Id) : IQuery<Found<SharpFeedMessage>>;

/// <summary>The taps on <paramref name="Kind"/> (or on <c>*</c>), or every tap when null.</summary>
public record GetFeedTapsQuery(string? Kind) : IQuery<IReadOnlyList<SharpFeedTap>>;
