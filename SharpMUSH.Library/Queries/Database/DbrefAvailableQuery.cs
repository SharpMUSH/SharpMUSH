using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Whether a dbref is one a build could take right now — the read-only half of PennMUSH's
/// <c>make_first_free_wrapper</c> (<c>src/destroy.c:939-947</c>), asked before a multi-object build
/// creates anything so a request it cannot honour leaves nothing behind.
/// </summary>
/// <remarks>
/// Deliberately not <c>ICacheable</c>. The answer is "this id holds no object", which is exactly the
/// state a cached miss describes and exactly the state the next build changes; caching it would hand
/// a second caller a slot the first has already taken. It is also advisory either way — the authority
/// is the availability check inside the write transaction that takes the id.
/// </remarks>
/// <param name="Requested">The dbref to ask about</param>
public record DbrefAvailableQuery(DBRef Requested) : IQuery<bool>;
