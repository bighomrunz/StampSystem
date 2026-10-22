namespace CoffeeStamp.Web.Domain;

/// <summary>
/// Ledger ของแต้ม เป็น append-only ห้าม UPDATE หรือ DELETE
/// การแก้ไขทำโดยเพิ่มรายการ Void หรือ Adjust เท่านั้น
/// </summary>
public class StampTransaction
{
    public long Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public StampTransactionType Type { get; set; }

    /// <summary>บวกสำหรับ Earn, ลบสำหรับ Redeem/Void/Expire, Adjust ได้ทั้งสองทาง</summary>
    public int Quantity { get; set; }

    /// <summary>ออเดอร์ที่ทำให้เกิดรายการนี้ ทุกแต้มที่ออกต้องมีออเดอร์จริง</summary>
    public long? OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>การแลกรางวัลที่ทำให้เกิดรายการนี้ (เฉพาะรายการ Redeem)</summary>
    public long? RedemptionId { get; set; }
    public Redemption? Redemption { get; set; }

    /// <summary>วันหมดอายุของรายการ Earn นี้ (null = ไม่หมดอายุ) ใช้ตัดแบบ FIFO ใน Phase 3</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>พนักงานที่รับผิดชอบรายการนี้ null ได้เฉพาะงานที่ระบบทำเอง เช่น job หมดอายุ</summary>
    public int? StaffId { get; set; }
    public Staff? Staff { get; set; }

    /// <summary>
    /// ผู้อนุมัติ สำหรับรายการที่ไม่ได้เกิดจากออเดอร์ เช่นการย้ายบัตรกระดาษ
    /// (เทียบกับ Order.VoidApprovedByStaffId)
    /// </summary>
    public int? ApprovedByStaffId { get; set; }

    /// <summary>ประเภทของ Adjust ดู AdjustKind — null สำหรับรายการประเภทอื่น</summary>
    public string? AdjustKind { get; set; }

    public Guid? DeviceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired(DateTime asOfUtc) => ExpiresAt is { } at && at <= asOfUtc;
}
