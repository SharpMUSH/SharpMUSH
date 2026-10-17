using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Always reads storage: credential ownership must not be cached across link/unlink decisions.</summary>
public record GetExternalIdentityQuery(ExternalIdentityKey Key) : IQuery<Found<ExternalIdentity>>;

/// <summary>Always reads storage: security management must list the current credentials.</summary>
public record GetExternalIdentitiesQuery(string AccountId) : IQuery<IReadOnlyList<ExternalIdentity>>;
