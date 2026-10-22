using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages.Staff;

public class LoginModel : PageModel
{
    private readonly StaffAuthService _auth;
    private readonly DeviceService _devices;
    private readonly SettingsService _settings;

    public LoginModel(StaffAuthService auth, DeviceService devices, SettingsService settings)
    {
        _auth = auth;
        _devices = devices;
        _settings = settings;
    }

    public record StaffOption(int Id, string Name);

    public List<StaffOption> StaffList { get; private set; } = [];
    public string ShopName { get; private set; } = ShopSettings.DefaultShopName;
    public bool DeviceAllowed { get; private set; }
    public bool IsBootstrap { get; private set; }
    public string? LoadError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            ShopName = (await _settings.GetAsync(ct)).ShopName;

            IsBootstrap = await _devices.IsBootstrapAsync(ct);
            var deviceId = Request.Cookies.TryGetValue(DeviceService.CookieName, out var raw)
                           && Guid.TryParse(raw, out var parsed)
                ? parsed
                : (Guid?)null;

            DeviceAllowed = IsBootstrap || await _devices.GetActiveAsync(deviceId, ct) is not null;

            if (DeviceAllowed)
            {
                var staff = await _auth.GetActiveStaffAsync(ct);
                StaffList = staff.Select(s => new StaffOption(s.Id, s.Name)).ToList();
            }
        }
        catch (Exception)
        {
            LoadError = "เชื่อมต่อฐานข้อมูลไม่ได้ กรุณาแจ้งผู้ดูแลระบบ";
        }
    }
}
