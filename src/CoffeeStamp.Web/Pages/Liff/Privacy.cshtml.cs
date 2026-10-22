using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages.Liff;

public class PrivacyModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly CustomerAuthService _auth;

    public PrivacyModel(SettingsService settings, CustomerAuthService auth)
    {
        _settings = settings;
        _auth = auth;
    }

    public string ShopName { get; private set; } = ShopSettings.DefaultShopName;
    public string ConsentVersion => _auth.ConsentVersion;

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            ShopName = (await _settings.GetAsync(ct)).ShopName;
        }
        catch (Exception)
        {
            // หน้านี้ต้องอ่านได้แม้ DB มีปัญหา เพราะเป็นเอกสารตาม PDPA
        }
    }
}
