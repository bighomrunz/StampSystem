using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeeStamp.Web.Services;

/// <summary>เมนูหนึ่งรายการพร้อมคำตอบว่าแลกได้ไหม และต้องจ่ายเพิ่มเท่าไร</summary>
public record RewardOption(
    int MenuItemId,
    string Name,
    string? Category,
    decimal Price,
    bool CanRedeem,
    decimal TopUpAmount,
    string? Reason);

public record RedeemResult(
    long OrderId,
    string OrderNo,
    int StampsUsed,
    int Balance,
    int StampsPerReward,
    int RewardsAvailable,
    string MenuName,
    decimal TopUpAmount,
    int CustomerId,
    string? CustomerName,
    string? LineUserId);

/// <summary>
/// การแลกรางวัล — ประสานระหว่างออเดอร์ (OrderService) กับ ledger (StampService)
/// แยกออกมาเพราะถ้าเอาไปไว้ใน StampService จะเกิด dependency วนกับ OrderService
/// ตัวเลขแต้มและการตัดแบบ FIFO ยังคงอยู่ใน StampService ที่เดียวตาม convention ของ README
/// </summary>
public class RewardService
{
    private const string UniqueViolation = "23505";
    private const int ConcurrencyRetryLimit = 3;

    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    private readonly StampService _stamps;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;
    private readonly StaffAuthService _auth;
    private readonly ILogger<RewardService> _logger;

    public RewardService(AppDbContext db, OrderService orders, StampService stamps,
        SettingsService settings, AuditService audit, StaffContext staff, StaffAuthService auth,
        ILogger<RewardService> logger)
    {
        _db = db;
        _orders = orders;
        _stamps = stamps;
        _settings = settings;
        _audit = audit;
        _staff = staff;
        _auth = auth;
        _logger = logger;
    }

    /// <summary>
    /// รายการเมนูที่แลกได้ตามโหมดใน ShopSettings
    /// - AllItems: แลกได้ทุกเมนูที่ยังขายอยู่
    /// - SelectedItems: เฉพาะเมนูที่ติ๊ก "ใช้แลกรางวัลได้"
    /// - PriceCap: ราคาไม่เกินเพดานที่เจ้าของตั้งไว้ ถ้าเกินและเปิด AllowRewardTopUp
    ///   ลูกค้าจ่ายส่วนต่างได้
    /// </summary>
    public async Task<List<RewardOption>> GetOptionsAsync(CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);

        var items = await _db.MenuItems.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(ct);

        return items.Select(item => Evaluate(item, settings)).ToList();
    }

    /// <summary>ตรวจเมนูหนึ่งรายการตามโหมดรางวัล ใช้ทั้งตอนแสดงรายการและตอนแลกจริง</summary>
    public static RewardOption Evaluate(MenuItem item, ShopSettings settings)
    {
        switch (settings.RewardMode)
        {
            case RewardMode.AllItems:
                return new RewardOption(item.Id, item.Name, item.Category, item.Price,
                    true, 0m, null);

            case RewardMode.SelectedItems:
                return item.IsRewardEligible
                    ? new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        true, 0m, null)
                    : new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        false, 0m, "เมนูนี้ไม่ได้ตั้งให้ใช้แลกรางวัล");

            case RewardMode.PriceCap:
                var cap = settings.RewardPriceCap ?? 0m;

                if (cap <= 0m)
                {
                    return new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        false, 0m, "ยังไม่ได้ตั้งเพดานราคารางวัลในหน้าตั้งค่า");
                }

                if (item.Price <= cap)
                {
                    return new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        true, 0m, null);
                }

                // เกินเพดาน: ให้จ่ายส่วนต่างได้ถ้าเจ้าของร้านเปิดไว้
                var topUp = Money.Round(item.Price - cap);

                return settings.AllowRewardTopUp
                    ? new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        true, topUp, $"เกินเพดาน {Money.Format(cap)} บาท ลูกค้าจ่ายเพิ่ม " +
                                     $"{Money.Format(topUp)} บาท")
                    : new RewardOption(item.Id, item.Name, item.Category, item.Price,
                        false, topUp, $"ราคาเกินเพดาน {Money.Format(cap)} บาท");

            default:
                return new RewardOption(item.Id, item.Name, item.Category, item.Price,
                    false, 0m, "โหมดรางวัลไม่ถูกต้อง");
        }
    }

    /// <summary>
    /// แลกรางวัลด้วยแต้ม: สร้างออเดอร์ที่มีบรรทัดรางวัล ตัดแต้มแบบ FIFO และบันทึก Redemption
    /// ทั้งหมดอยู่ใน transaction เดียว ถ้าพังระหว่างทางจะไม่มีทั้งออเดอร์และการตัดแต้ม
    /// </summary>
    public async Task<RedeemResult> RedeemAsync(int customerId, int menuItemId,
        PaymentMethod topUpPaymentMethod, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        _staff.RequireDeviceId();

        var settings = await _settings.GetAsync(ct);
        var perReward = settings.StampsPerReward;

        if (perReward < 1)
            throw new DomainException("ยังไม่ได้ตั้งจำนวนแต้มต่อรางวัลในหน้าตั้งค่า");

        var menuItem = await _db.MenuItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == menuItemId, ct)
            ?? throw new DomainException("ไม่พบเมนูนี้");

        if (!menuItem.IsActive)
            throw new DomainException($"เมนู {menuItem.Name} ถูกปิดการขายแล้ว");

        var option = Evaluate(menuItem, settings);
        if (!option.CanRedeem)
            throw new DomainException(option.Reason ?? "เมนูนี้แลกไม่ได้");

        var strategy = _db.Database.CreateExecutionStrategy();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await strategy.ExecuteAsync(() =>
                    RedeemOnceAsync(customerId, menuItem, option, settings, staffId,
                        topUpPaymentMethod, ct));
            }
            catch (DbUpdateConcurrencyException) when (attempt < ConcurrencyRetryLimit)
            {
                // ยอดแต้มถูกแก้พร้อมกัน (เช่นอีกเครื่องกำลังสแตมป์อยู่) อ่านใหม่แล้วลองอีกครั้ง
                _logger.LogInformation(
                    "ยอดแต้มของลูกค้า {CustomerId} ถูกแก้พร้อมกันระหว่างแลก ลองใหม่ครั้งที่ {Attempt}",
                    customerId, attempt + 1);
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (IsDuplicate(ex) && attempt < ConcurrencyRetryLimit)
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<RedeemResult> RedeemOnceAsync(int customerId, MenuItem menuItem,
        RewardOption option, ShopSettings settings, int staffId,
        PaymentMethod topUpPaymentMethod, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var customer = await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
            ?? throw new DomainException("ไม่พบสมาชิกคนนี้");

        var balance = customer.Balance ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้า");
        var perReward = settings.StampsPerReward;

        // ยอดที่ใช้ได้จริงต้องไม่นับแต้มที่หมดอายุแล้วแต่ job ยังไม่ได้ตัด
        var usable = await _stamps.GetUsableStampsAsync(customerId, ct);

        if (usable < perReward)
        {
            throw new DomainException(
                $"แต้มไม่พอ ต้องมี {perReward} แต้ม ตอนนี้ใช้ได้ {usable} แต้ม");
        }

        var order = await _orders.CreateRewardOrderAsync(
            customer.Id, menuItem, option.TopUpAmount, topUpPaymentMethod, ct);

        var redemption = new Redemption
        {
            CustomerId = customer.Id,
            Order = order,
            Source = RedemptionSource.Stamps,
            StampsUsed = perReward,
            MenuItemId = menuItem.Id,
            MenuNameSnapshot = menuItem.Name,
            MenuPriceSnapshot = menuItem.Price,
            TopUpAmount = option.TopUpAmount,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            CreatedAt = DateTime.UtcNow
        };

        var redeemTxn = new StampTransaction
        {
            CustomerId = customer.Id,
            Type = StampTransactionType.Redeem,
            Quantity = -perReward,
            OrderId = order.Id,
            Redemption = redemption,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            Note = menuItem.Name,
            CreatedAt = DateTime.UtcNow
        };

        _db.Redemptions.Add(redemption);
        _db.StampTransactions.Add(redeemTxn);

        // ตัดจากก้อนที่ใกล้หมดอายุก่อน
        await _stamps.AllocateFifoAsync(customer.Id, perReward, redeemTxn, ct);

        balance.Stamps -= perReward;
        balance.UpdatedAt = DateTime.UtcNow;

        _audit.Add("stamp.redeem", nameof(Redemption), order.OrderNo, new
        {
            order.OrderNo,
            CustomerId = customer.Id,
            customer.DisplayName,
            Menu = menuItem.Name,
            StampsUsed = perReward,
            option.TopUpAmount,
            NewBalance = balance.Stamps,
            Mode = settings.RewardMode.ToString()
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new RedeemResult(
            order.Id,
            order.OrderNo,
            perReward,
            balance.Stamps,
            perReward,
            perReward > 0 ? balance.Stamps / perReward : 0,
            menuItem.Name,
            option.TopUpAmount,
            customer.Id,
            customer.DisplayName,
            customer.LineUserId);
    }

    /// <summary>
    /// แลกด้วยบัตรกระดาษเดิมช่วง dual-run — ไม่ตัดแต้มเพราะไม่มีแต้มในระบบ
    /// ต้องมีเหตุผลและ PIN ผู้จัดการ และนับแยกในรายงานเพื่อไม่ให้ยอดขายเพี้ยน
    /// </summary>
    public async Task<RedeemResult> RedeemWithPaperCardAsync(int menuItemId, string reason,
        string managerPin, PaymentMethod topUpPaymentMethod, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        _staff.RequireDeviceId();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ต้องระบุเหตุผล เช่น เลขบัตรกระดาษที่รับมา");

        var settings = await _settings.GetAsync(ct);
        var menuItem = await _db.MenuItems.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == menuItemId, ct)
            ?? throw new DomainException("ไม่พบเมนูนี้");

        var option = Evaluate(menuItem, settings);
        if (!option.CanRedeem)
            throw new DomainException(option.Reason ?? "เมนูนี้แลกไม่ได้");

        var approver = await _auth.VerifyManagerPinAsync(managerPin, ct);

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var order = await _orders.CreateRewardOrderAsync(
                null, menuItem, option.TopUpAmount, topUpPaymentMethod, ct);

            _db.Redemptions.Add(new Redemption
            {
                CustomerId = null,
                Order = order,
                Source = RedemptionSource.PaperCard,
                StampsUsed = 0,
                MenuItemId = menuItem.Id,
                MenuNameSnapshot = menuItem.Name,
                MenuPriceSnapshot = menuItem.Price,
                TopUpAmount = option.TopUpAmount,
                StaffId = staffId,
                ApprovedByStaffId = approver.Id,
                DeviceId = _staff.DeviceId,
                Note = reason.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            _audit.Add("stamp.redeem_paper_card", nameof(Redemption), order.OrderNo, new
            {
                order.OrderNo,
                Menu = menuItem.Name,
                Reason = reason.Trim(),
                ApprovedBy = approver.Name,
                ApprovedByStaffId = approver.Id
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new RedeemResult(order.Id, order.OrderNo, 0, 0, settings.StampsPerReward, 0,
                menuItem.Name, option.TopUpAmount, 0, "บัตรกระดาษ (ไม่ตัดแต้ม)", null);
        });
    }

    private static bool IsDuplicate(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
