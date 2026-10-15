using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The kept activity from <paramref name="FromDay"/> to <paramref name="ToDay"/> (UTC day numbers, both
/// included). Not cached: only the staff reports read it, each over a different window.
/// </summary>
public record GetActivityQuery(int FromDay, int ToDay) : IQuery<ActivityWindow>;
