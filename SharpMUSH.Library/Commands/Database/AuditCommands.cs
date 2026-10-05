using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Keeps one staff action in the audit log; answers with the kept entry. See
/// <see cref="IAuditStore.AppendAuditAsync"/>. Nothing reads the log through the cache, so nothing is
/// invalidated.
/// </summary>
public record RecordAuditCommand(AuditDraft Draft) : ICommand<AuditEntry>;
