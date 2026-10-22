namespace CoffeeStamp.Web.Domain;

public class Order
{
    public long Id { get; set; }

    /// <summary>เลขออเดอร์ที่อ่านออก เช่น 20261016-0007 ต้องไม่ซ้ำ</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>วันทำการตามเวลาไทย ใช้เป็นหลักในการปิดยอดและออกแต้ม</summary>
    public DateOnly BusinessDate { get; set; }

    public int StaffId { get; set; }
    public Staff? Staff { get; set; }

    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>
    /// ลูกค้าที่ผูกกับออเดอร์นี้ ตั้งค่าตอนพนักงานสแกน QR ลูกค้าเพื่อออกแต้ม
    /// null = ลูกค้าไม่ได้เป็นสมาชิกหรือยังไม่ได้สแกน
    /// </summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal SubTotal { get; set; }
    public decimal Total { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public DateTime? VoidedAt { get; set; }
    public int? VoidedByStaffId { get; set; }

    /// <summary>ผู้จัดการที่อนุมัติการยกเลิกด้วย PIN อาจไม่ใช่คนเดียวกับคนที่กด</summary>
    public int? VoidApprovedByStaffId { get; set; }
    public string? VoidReason { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    /// <summary>แต้มที่ออเดอร์นี้มีสิทธิ์ได้ แก้วรางวัลไม่นับ ใช้จริงใน Phase 2</summary>
    public int EligibleStampCount =>
        Items.Where(i => i.IsReward == false && i.EarnsStampSnapshot).Sum(i => i.Quantity);
}
