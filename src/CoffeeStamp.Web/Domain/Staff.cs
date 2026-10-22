namespace CoffeeStamp.Web.Domain;

public class Staff
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>PIN ส่วนตัว เก็บเป็น hash ด้วย ASP.NET Identity PasswordHasher เท่านั้น</summary>
    public string PinHash { get; set; } = "";

    public StaffRole Role { get; set; } = StaffRole.Staff;
    public bool IsActive { get; set; } = true;

    /// <summary>บังคับเปลี่ยน PIN ครั้งแรก สำหรับบัญชีที่ถูก seed มาพร้อม PIN ตั้งต้น</summary>
    public bool MustChangePin { get; set; }

    public int FailedPinCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsManagerOrAbove => Role is StaffRole.Manager or StaffRole.Owner;
}
