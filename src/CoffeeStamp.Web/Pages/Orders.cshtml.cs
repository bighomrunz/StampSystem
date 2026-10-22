using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages;

public class OrdersModel : PageModel
{
    private readonly OrderService _orders;
    private readonly DailyCloseService _dailyClose;
    private readonly StaffContext _staff;

    public OrdersModel(OrderService orders, DailyCloseService dailyClose, StaffContext staff)
    {
        _orders = orders;
        _dailyClose = dailyClose;
        _staff = staff;
    }

    public DateOnly BusinessDate { get; private set; } = BangkokTime.Today();
    public List<Order> Orders { get; private set; } = [];
    public bool IsClosed { get; private set; }
    public string? LoadError { get; private set; }
    public bool IsManager => _staff.IsManagerOrAbove;

    /// <summary>ออกแต้มย้อนหลังได้เฉพาะวันทำการเดียวกันเท่านั้น</summary>
    public bool IsToday => BusinessDate == BangkokTime.Today();

    public decimal PaidTotal => Orders.Where(o => o.Status == OrderStatus.Paid).Sum(o => o.Total);
    public int PaidCount => Orders.Count(o => o.Status == OrderStatus.Paid);
    public int PendingCount => Orders.Count(o => o.Status == OrderStatus.Pending);
    public int VoidedCount => Orders.Count(o => o.Status == OrderStatus.Voided);

    public async Task OnGetAsync(string? date, CancellationToken ct)
    {
        if (DateOnly.TryParse(date, out var parsed))
            BusinessDate = parsed;

        try
        {
            Orders = await _orders.GetForDateAsync(BusinessDate, ct);
            var summary = await _dailyClose.GetSummaryAsync(BusinessDate, ct);
            IsClosed = summary.IsClosed;
        }
        catch (Exception)
        {
            LoadError = "โหลดข้อมูลออเดอร์ไม่สำเร็จ กรุณาลองใหม่";
        }
    }

    public string StatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "รอรับเงิน",
        OrderStatus.Paid => "รับเงินแล้ว",
        OrderStatus.Cancelled => "ยกเลิกก่อนจ่าย",
        OrderStatus.Voided => "ยกเลิกหลังจ่าย",
        _ => status.ToString()
    };

    public string StatusClass(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "badge-pending",
        OrderStatus.Paid => "badge-paid",
        OrderStatus.Cancelled => "badge-cancelled",
        OrderStatus.Voided => "badge-void",
        _ => "badge-cancelled"
    };

    public string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "เงินสด",
        PaymentMethod.PromptPay => "พร้อมเพย์",
        PaymentMethod.PaoTang => "เป๋าตัง",
        _ => method.ToString()
    };
}
