namespace CoffeeStamp.Web.Domain;

/// <summary>อุปกรณ์ที่อนุญาตให้ใช้แอปพนักงาน ต้องลงทะเบียนก่อนจึงจะล็อกอินได้</summary>
public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>รหัสลงทะเบียนแบบใช้ครั้งเดียว ผู้จัดการสร้างให้แล้วนำไปกรอกที่เครื่องนั้น</summary>
    public string? RegistrationCode { get; set; }

    public DateTime? RegisteredAt { get; set; }
    public int? RegisteredByStaffId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRegistered => RegisteredAt is not null;
}
