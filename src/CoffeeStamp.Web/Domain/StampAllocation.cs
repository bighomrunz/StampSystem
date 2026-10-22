namespace CoffeeStamp.Web.Domain;

/// <summary>
/// บอกว่าแต้มที่ถูกใช้ไป (แลก/หมดอายุ/ย้อน) ไปตัดมาจากก้อนไหน
/// ทำให้ตัดแบบ FIFO ได้จริงและตรวจย้อนได้ว่าแต้มก้อนที่กำลังจะหมดอายุถูกใช้ไปหรือยัง
///
/// ก้อนต้นทาง = รายการที่ quantity เป็นบวก (Earn หรือ Adjust ที่เพิ่มแต้ม)
/// ก้อนปลายทาง = รายการที่ quantity เป็นลบ (Redeem, Expire, Void)
/// </summary>
public class StampAllocation
{
    public long Id { get; set; }

    /// <summary>รายการที่ใช้แต้ม (ด้านลบ)</summary>
    public long ConsumingTransactionId { get; set; }
    public StampTransaction? ConsumingTransaction { get; set; }

    /// <summary>รายการที่เป็นที่มาของแต้ม (ด้านบวก)</summary>
    public long SourceTransactionId { get; set; }
    public StampTransaction? SourceTransaction { get; set; }

    /// <summary>จำนวนแต้มที่ตัดจากก้อนนี้ เป็นค่าบวกเสมอ</summary>
    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
