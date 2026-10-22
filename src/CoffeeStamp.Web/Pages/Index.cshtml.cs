using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages;

public class IndexModel : PageModel
{
    private readonly DailyCloseService _dailyClose;
    private readonly StaffContext _staff;

    public IndexModel(DailyCloseService dailyClose, StaffContext staff)
    {
        _dailyClose = dailyClose;
        _staff = staff;
    }

    public List<DateOnly> UnclosedDates { get; private set; } = [];
    public bool IsManager => _staff.IsManagerOrAbove;

    /// <summary>ไม่มี claim อุปกรณ์ = ล็อกอินมาในโหมดติดตั้ง ยังออกออเดอร์ไม่ได้</summary>
    public bool HasDevice => _staff.DeviceId is not null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!IsManager) return;

        try
        {
            UnclosedDates = await _dailyClose.GetUnclosedPastDatesAsync(ct);
        }
        catch (Exception)
        {
            // หน้าขายต้องเปิดได้เสมอ การเตือนปิดยอดเป็นเรื่องรอง
            UnclosedDates = [];
        }
    }
}
