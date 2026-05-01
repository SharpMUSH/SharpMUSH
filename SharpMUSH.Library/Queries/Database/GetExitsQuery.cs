using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetExitsQuery(DbRefOrContainer DBRef)
	: IStreamQuery<SharpExit>;
