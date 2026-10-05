using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The audit entries <paramref name="Filter"/> matches, newest first. Not cached: the log grows with
/// every staff action and only the audit viewer reads it.
/// </summary>
public record GetAuditEntriesQuery(AuditFilter Filter) : IQuery<AuditPage>;
