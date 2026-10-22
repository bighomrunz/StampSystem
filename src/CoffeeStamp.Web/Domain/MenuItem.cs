namespace CoffeeStamp.Web.Domain;

public class MenuItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public decimal Price { get; set; }

    /// <summary>เมนูนี้นับแต้มให้ลูกค้าหรือไม่ แต้มคิดจาก OrderItem.Quantity</summary>
    public bool EarnsStamp { get; set; } = true;

    /// <summary>ใช้แลกรางวัลได้หรือไม่ มีผลเมื่อ RewardMode = SelectedItems</summary>
    public bool IsRewardEligible { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
