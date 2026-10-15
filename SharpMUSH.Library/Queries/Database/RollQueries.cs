using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The roll kept under <paramref name="Id"/>. Not cached: a roll is read when someone checks one, rarely
/// twice.
/// </summary>
public record GetRollQuery(long Id) : IQuery<Found<RecordedRoll>>;
