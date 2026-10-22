using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public record SettingsInput(
    string ShopName,
    string? PromptPayId,
    PromptPayIdType PromptPayIdType,
    string? PromptPayMerchantName,
    int StampsPerReward,
    int StampExpiryMonths,
    RewardMode RewardMode,
    decimal? RewardPriceCap,
    bool AllowRewardTopUp,
    int MaxStampsPerOrder,
    int MaxStampsPerDayPerCustomer,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    decimal VarianceAlertThreshold,
    int StaffIdleTimeoutMinutes);

public class SettingsService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;

    public SettingsService(AppDbContext db, AuditService audit, StaffContext staff)
    {
        _db = db;
        _audit = audit;
        _staff = staff;
    }

    public Task<ShopSettings> GetAsync(CancellationToken ct = default) =>
        _db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == ShopSettings.SingletonId, ct);

    /// <summary>
    /// ชื่อร้านสำหรับเอาไปแสดงบนหน้าจอ ใช้ที่เดียวกันทุกหน้า
    /// ถ้าฐานข้อมูลยังไม่พร้อมก็ยังคืนค่าปริยายได้ หน้าเว็บจะไม่พังเพราะแค่ชื่อร้าน
    /// </summary>
    public async Task<string> GetShopNameAsync(CancellationToken ct = default)
    {
        try
        {
            var name = (await GetAsync(ct)).ShopName;
            return string.IsNullOrWhiteSpace(name) ? ShopSettings.DefaultShopName : name;
        }
        catch (Exception)
        {
            return ShopSettings.DefaultShopName;
        }
    }

    public async Task<ShopSettings> UpdateAsync(SettingsInput input, CancellationToken ct = default)
    {
        Validate(input);

        var settings = await _db.ShopSettings
            .FirstAsync(s => s.Id == ShopSettings.SingletonId, ct);

        var before = Snapshot(settings);

        settings.ShopName = input.ShopName.Trim();
        settings.PromptPayId = string.IsNullOrWhiteSpace(input.PromptPayId)
            ? null
            : input.PromptPayId.Trim();
        settings.PromptPayIdType = input.PromptPayIdType;
        settings.PromptPayMerchantName = string.IsNullOrWhiteSpace(input.PromptPayMerchantName)
            ? null
            : input.PromptPayMerchantName.Trim();
        settings.StampsPerReward = input.StampsPerReward;
        settings.StampExpiryMonths = input.StampExpiryMonths;
        settings.RewardMode = input.RewardMode;
        settings.RewardPriceCap = input.RewardPriceCap is null
            ? null
            : Money.Round(input.RewardPriceCap.Value);
        settings.AllowRewardTopUp = input.AllowRewardTopUp;
        settings.MaxStampsPerOrder = input.MaxStampsPerOrder;
        settings.MaxStampsPerDayPerCustomer = input.MaxStampsPerDayPerCustomer;
        settings.OpenTime = input.OpenTime;
        settings.CloseTime = input.CloseTime;
        settings.VarianceAlertThreshold = Money.Round(input.VarianceAlertThreshold);
        settings.StaffIdleTimeoutMinutes = input.StaffIdleTimeoutMinutes;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByStaffId = _staff.StaffId;

        _audit.Add("settings.update", nameof(ShopSettings), ShopSettings.SingletonId.ToString(),
            new { Before = before, After = Snapshot(settings) });

        await _db.SaveChangesAsync(ct);
        return settings;
    }

    private static object Snapshot(ShopSettings s) => new
    {
        s.ShopName,
        s.PromptPayId,
        PromptPayIdType = s.PromptPayIdType.ToString(),
        s.PromptPayMerchantName,
        s.StampsPerReward,
        s.StampExpiryMonths,
        RewardMode = s.RewardMode.ToString(),
        s.RewardPriceCap,
        s.AllowRewardTopUp,
        s.MaxStampsPerOrder,
        s.MaxStampsPerDayPerCustomer,
        OpenTime = s.OpenTime.ToString("HH:mm"),
        CloseTime = s.CloseTime.ToString("HH:mm"),
        s.VarianceAlertThreshold,
        s.StaffIdleTimeoutMinutes
    };

    private static void Validate(SettingsInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ShopName))
            throw new DomainException("กรุณากรอกชื่อร้าน");

        if (input.StampsPerReward < 1 || input.StampsPerReward > 100)
            throw new DomainException("จำนวนแต้มต่อรางวัลต้องอยู่ระหว่าง 1 ถึง 100");

        if (input.StampExpiryMonths < 0 || input.StampExpiryMonths > 120)
            throw new DomainException("อายุแต้มต้องอยู่ระหว่าง 0 ถึง 120 เดือน (0 = ไม่หมดอายุ)");

        if (input.MaxStampsPerOrder < 1 || input.MaxStampsPerDayPerCustomer < 1)
            throw new DomainException("เพดานแต้มต้องมากกว่า 0");

        if (input.VarianceAlertThreshold < 0)
            throw new DomainException("เกณฑ์ส่วนต่างต้องไม่เป็นค่าลบ");

        if (input.StaffIdleTimeoutMinutes < 1 || input.StaffIdleTimeoutMinutes > 240)
            throw new DomainException("เวลาล็อกเอาต์อัตโนมัติต้องอยู่ระหว่าง 1 ถึง 240 นาที");

        if (input.RewardMode == RewardMode.PriceCap &&
            (input.RewardPriceCap is null || input.RewardPriceCap <= 0))
            throw new DomainException("โหมด PriceCap ต้องตั้งเพดานราคารางวัลมากกว่า 0");

        // ตรวจว่าเลขพร้อมเพย์ที่กรอกสร้าง QR ได้จริง ดีกว่าให้พังตอนหน้าร้าน
        if (!string.IsNullOrWhiteSpace(input.PromptPayId))
        {
            try
            {
                PromptPayPayload.NormalizeTarget(input.PromptPayId, input.PromptPayIdType);
            }
            catch (ArgumentException ex)
            {
                throw new DomainException(ex.Message);
            }
        }
    }
}
