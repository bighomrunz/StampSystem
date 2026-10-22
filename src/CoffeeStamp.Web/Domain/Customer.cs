namespace CoffeeStamp.Web.Domain;

public class Customer
{
    public int Id { get; set; }

    /// <summary>ไอดีที่ใช้ภายนอก เช่นใน QR ของลูกค้า ไม่ใช้ Id ที่เดารูปแบบได้</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// userId จาก LINE ได้มาจากการ verify ID token ฝั่งเซิร์ฟเวอร์เท่านั้น
    /// null = สมาชิกที่พนักงานสร้างให้ที่เคาน์เตอร์และยังไม่ได้ผูกบัญชี LINE
    /// </summary>
    public string? LineUserId { get; set; }

    public string? DisplayName { get; set; }
    public string? PictureUrl { get; set; }

    /// <summary>เบอร์โทร เก็บเฉพาะตัวเลข ใช้ค้นหาที่เคาน์เตอร์และยืนยันตัวตนตอนผูก LINE</summary>
    public string? Phone { get; set; }

    public CustomerSource Source { get; set; } = CustomerSource.Line;

    /// <summary>พนักงานที่สร้างบัญชีนี้ให้ (เฉพาะกรณีสร้างที่เคาน์เตอร์)</summary>
    public int? CreatedByStaffId { get; set; }
    public Staff? CreatedByStaff { get; set; }

    // --- PDPA ---
    /// <summary>เวอร์ชันของข้อความขอความยินยอมที่ลูกค้ากดยอมรับ</summary>
    public string? ConsentVersion { get; set; }
    public DateTime? ConsentAt { get; set; }
    public ConsentSource? ConsentSource { get; set; }

    /// <summary>
    /// step ของ QR ที่ใช้สำเร็จไปแล้ว กัน token เดิมถูกใช้ซ้ำ
    /// (หัวใจของการกันเคสแคป QR ส่งต่อ)
    /// </summary>
    public long LastUsedQrStep { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    public CustomerBalance? Balance { get; set; }

    public bool HasConsented(string currentVersion) =>
        ConsentAt is not null &&
        string.Equals(ConsentVersion, currentVersion, StringComparison.Ordinal);

    /// <summary>ผูกบัญชี LINE แล้วหรือยัง ใช้กันการผูกซ้ำ</summary>
    public bool IsLineLinked => !string.IsNullOrEmpty(LineUserId);

    /// <summary>ชื่อที่เอาไปแสดงให้พนักงานเห็น</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DisplayName) ? $"สมาชิก #{Id}" : DisplayName;
}
