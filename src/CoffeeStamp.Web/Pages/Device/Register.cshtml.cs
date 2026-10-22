using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages.Device;

public class RegisterModel : PageModel
{
    /// <summary>คุกกี้อุปกรณ์อยู่ยาว เพราะร้านใช้แท็บเล็ตเครื่องเดิมทุกวัน</summary>
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    private readonly DeviceService _devices;

    public RegisterModel(DeviceService devices) => _devices = devices;

    [BindProperty]
    public string? Code { get; set; }

    public string? Error { get; private set; }
    public string? RegisteredName { get; private set; }

    public void OnGet(string? code)
    {
        Code = code;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var userAgent = Request.Headers.UserAgent.ToString();
            var device = await _devices.RegisterAsync(Code ?? "", userAgent, ct);

            Response.Cookies.Append(DeviceService.CookieName, device.Id.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.Add(CookieLifetime)
            });

            RegisteredName = device.Name;
            return Page();
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
            return Page();
        }
        catch (Exception)
        {
            Error = "เชื่อมต่อฐานข้อมูลไม่ได้ กรุณาแจ้งผู้ดูแลระบบ";
            return Page();
        }
    }
}
