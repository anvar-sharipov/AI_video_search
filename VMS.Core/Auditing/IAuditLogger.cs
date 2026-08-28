using VMS.Core.Domain;

namespace VMS.Core.Auditing;

/// <summary>
/// Append-only by design: intentionally offers no Update/Delete. Anything that
/// needs to "correct" the audit trail should append a new compensating entry instead.
/// </summary>
public interface IAuditLogger
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        Guid? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}
