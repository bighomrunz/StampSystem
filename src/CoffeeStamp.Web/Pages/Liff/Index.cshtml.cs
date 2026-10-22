using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Web.Pages.Liff;

public class IndexModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly CustomerAuthService _auth;
    private readonly LineOptions _line;
    private readonly IAntiforgery _antiforgery;
    private readonly IHostEnvironment _environment;

    public IndexModel(SettingsService settings, CustomerAuthService auth,
        IOptions<LineOptions> line, IAntiforgery antiforgery, IHostEnvironment environment)
    {
        _settings = settings;
        _auth = auth;
        _line = line.Value;
        _antiforgery = antiforgery;
        _environment = environment;
    }

    public string ShopName { get; private set; } = ShopSettings.DefaultShopName;
    public string? LiffId => _line.LiffId;
    public string ConsentVersion => _auth.ConsentVersion;
    public bool AllowDevMockLogin => _environment.IsDevelopment() && _line.AllowDevMockLogin;

    /// <summary>โหลด SDK ของ LINE เฉพาะเมื่อมี LIFF ID จริง เวลาเทสในเครื่องจะไม่ต้องรอเน็ต</summary>
    public bool LiffSdkEnabled => !string.IsNullOrWhiteSpace(_line.LiffId);

    public string CsrfToken { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        CsrfToken = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken ?? "";

        try
        {
            ShopName = (await _settings.GetAsync(ct)).ShopName;
        }
        catch (Exception)
        {
            // หน้าบัตรต้องเปิดได้เสมอ ชื่อร้านเป็นเรื่องรอง
        }
    }
}
