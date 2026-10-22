using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages;

public class StampModel : PageModel
{
    private readonly StaffContext _staff;

    public StampModel(StaffContext staff) => _staff = staff;

    /// <summary>ไม่มี claim อุปกรณ์ = ล็อกอินมาในโหมดติดตั้ง ยังสแตมป์ไม่ได้</summary>
    public bool HasDevice => _staff.DeviceId is not null;

    public bool IsManager => _staff.IsManagerOrAbove;

    public void OnGet() { }
}
