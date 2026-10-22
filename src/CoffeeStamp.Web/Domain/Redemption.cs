namespace CoffeeStamp.Web.Domain;

/// <summary>ที่มาของการแลก ใช้แยกบัตรกระดาษช่วงเปลี่ยนผ่านออกจากแต้มในระบบ</summary>
public enum RedemptionSource
{
    /// <summary>ตัดแต้มจาก ledger ตามปกติ</summary>
    Stamps = 1,

    /// <summary>
    /// ลูกค้าเอาบัตรกระดาษเดิมมาแลก ไม่มีแต้มในระบบให้ตัด
    /// ใช้ได้ถึงวันตัดเท่านั้น ต้องมีเหตุผลและผู้อนุมัติ (ดู docs/MIGRATION-PAPER-CARDS.md)
    /// </summary>
    PaperCard = 2
}

/// <summary>การแลกรางวัลหนึ่งครั้ง ผูกกับออเดอร์ที่มีบรรทัดรางวัลเสมอ</summary>
public class Redemption
{
    public long Id { get; set; }

    /// <summary>null เมื่อแลกด้วยบัตรกระดาษเดิม เพราะยังไม่มีสมาชิกในระบบ</summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>ออเดอร์ที่มีบรรทัดรางวัล ราคา 0 หรือส่วนต่างถ้าจ่ายเพิ่ม</summary>
    public long OrderId { get; set; }
    public Order? Order { get; set; }

    public RedemptionSource Source { get; set; } = RedemptionSource.Stamps;

    /// <summary>แต้มที่ใช้ไป 0 ได้เฉพาะกรณีแลกด้วยบัตรกระดาษ</summary>
    public int StampsUsed { get; set; }

    /// <summary>เมนูที่แลก เก็บชื่อและราคาไว้ด้วยเพราะเมนูแก้ไขภายหลังได้</summary>
    public int? MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }
    public string MenuNameSnapshot { get; set; } = "";
    public decimal MenuPriceSnapshot { get; set; }

    /// <summary>ส่วนต่างที่ลูกค้าจ่ายเพิ่มเมื่อเมนูเกินเพดาน (ใช้เมื่อ AllowRewardTopUp)</summary>
    public decimal TopUpAmount { get; set; }

    public int StaffId { get; set; }
    public Staff? Staff { get; set; }

    /// <summary>ผู้อนุมัติ จำเป็นเมื่อแลกด้วยบัตรกระดาษ</summary>
    public int? ApprovedByStaffId { get; set; }

    public Guid? DeviceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
