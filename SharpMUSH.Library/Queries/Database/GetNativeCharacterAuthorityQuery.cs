using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Never cached: native authorization must see current ownership, status, full identity and credential epochs.</summary>
public record GetNativeCharacterAuthorityQuery(string AccountId, DBRef Character) : IQuery<Found<NativeCharacterAuthority>>;
