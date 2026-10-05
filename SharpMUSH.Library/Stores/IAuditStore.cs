using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The audit log: every staff action, kept in the order taken. Append-only apart from retention, which
/// purges by age through the store's <see cref="Services.Interfaces.IHistoryStore"/> kind <c>audit</c>.
/// </summary>
public interface IAuditStore
{
	/// <summary>Keeps <paramref name="draft"/> and returns it with its id.</summary>
	ValueTask<AuditEntry> AppendAuditAsync(AuditDraft draft, CancellationToken cancellationToken = default);

	/// <summary>
	/// The entries <paramref name="filter"/> matches, newest first, reading from <see cref="AuditFilter.To"/>
	/// (or the newest) and stopping at <see cref="AuditFilter.From"/>.
	/// </summary>
	ValueTask<AuditPage> GetAuditEntriesAsync(AuditFilter filter, CancellationToken cancellationToken = default);
}
