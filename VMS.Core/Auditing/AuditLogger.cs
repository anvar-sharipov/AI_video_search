using Microsoft.EntityFrameworkCore;
using VMS.Core.Data;
using VMS.Core.Domain;

namespace VMS.Core.Auditing;

public class AuditLogger(VmsDbContext db) : IAuditLogger
{
    public async Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        Guid? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.AuditLog.AsNoTracking().AsQueryable();

        if (userId is not null)
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (from is not null)
        {
            query = query.Where(a => a.Timestamp >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.Timestamp <= to);
        }

        return await query.OrderByDescending(a => a.Timestamp).ToListAsync(cancellationToken);
    }
}
