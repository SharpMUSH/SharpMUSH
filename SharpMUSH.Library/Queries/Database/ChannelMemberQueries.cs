using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// <paramref name="Member"/>'s standing on <paramref name="Channel"/>, or <see cref="NotFound"/> when it is
/// not a member: one point read of the membership row rather than a walk of every member. A member whose
/// object is gone, or whose objid no longer matches, is not a member. Uncached, like
/// <see cref="SharpChannel.Members"/>, which it answers for one object.
/// </summary>
public record GetChannelMemberStatusQuery(SharpChannel Channel, DBRef Member) : IQuery<Found<SharpChannelStatus>>;

/// <summary>
/// How many members <paramref name="Channel"/> has, counted from its membership keys without loading a
/// member; a membership row whose object is gone is not counted. Uncached, like
/// <see cref="SharpChannel.Members"/>.
/// </summary>
public record GetChannelMemberCountQuery(SharpChannel Channel) : IQuery<int>;
