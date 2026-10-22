namespace CoffeeStamp.Web.Domain;

/// <summary>
/// ยอดแต้มคงเหลือแบบ denormalized เพื่ออ่านเร็วและใช้เป็นจุดล็อกกันแลกซ้อน
/// ค่าจริงต้องเท่ากับผลรวมของ StampTransaction ของลูกค้าคนนั้นเสมอ
/// ใช้ xmin ของ PostgreSQL เป็น concurrency token (ดู AppDbContext)
/// </summary>
public class CustomerBalance
{
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int Stamps { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
