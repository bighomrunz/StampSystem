using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages.Admin;

public class SettingsModel : PageModel
{
    private readonly SettingsService _settings;

    public SettingsModel(SettingsService settings) => _settings = settings;

    /// <summary>ชนิดแยกจาก entity เพื่อไม่ให้ model binding แตะค่าที่ไม่ควรแก้ เช่น UpdatedByStaffId</summary>
    public class SettingsForm
    {
        public string ShopName { get; set; } = "";
        public string? PromptPayId { get; set; }
        public PromptPayIdType PromptPayIdType { get; set; } = PromptPayIdType.Mobile;
        public string? PromptPayMerchantName { get; set; }
        public int StampsPerReward { get; set; } = 10;
        public int StampExpiryMonths { get; set; } = 12;
        public RewardMode RewardMode { get; set; } = RewardMode.AllItems;
        public decimal? RewardPriceCap { get; set; }
        public bool AllowRewardTopUp { get; set; }
        public int MaxStampsPerOrder { get; set; } = 6;
        public int MaxStampsPerDayPerCustomer { get; set; } = 6;
        public TimeOnly OpenTime { get; set; } = new(7, 0);
        public TimeOnly CloseTime { get; set; } = new(18, 0);
        public decimal VarianceAlertThreshold { get; set; } = 20m;
        public int StaffIdleTimeoutMinutes { get; set; } = 15;
    }

    [BindProperty]
    public SettingsForm Input { get; set; } = new();

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Input = ToForm(await _settings.GetAsync(ct));
        }
        catch (Exception)
        {
            Error = "โหลดการตั้งค่าไม่สำเร็จ ตรวจการเชื่อมต่อฐานข้อมูล";
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            await _settings.UpdateAsync(new SettingsInput(
                Input.ShopName,
                Input.PromptPayId,
                Input.PromptPayIdType,
                Input.PromptPayMerchantName,
                Input.StampsPerReward,
                Input.StampExpiryMonths,
                Input.RewardMode,
                Input.RewardPriceCap,
                Input.AllowRewardTopUp,
                Input.MaxStampsPerOrder,
                Input.MaxStampsPerDayPerCustomer,
                Input.OpenTime,
                Input.CloseTime,
                Input.VarianceAlertThreshold,
                Input.StaffIdleTimeoutMinutes), ct);

            Message = "บันทึกการตั้งค่าแล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    private static SettingsForm ToForm(ShopSettings s) => new()
    {
        ShopName = s.ShopName,
        PromptPayId = s.PromptPayId,
        PromptPayIdType = s.PromptPayIdType,
        PromptPayMerchantName = s.PromptPayMerchantName,
        StampsPerReward = s.StampsPerReward,
        StampExpiryMonths = s.StampExpiryMonths,
        RewardMode = s.RewardMode,
        RewardPriceCap = s.RewardPriceCap,
        AllowRewardTopUp = s.AllowRewardTopUp,
        MaxStampsPerOrder = s.MaxStampsPerOrder,
        MaxStampsPerDayPerCustomer = s.MaxStampsPerDayPerCustomer,
        OpenTime = s.OpenTime,
        CloseTime = s.CloseTime,
        VarianceAlertThreshold = s.VarianceAlertThreshold,
        StaffIdleTimeoutMinutes = s.StaffIdleTimeoutMinutes
    };
}
