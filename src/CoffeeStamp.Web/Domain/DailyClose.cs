namespace CoffeeStamp.Web.Domain;

/// <summary>ปิดยอดรายวัน หนึ่งแถวต่อหนึ่งวันทำการ ปิดแล้วออเดอร์ของวันนั้นแก้ไขไม่ได้</summary>
public class DailyClose
{
    public DateOnly BusinessDate { get; set; }

    public decimal ExpectedCash { get; set; }
    public decimal ExpectedPromptPay { get; set; }
    public decimal ExpectedPaoTang { get; set; }

    public decimal ActualCash { get; set; }
    public decimal ActualPromptPay { get; set; }
    public decimal ActualPaoTang { get; set; }

    public decimal VarianceCash { get; set; }
    public decimal VariancePromptPay { get; set; }
    public decimal VariancePaoTang { get; set; }
    public decimal VarianceTotal { get; set; }

    public int OrderCount { get; set; }
    public int VoidCount { get; set; }

    public int ClosedByStaffId { get; set; }
    public Staff? ClosedByStaff { get; set; }
    public DateTime ClosedAt { get; set; }
    public string? Note { get; set; }

    public decimal ExpectedTotal => ExpectedCash + ExpectedPromptPay + ExpectedPaoTang;
    public decimal ActualTotal => ActualCash + ActualPromptPay + ActualPaoTang;
}
