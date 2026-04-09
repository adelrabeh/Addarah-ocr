using DarahOcr.Data;
using DarahOcr.Models;

namespace DarahOcr.Services;

public class AuditService(AppDbContext db)
{
    public async Task LogAsync(
        int? userId,
        string? username,
        string action,
        string? resourceType = null,
        int? resourceId = null,
        string? details = null,
        string? ipAddress = null)
    {
        var log = new AuditLog
        {
            UserId = userId,
            Username = username,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details?.Length > 2000 ? details[..2000] : details,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        };
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();
    }
}
