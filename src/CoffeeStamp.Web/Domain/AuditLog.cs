namespace CoffeeStamp.Web.Domain;

/// <summary>บันทึกทุก action ที่เกี่ยวกับเงิน แต้ม การตั้งค่า และการ override แบบ append-only</summary>
public class AuditLog
{
    public long Id { get; set; }
    public int? StaffId { get; set; }
    public Guid? DeviceId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? DataJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
