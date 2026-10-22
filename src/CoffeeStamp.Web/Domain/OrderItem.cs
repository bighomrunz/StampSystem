namespace CoffeeStamp.Web.Domain;

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>อ้างเมนูไว้ทำรายงาน แต่ชื่อและราคาที่ใช้คิดเงินเป็นค่า snapshot เสมอ</summary>
    public int? MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }

    public string NameSnapshot { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>บรรทัดนี้คือแก้วที่แลกด้วยแต้ม ราคา 0 หรือส่วนต่าง และไม่ได้แต้ม</summary>
    public bool IsReward { get; set; }

    /// <summary>ค่า EarnsStamp ของเมนูตอนขาย กันผลกระทบเมื่อแอดมินแก้เมนูทีหลัง</summary>
    public bool EarnsStampSnapshot { get; set; } = true;
}
