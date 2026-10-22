namespace CoffeeStamp.Web.Domain;

/// <summary>ตารางตั้งค่าแบบ singleton มีแถวเดียวคือ Id = 1</summary>
public class ShopSettings
{
    public const int SingletonId = 1;

    /// <summary>
    /// ชื่อร้านที่ใช้ตอนยังไม่มีค่าในฐานข้อมูล
    /// ชื่อจริงของร้านตั้งที่หน้า ตั้งค่า → ชื่อร้าน ที่เดียว แล้วไหลไปทุกหน้าเอง
    /// </summary>
    public const string DefaultShopName = "MInDCoffee";

    public int Id { get; set; } = SingletonId;

    public string ShopName { get; set; } = DefaultShopName;

    // --- พร้อมเพย์ ---
    public string? PromptPayId { get; set; }
    public PromptPayIdType PromptPayIdType { get; set; } = PromptPayIdType.Mobile;

    /// <summary>ชื่อผู้รับเงินที่ฝังไปใน QR ไม่บังคับ</summary>
    public string? PromptPayMerchantName { get; set; }

    // --- กฎแต้ม เก็บไว้ที่เดียวตั้งแต่ Phase 1 แม้จะใช้จริงใน Phase 2-3 ---
    public int StampsPerReward { get; set; } = 10;

    /// <summary>0 = ไม่หมดอายุ การเปลี่ยนค่ามีผลกับแต้มที่ออกใหม่เท่านั้น</summary>
    public int StampExpiryMonths { get; set; } = 12;

    public RewardMode RewardMode { get; set; } = RewardMode.AllItems;
    public decimal? RewardPriceCap { get; set; }
    public bool AllowRewardTopUp { get; set; }
    public int MaxStampsPerOrder { get; set; } = 6;
    public int MaxStampsPerDayPerCustomer { get; set; } = 6;

    // --- เวลาเปิดร้าน ใช้ตรวจรายการนอกเวลาใน anomaly report ---
    public TimeOnly OpenTime { get; set; } = new(7, 0);
    public TimeOnly CloseTime { get; set; } = new(18, 0);

    /// <summary>ส่วนต่างตอนปิดยอด หน่วยบาท ที่เกินแล้วถือว่าผิดปกติ</summary>
    public decimal VarianceAlertThreshold { get; set; } = 20m;

    /// <summary>นาทีที่ไม่มีการใช้งานแล้วล็อกเอาต์พนักงานอัตโนมัติ</summary>
    public int StaffIdleTimeoutMinutes { get; set; } = 15;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByStaffId { get; set; }
}
