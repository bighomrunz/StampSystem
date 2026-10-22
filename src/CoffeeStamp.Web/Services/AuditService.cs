using System.Text.Json;
using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;

namespace CoffeeStamp.Web.Services;

public class AuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly AppDbContext _db;
    private readonly StaffContext _staff;

    public AuditService(AppDbContext db, StaffContext staff)
    {
        _db = db;
        _staff = staff;
    }

    /// <summary>
    /// ใส่รายการ audit เข้า change tracker เพื่อให้บันทึกใน transaction เดียวกับ action
    /// ผู้เรียกเป็นคนสั่ง SaveChanges
    /// </summary>
    public AuditLog Add(string action, string entityType, string? entityId, object? data = null,
        int? staffId = null)
    {
        var log = new AuditLog
        {
            StaffId = staffId ?? _staff.StaffId,
            DeviceId = _staff.DeviceId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            IpAddress = _staff.IpAddress,
            CreatedAt = DateTime.UtcNow
        };
        _db.AuditLogs.Add(log);
        return log;
    }

    public async Task LogAsync(string action, string entityType, string? entityId, object? data = null,
        int? staffId = null, CancellationToken ct = default)
    {
        Add(action, entityType, entityId, data, staffId);
        await _db.SaveChangesAsync(ct);
    }
}
