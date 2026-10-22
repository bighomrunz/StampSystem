namespace CoffeeStamp.Web.Domain;

/// <summary>ช่องทางชำระเงินที่ร้านรับ (ระบบนี้ทำหน้าที่เป็น mini-POS เอง)</summary>
public enum PaymentMethod
{
    Cash = 1,
    PromptPay = 2,
    PaoTang = 3
}

public enum OrderStatus
{
    /// <summary>สร้างออเดอร์แล้วแต่ยังไม่ยืนยันว่ารับเงิน ใช้กับ PromptPay และเป๋าตัง</summary>
    Pending = 0,
    Paid = 1,
    /// <summary>ยกเลิกก่อนรับเงิน ไม่นับยอดขาย ไม่ต้องใช้ PIN ผู้จัดการ</summary>
    Cancelled = 2,
    /// <summary>ยกเลิกหลังรับเงินแล้ว ต้องใช้ PIN ผู้จัดการและเหตุผล</summary>
    Voided = 3
}

public enum StaffRole
{
    Staff = 1,
    Manager = 2,
    Owner = 3
}

public enum RewardMode
{
    AllItems = 1,
    SelectedItems = 2,
    PriceCap = 3
}

/// <summary>บัญชีสมาชิกนี้เกิดขึ้นทางไหน</summary>
public enum CustomerSource
{
    /// <summary>พนักงานสร้างให้ที่เคาน์เตอร์ (ช่วงที่ยังไม่เปิด LIFF)</summary>
    Counter = 1,

    /// <summary>ลูกค้าสมัครเองผ่าน LIFF</summary>
    Line = 2
}

/// <summary>ลูกค้าให้ความยินยอม PDPA ผ่านช่องทางไหน</summary>
public enum ConsentSource
{
    /// <summary>พนักงานแจ้งข้อความที่เคาน์เตอร์แล้วลูกค้ายินยอมด้วยวาจา</summary>
    Counter = 1,

    /// <summary>ลูกค้ากดยอมรับเองในหน้าบัตร LIFF</summary>
    Liff = 2
}

/// <summary>ประเภทของรายการ Adjust เพื่อแยกการย้ายบัตรกระดาษออกจากการแก้ไขทั่วไป</summary>
public static class AdjustKind
{
    /// <summary>ย้ายแต้มจากบัตรกระดาษเดิม ทำได้ครั้งเดียวต่อลูกค้า (บังคับด้วย unique index)</summary>
    public const string CardMigration = "CardMigration";

    /// <summary>แก้ไขด้วยมือกรณีอื่น เช่นคีย์ผิด</summary>
    public const string Correction = "Correction";
}

public enum StampTransactionType
{
    /// <summary>ได้แต้มจากการซื้อ ผูกกับออเดอร์จริงเสมอ</summary>
    Earn = 1,

    /// <summary>ใช้แต้มแลกรางวัล (Phase 3)</summary>
    Redeem = 2,

    /// <summary>ย้อนแต้มเพราะออเดอร์ถูกยกเลิก</summary>
    Void = 3,

    /// <summary>แต้มหมดอายุ ออกโดย job รายวัน (Phase 3)</summary>
    Expire = 4,

    /// <summary>ปรับด้วยมือ เช่นย้ายข้อมูลจากใบคะแนนเดิม ต้องมีผู้อนุมัติ (Phase 4)</summary>
    Adjust = 5
}

/// <summary>ชนิดเลขพร้อมเพย์ที่ใช้ใน tag 29 ของ EMVCo payload</summary>
public enum PromptPayIdType
{
    Mobile = 1,
    NationalId = 2,
    EWalletId = 3
}
