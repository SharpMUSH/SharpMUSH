using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Everything whose home is <paramref name="Home"/> — players and things that go there on
/// <c>home</c>, plus exits that lead there. Rooms are excluded; see
/// <see cref="INavigationStore.GetHomedAtAsync"/>.
/// </summary>
/// <remarks>
/// Deliberately not <c>ICacheable</c>. Its only caller is object destruction, which must see the
/// current set — a stale hit here rehomes the wrong objects and leaves live ones pointing at a row
/// that is about to disappear.
/// </remarks>
public record GetHomedAtQuery(DBRef Home) : IStreamQuery<AnySharpContent>;
