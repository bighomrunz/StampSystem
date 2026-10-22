using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeeStamp.Web.Services;

public record OrderLineRequest(int MenuItemId, int Quantity);

public record CreateOrderRequest(
    List<OrderLineRequest> Items,
    PaymentMethod PaymentMethod,
    string? Note);

/// <summary>
/// ขายเร็ว: ไม่ต้องเลือกเมนู ใส่แค่จำนวนแก้วที่นับแต้ม
/// ยอดเงินไม่บังคับ (ช่วงที่เปิดใช้แค่สแตมป์ ร้านยังไม่ได้ใช้ระบบคิดเงิน)
/// </summary>
public record QuickSaleRequest(int Cups, PaymentMethod PaymentMethod, decimal? Amount, string? Note);

public class OrderService
{
    public const int MaxQuantityPerLine = 99;

    /// <summary>SQLSTATE ของ PostgreSQL เมื่อชน unique constraint</summary>
    private const string UniqueViolation = "23505";

    private const int OrderNoRetryLimit = 5;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;
    private readonly StaffAuthService _auth;

    private readonly StampService _stamps;

    public OrderService(AppDbContext db, AuditService audit, StaffContext staff,
        StaffAuthService auth, StampService stamps)
    {
        _db = db;
        _audit = audit;
        _staff = staff;
        _auth = auth;
        _stamps = stamps;
    }

    public Task<Order?> GetAsync(long id, CancellationToken ct = default) =>
        _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Staff)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<List<Order>> GetForDateAsync(DateOnly businessDate, CancellationToken ct = default) =>
        _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Staff)
            .Include(o => o.Customer)
            .Where(o => o.BusinessDate == businessDate)
            .OrderByDescending(o => o.Id)
            .ToListAsync(ct);

    /// <summary>
    /// สร้างออเดอร์ เงินสดถือว่ารับเงินแล้วทันที
    /// พร้อมเพย์และเป๋าตังจะเป็น Pending รอพนักงานกดยืนยันว่าเงินเข้าแล้ว
    /// </summary>
    public async Task<Order> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var deviceId = _staff.RequireDeviceId();

        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("ยังไม่ได้เลือกเมนู");

        var businessDate = BangkokTime.Today();
        await EnsureDayOpenAsync(businessDate, ct);

        // รวมบรรทัดที่เป็นเมนูเดียวกันก่อน กันกรณีหน้าจอส่งซ้ำ
        var lines = request.Items
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.MenuItemId)
            .Select(g => new OrderLineRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (lines.Count == 0)
            throw new DomainException("ยังไม่ได้เลือกเมนู");

        if (lines.Any(l => l.Quantity > MaxQuantityPerLine))
            throw new DomainException($"จำนวนต่อเมนูต้องไม่เกิน {MaxQuantityPerLine} แก้ว");

        var menuIds = lines.Select(l => l.MenuItemId).ToList();
        var menu = await _db.MenuItems.AsNoTracking()
            .Where(m => menuIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        foreach (var line in lines)
        {
            if (!menu.TryGetValue(line.MenuItemId, out var item))
                throw new DomainException("มีเมนูที่ไม่พบในระบบ กรุณารีเฟรชหน้าจอ");
            if (!item.IsActive)
                throw new DomainException($"เมนู {item.Name} ถูกปิดการขายแล้ว");
        }

        for (var attempt = 1; ; attempt++)
        {
            var order = new Order
            {
                OrderNo = await NextOrderNoAsync(businessDate, ct),
                BusinessDate = businessDate,
                StaffId = staffId,
                DeviceId = deviceId,
                PaymentMethod = request.PaymentMethod,
                Note = Trim(request.Note, 300),
                CreatedAt = DateTime.UtcNow,
                Status = request.PaymentMethod == PaymentMethod.Cash
                    ? OrderStatus.Paid
                    : OrderStatus.Pending
            };

            if (order.Status == OrderStatus.Paid)
                order.PaidAt = order.CreatedAt;

            foreach (var line in lines)
            {
                var item = menu[line.MenuItemId];
                order.Items.Add(new OrderItem
                {
                    MenuItemId = item.Id,
                    NameSnapshot = item.Name,
                    UnitPrice = item.Price,
                    Quantity = line.Quantity,
                    LineTotal = Money.Round(item.Price * line.Quantity),
                    IsReward = false,
                    EarnsStampSnapshot = item.EarnsStamp
                });
            }

            order.SubTotal = Money.Round(order.Items.Sum(i => i.LineTotal));
            order.Total = order.SubTotal;

            _db.Orders.Add(order);
            _audit.Add("order.create", nameof(Order), order.OrderNo, new
            {
                order.OrderNo,
                order.Total,
                PaymentMethod = order.PaymentMethod.ToString(),
                Status = order.Status.ToString(),
                Items = order.Items.Select(i => new { i.NameSnapshot, i.Quantity, i.LineTotal })
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                return order;
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex) && attempt < OrderNoRetryLimit)
            {
                // เครื่องอื่นออกเลขเดียวกันพอดี ล้าง change tracker แล้วขอเลขใหม่
                DetachAdded();
            }
        }
    }

    /// <summary>
    /// ขายเร็วแบบไม่เลือกเมนู ใช้เป็นทางหลักช่วงที่เปิดใช้แค่การสแตมป์
    /// ยังคงหลักข้อ 2 ของ README ไว้: แต้มยังผูกกับออเดอร์จริงที่มีพนักงาน อุปกรณ์ และเวลา
    /// ต่างจากหน้าขายเต็มคือไม่มีการเลือกเมนูและถือว่ารับเงินแล้วทันที (พนักงานกดหลังรับเงิน)
    /// </summary>
    public async Task<Order> CreateQuickSaleAsync(QuickSaleRequest request,
        CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var deviceId = _staff.RequireDeviceId();

        if (request.Cups < 1)
            throw new DomainException("ต้องระบุจำนวนแก้วอย่างน้อย 1 แก้ว");

        if (request.Cups > MaxQuantityPerLine)
            throw new DomainException($"ขายเร็วได้ไม่เกิน {MaxQuantityPerLine} แก้วต่อครั้ง");

        if (request.Amount is { } amount && (amount < 0 || amount > 99_999_99m))
            throw new DomainException("ยอดเงินไม่ถูกต้อง");

        var businessDate = BangkokTime.Today();
        await EnsureDayOpenAsync(businessDate, ct);

        var total = Money.Round(request.Amount ?? 0m);
        var now = DateTime.UtcNow;

        for (var attempt = 1; ; attempt++)
        {
            var order = new Order
            {
                OrderNo = await NextOrderNoAsync(businessDate, ct),
                BusinessDate = businessDate,
                StaffId = staffId,
                DeviceId = deviceId,
                PaymentMethod = request.PaymentMethod,
                Status = OrderStatus.Paid,
                PaidAt = now,
                CreatedAt = now,
                Note = Trim(request.Note, 300),
                SubTotal = total,
                Total = total
            };

            order.Items.Add(new OrderItem
            {
                MenuItemId = null,
                NameSnapshot = $"ขายเร็ว {request.Cups} แก้ว",
                // ราคาต่อแก้วเป็นค่าประกอบเพื่อให้อ่านรายงานได้ ยอดที่ใช้จริงคือ LineTotal
                UnitPrice = request.Cups > 0 ? Money.Round(total / request.Cups) : 0m,
                Quantity = request.Cups,
                LineTotal = total,
                IsReward = false,
                EarnsStampSnapshot = true
            });

            _db.Orders.Add(order);
            _audit.Add("order.quick_sale", nameof(Order), order.OrderNo, new
            {
                order.OrderNo,
                Cups = request.Cups,
                order.Total,
                PaymentMethod = order.PaymentMethod.ToString()
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                return order;
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex) && attempt < OrderNoRetryLimit)
            {
                DetachAdded();
            }
        }
    }

    /// <summary>
    /// ออเดอร์สำหรับแก้วที่แลกด้วยแต้ม มีบรรทัดเดียวราคา 0 หรือส่วนต่างถ้าลูกค้าจ่ายเพิ่ม
    /// แก้วรางวัลไม่ได้แต้ม (EarnsStampSnapshot = false) ตามกฎใน README
    /// เรียกจาก RewardService ภายใน transaction เดียวกับการตัดแต้ม
    /// </summary>
    public async Task<Order> CreateRewardOrderAsync(int? customerId, MenuItem menuItem,
        decimal topUpAmount, PaymentMethod paymentMethod, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var deviceId = _staff.RequireDeviceId();

        var businessDate = BangkokTime.Today();
        await EnsureDayOpenAsync(businessDate, ct);

        var topUp = Money.Round(topUpAmount);
        if (topUp < 0) throw new DomainException("ส่วนต่างที่จ่ายเพิ่มต้องไม่เป็นค่าลบ");

        var now = DateTime.UtcNow;

        for (var attempt = 1; ; attempt++)
        {
            var order = new Order
            {
                OrderNo = await NextOrderNoAsync(businessDate, ct),
                BusinessDate = businessDate,
                StaffId = staffId,
                DeviceId = deviceId,
                CustomerId = customerId,
                // ไม่มีส่วนต่างก็ถือเป็นเงินสดเพื่อให้ช่องทางไม่ว่าง ยอดเป็น 0 อยู่แล้ว
                PaymentMethod = topUp > 0 ? paymentMethod : PaymentMethod.Cash,
                Status = OrderStatus.Paid,
                PaidAt = now,
                CreatedAt = now,
                SubTotal = topUp,
                Total = topUp,
                Note = $"แลกรางวัล: {menuItem.Name}"
            };

            order.Items.Add(new OrderItem
            {
                MenuItemId = menuItem.Id,
                NameSnapshot = menuItem.Name,
                UnitPrice = topUp,
                Quantity = 1,
                LineTotal = topUp,
                IsReward = true,
                EarnsStampSnapshot = false
            });

            _db.Orders.Add(order);
            _audit.Add("order.reward", nameof(Order), order.OrderNo, new
            {
                order.OrderNo,
                Menu = menuItem.Name,
                TopUp = topUp,
                CustomerId = customerId
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                return order;
            }
            catch (DbUpdateException ex) when (IsDuplicateKey(ex) && attempt < OrderNoRetryLimit)
            {
                DetachAdded();
            }
        }
    }

    /// <summary>พนักงานกดยืนยันว่าได้รับเงินแล้ว ใช้กับพร้อมเพย์และเป๋าตัง</summary>
    public async Task<Order> ConfirmPaymentAsync(long orderId, CancellationToken ct = default)
    {
        var order = await RequireOrderAsync(orderId, ct);
        await EnsureDayOpenAsync(order.BusinessDate, ct);

        if (order.Status == OrderStatus.Paid)
            return order;
        if (order.Status != OrderStatus.Pending)
            throw new DomainException("ออเดอร์นี้ยืนยันการรับเงินไม่ได้");

        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTime.UtcNow;
        _audit.Add("order.confirm_payment", nameof(Order), order.OrderNo, new
        {
            order.OrderNo,
            order.Total,
            PaymentMethod = order.PaymentMethod.ToString()
        });
        await _db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>ยกเลิกออเดอร์ที่ยังไม่รับเงิน ไม่ต้องใช้ PIN ผู้จัดการเพราะไม่มีเงินเข้า</summary>
    public async Task<Order> CancelPendingAsync(long orderId, string? reason, CancellationToken ct = default)
    {
        var order = await RequireOrderAsync(orderId, ct);
        await EnsureDayOpenAsync(order.BusinessDate, ct);

        if (order.Status != OrderStatus.Pending)
            throw new DomainException("ยกเลิกได้เฉพาะออเดอร์ที่ยังไม่ยืนยันการรับเงิน");

        order.Status = OrderStatus.Cancelled;
        order.VoidedAt = DateTime.UtcNow;
        order.VoidedByStaffId = _staff.StaffId;
        order.VoidReason = Trim(reason, 300);
        _audit.Add("order.cancel", nameof(Order), order.OrderNo, new { order.OrderNo, order.VoidReason });
        await _db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>
    /// ยกเลิกออเดอร์ที่รับเงินแล้ว ต้องมี PIN ผู้จัดการและเหตุผลทุกครั้ง
    /// ถ้าออเดอร์นี้ออกแต้มไปแล้ว จะเขียนรายการ Void ย้อนแต้มใน transaction เดียวกัน
    /// และถ้าแต้มถูกใช้ไปแล้วจะ block (ดู StampService.AddVoidForOrderAsync)
    /// </summary>
    public async Task<Order> VoidAsync(long orderId, string reason, string managerPin,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ต้องระบุเหตุผลในการยกเลิก");

        var order = await RequireOrderAsync(orderId, ct);
        await EnsureDayOpenAsync(order.BusinessDate, ct);

        if (order.Status != OrderStatus.Paid)
            throw new DomainException("ยกเลิกได้เฉพาะออเดอร์ที่รับเงินแล้ว");

        var approver = await _auth.VerifyManagerPinAsync(managerPin, ct);

        order.Status = OrderStatus.Voided;
        order.VoidedAt = DateTime.UtcNow;
        order.VoidedByStaffId = _staff.StaffId;
        order.VoidApprovedByStaffId = approver.Id;
        order.VoidReason = Trim(reason, 300);

        // ย้อนแต้มของออเดอร์นี้ด้วย ถ้ามี ให้บันทึกพร้อมกันใน SaveChanges เดียว
        var reversedStamps = await _stamps.AddVoidForOrderAsync(order, ct);

        _audit.Add("order.void", nameof(Order), order.OrderNo, new
        {
            order.OrderNo,
            order.Total,
            Reason = order.VoidReason,
            ApprovedBy = approver.Name,
            ApprovedByStaffId = approver.Id,
            ReversedStamps = reversedStamps
        });

        await _db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<Order> RequireOrderAsync(long orderId, CancellationToken ct = default) =>
        await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new DomainException("ไม่พบออเดอร์");

    /// <summary>ปิดยอดวันไหนแล้ว ออเดอร์ของวันนั้นห้ามแก้ไขอีก</summary>
    private async Task EnsureDayOpenAsync(DateOnly businessDate, CancellationToken ct)
    {
        if (await _db.DailyCloses.AnyAsync(d => d.BusinessDate == businessDate, ct))
            throw new DomainException($"วันทำการ {businessDate:dd/MM/yyyy} ปิดยอดแล้ว แก้ไขออเดอร์ไม่ได้");
    }

    private async Task<string> NextOrderNoAsync(DateOnly businessDate, CancellationToken ct)
    {
        var prefix = businessDate.ToString("yyyyMMdd");
        var countToday = await _db.Orders.CountAsync(o => o.BusinessDate == businessDate, ct);
        return $"{prefix}-{countToday + 1:D4}";
    }

    private void DetachAdded()
    {
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
