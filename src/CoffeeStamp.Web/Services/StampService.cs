using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeeStamp.Web.Services;

public record ExpiringStamps(DateOnly ExpiresOn, int Stamps);

public record CustomerStampSummary(
    int CustomerId,
    Guid PublicId,
    string? DisplayName,
    int Balance,
    int StampsPerReward,
    int RewardsAvailable,
    int StampsToNextReward,
    List<ExpiringStamps> Expiring);

public record StampHistoryItem(
    DateTime CreatedAt,
    StampTransactionType Type,
    int Quantity,
    string? OrderNo,
    DateTime? ExpiresAt,
    string? Note);

public record StampEarnResult(
    int Earned,
    int Balance,
    int StampsPerReward,
    long OrderId,
    string OrderNo,
    int CustomerId,
    string? CustomerName,
    /// <summary>null สำหรับสมาชิกที่สมัครหน้าร้านและยังไม่ได้ผูก LINE จึง push แจ้งเตือนไม่ได้</summary>
    string? LineUserId,
    DateTime? ExpiresAt);

/// <summary>
/// ทุก logic ของแต้มอยู่ที่นี่ที่เดียวตาม convention ใน README
/// หลักที่ห้ามละเมิด: ร้านเป็นฝ่ายออกแต้ม, ทุกแต้มผูกออเดอร์จริง, ledger เป็น append-only
/// </summary>
public class StampService
{
    private const string UniqueViolation = "23505";
    private const int ConcurrencyRetryLimit = 3;
    private const int ExpiringSoonDays = 45;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;
    private readonly SettingsService _settings;
    private readonly CustomerAuthService _customers;
    private readonly QrSecretProvider _qrSecret;
    private readonly StaffAuthService _auth;
    private readonly ILogger<StampService> _logger;

    public StampService(AppDbContext db, AuditService audit, StaffContext staff,
        SettingsService settings, CustomerAuthService customers, QrSecretProvider qrSecret,
        StaffAuthService auth, ILogger<StampService> logger)
    {
        _db = db;
        _audit = audit;
        _staff = staff;
        _settings = settings;
        _customers = customers;
        _qrSecret = qrSecret;
        _auth = auth;
        _logger = logger;
    }

    /// <summary>ออก QR ให้ลูกค้า เรียกได้จาก endpoint ที่ยืนยันตัวตนลูกค้าแล้วเท่านั้น</summary>
    public string IssueQrToken(Guid publicId) =>
        CustomerQrToken.Issue(_qrSecret.Secret, publicId, DateTime.UtcNow);

    public static int SecondsUntilQrRefresh() =>
        CustomerQrToken.SecondsUntilNextStep(DateTime.UtcNow);

    /// <summary>
    /// อ่าน QR เพื่อดูว่าเป็นลูกค้าคนไหน โดย "ไม่" ใช้ step นั้นทิ้ง
    /// ใช้ตอนพนักงานสแกนเพื่อดูยอดก่อนตัดสินใจ การออกแต้มจริงคือตัวที่ใช้ step
    /// </summary>
    public async Task<CustomerStampSummary> ResolveByQrAsync(string? qrToken,
        CancellationToken ct = default)
    {
        var payload = CustomerQrToken.Verify(_qrSecret.Secret, qrToken, DateTime.UtcNow);
        var customer = await RequireCustomerByPublicIdAsync(payload.PublicId, ct);
        return await GetSummaryAsync(customer.Id, ct);
    }

    /// <summary>
    /// ออกแต้มให้ลูกค้าจากออเดอร์จริง หัวใจของ Phase 2
    /// เงื่อนไข: ออเดอร์รับเงินแล้ว, อยู่ในวันทำการเดียวกัน, วันนั้นยังไม่ปิดยอด,
    /// ออเดอร์นั้นยังไม่เคยออกแต้ม และ QR ยังไม่ถูกใช้
    /// </summary>
    public Task<StampEarnResult> EarnForOrderAsync(long orderId, string? qrToken,
        string? managerPin = null, CancellationToken ct = default)
    {
        var payload = CustomerQrToken.Verify(_qrSecret.Secret, qrToken, DateTime.UtcNow);

        return EarnCoreAsync(orderId,
            ct2 => RequireCustomerByPublicIdAsync(payload.PublicId, ct2),
            payload.Step,
            managerPin,
            ct);
    }

    /// <summary>
    /// ออกแต้มโดยพนักงานเลือกลูกค้าจากรายชื่อ ใช้ช่วงที่ยังไม่เปิด LIFF
    /// ลูกค้ากลุ่มนี้ไม่มี QR ของตัวเอง สิ่งที่ยังคุมอยู่คือแต้มต้องผูกออเดอร์ที่รับเงินแล้วเสมอ
    /// และทุกครั้งถูกบันทึกว่าใครเป็นคนออก เพื่อให้รายงานใน Phase 4 จับความผิดปกติได้
    /// </summary>
    public Task<StampEarnResult> EarnForOrderByCustomerAsync(long orderId, int customerId,
        string? managerPin = null, CancellationToken ct = default) =>
        EarnCoreAsync(orderId,
            ct2 => RequireCustomerByIdAsync(customerId, ct2),
            qrStep: null,
            managerPin,
            ct);

    private async Task<StampEarnResult> EarnCoreAsync(long orderId,
        Func<CancellationToken, Task<Customer>> resolveCustomer, long? qrStep,
        string? managerPin, CancellationToken ct)
    {
        var staffId = _staff.RequireStaffId();
        var deviceId = _staff.RequireDeviceId();
        var settings = await _settings.GetAsync(ct);

        for (var attempt = 1; ; attempt++)
        {
            var customer = await resolveCustomer(ct);

            if (!customer.HasConsented(_customers.ConsentVersion))
            {
                throw new DomainException(
                    "ลูกค้ายังไม่ได้ให้ความยินยอมตามเวอร์ชันปัจจุบัน " +
                    "ถ้าเป็นสมาชิกที่สมัครหน้าร้าน ให้แจ้งข้อความแล้วบันทึกความยินยอมใหม่");
            }

            // token เดิมใช้ได้ครั้งเดียว ภาพที่แคปไว้หรือส่งต่อจึงใช้ไม่ได้
            if (qrStep is { } step && step <= customer.LastUsedQrStep)
            {
                throw new DomainException(
                    "QR นี้ถูกใช้ไปแล้ว ให้ลูกค้ากดรีเฟรชหน้าบัตรเพื่อขอ QR ใหม่");
            }

            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new DomainException("ไม่พบออเดอร์");

            if (order.Status != OrderStatus.Paid)
                throw new DomainException("ออกแต้มได้เฉพาะออเดอร์ที่รับเงินแล้ว");

            // สแกนภายหลังได้ แต่ต้องอยู่ในวันทำการเดียวกัน
            if (order.BusinessDate != BangkokTime.Today())
                throw new DomainException("ออกแต้มได้เฉพาะออเดอร์ของวันทำการวันนี้");

            if (await _db.DailyCloses.AnyAsync(d => d.BusinessDate == order.BusinessDate, ct))
                throw new DomainException("วันทำการนี้ปิดยอดแล้ว ออกแต้มเพิ่มไม่ได้");

            // เช็กจาก ledger ไม่ใช่จาก order.CustomerId เพราะการย้ายแต้มทำให้ค่านั้นเปลี่ยนได้
            // และรายการ Earn เดิมยังอยู่ (ledger ลบไม่ได้) ข้อความต้องบอกทางแก้ให้พนักงานด้วย
            if (await _db.StampTransactions.AnyAsync(
                    t => t.OrderId == order.Id && t.Type == StampTransactionType.Earn, ct))
            {
                throw new DomainException(
                    "ออเดอร์นี้ออกแต้มไปแล้ว ถ้าสแตมป์ผิดคนให้ใช้ปุ่มย้ายแต้มที่หน้าออเดอร์");
            }

            var stamps = order.EligibleStampCount;
            if (stamps <= 0)
                throw new DomainException("ออเดอร์นี้ไม่มีเมนูที่นับแต้ม");

            // เพดานกันการแจกแต้มรัว ๆ ให้คนรู้จัก เกินได้ถ้ามีผู้จัดการอนุมัติ
            await EnsureWithinCapsAsync(customer, order, stamps, settings, managerPin, ct);

            var expiresAt = settings.StampExpiryMonths <= 0
                ? null
                : (DateTime?)BangkokTime.EndOfLocalDayUtc(
                    order.BusinessDate.AddMonths(settings.StampExpiryMonths));

            var balance = customer.Balance
                          ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้า");

            _db.StampTransactions.Add(new StampTransaction
            {
                CustomerId = customer.Id,
                Type = StampTransactionType.Earn,
                Quantity = stamps,
                OrderId = order.Id,
                ExpiresAt = expiresAt,
                StaffId = staffId,
                DeviceId = deviceId,
                CreatedAt = DateTime.UtcNow
            });

            balance.Stamps += stamps;
            balance.UpdatedAt = DateTime.UtcNow;
            order.CustomerId = customer.Id;

            if (qrStep is { } usedStep)
                customer.LastUsedQrStep = usedStep;

            _audit.Add("stamp.earn", nameof(StampTransaction), order.OrderNo, new
            {
                order.OrderNo,
                CustomerId = customer.Id,
                customer.PublicId,
                Stamps = stamps,
                NewBalance = balance.Stamps,
                ExpiresAt = expiresAt,
                QrStep = qrStep,
                // แยกให้ชัดว่าออกแต้มจาก QR หรือพนักงานเลือกเอง ใช้ทำรายงานความผิดปกติ
                Source = qrStep is null ? "ManualSelect" : "Qr"
            });

            try
            {
                await _db.SaveChangesAsync(ct);

                return new StampEarnResult(
                    stamps,
                    balance.Stamps,
                    settings.StampsPerReward,
                    order.Id,
                    order.OrderNo,
                    customer.Id,
                    customer.DisplayName,
                    customer.LineUserId,
                    expiresAt);
            }
            catch (DbUpdateException ex) when (IsDuplicateEarn(ex))
            {
                // ชน unique index ที่กัน Earn ซ้ำต่อออเดอร์ แปลว่ามีอีกเครื่องออกแต้มไปพร้อมกัน
                throw new DomainException("ออเดอร์นี้ออกแต้มไปแล้ว");
            }
            catch (DbUpdateConcurrencyException) when (attempt < ConcurrencyRetryLimit)
            {
                // ยอดแต้มถูกแก้พร้อมกัน ล้าง tracker แล้วอ่านใหม่ทั้งชุด
                _logger.LogInformation(
                    "ยอดแต้มของลูกค้า {CustomerId} ถูกแก้พร้อมกัน ลองใหม่ครั้งที่ {Attempt}",
                    customer.Id, attempt + 1);
                _db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// ย้อนแต้มของออเดอร์ที่ถูกยกเลิก เขียนเป็นรายการ Void ใหม่ ไม่แก้รายการ Earn เดิม
    /// ผู้เรียกเป็นคนสั่ง SaveChanges เพื่อให้อยู่ transaction เดียวกับการ void ออเดอร์
    /// </summary>
    public async Task<int> AddVoidForOrderAsync(Order order, CancellationToken ct = default)
    {
        var earn = await _db.StampTransactions
            .FirstOrDefaultAsync(t => t.OrderId == order.Id &&
                                      t.Type == StampTransactionType.Earn, ct);

        if (earn is null) return 0;

        var alreadyVoided = await _db.StampTransactions
            .AnyAsync(t => t.OrderId == order.Id && t.Type == StampTransactionType.Void, ct);

        if (alreadyVoided) return 0;

        var balance = await _db.CustomerBalances
            .FirstOrDefaultAsync(b => b.CustomerId == earn.CustomerId, ct)
            ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้า");

        // ถ้าแต้มก้อนนี้ถูกใช้แลกรางวัลไปแล้ว ต้อง block ตามที่ README กำหนด
        // เช็กจาก allocation ของก้อนนั้นตรง ๆ ไม่ใช่เดาจากยอดคงเหลือ
        var consumed = await _db.StampAllocations
            .Where(a => a.SourceTransactionId == earn.Id)
            .SumAsync(a => (int?)a.Quantity, ct) ?? 0;

        if (consumed > 0)
        {
            throw new DomainException(
                $"แต้มของออเดอร์นี้ถูกใช้ไปแล้ว {consumed} แต้ม (แลกรางวัลหรือหมดอายุ) " +
                "ยกเลิกเองไม่ได้ ให้เจ้าของร้านตรวจสอบก่อน");
        }

        if (balance.Stamps < earn.Quantity)
        {
            throw new DomainException(
                "ยอดแต้มคงเหลือน้อยกว่าที่จะย้อนคืน ให้เจ้าของร้านตรวจสอบก่อน");
        }

        _db.StampTransactions.Add(new StampTransaction
        {
            CustomerId = earn.CustomerId,
            Type = StampTransactionType.Void,
            Quantity = -earn.Quantity,
            OrderId = order.Id,
            StaffId = _staff.StaffId,
            DeviceId = _staff.DeviceId,
            Note = order.VoidReason,
            CreatedAt = DateTime.UtcNow
        });

        balance.Stamps -= earn.Quantity;
        balance.UpdatedAt = DateTime.UtcNow;

        _audit.Add("stamp.void", nameof(StampTransaction), order.OrderNo, new
        {
            order.OrderNo,
            earn.CustomerId,
            Stamps = -earn.Quantity,
            NewBalance = balance.Stamps,
            order.VoidReason
        });

        return earn.Quantity;
    }

    public async Task<CustomerStampSummary> GetSummaryAsync(int customerId,
        CancellationToken ct = default)
    {
        var customer = await _customers.RequireAsync(customerId, ct);
        var settings = await _settings.GetAsync(ct);
        var balance = customer.Balance?.Stamps ?? 0;
        var perReward = Math.Max(1, settings.StampsPerReward);

        return new CustomerStampSummary(
            customer.Id,
            customer.PublicId,
            customer.DisplayName,
            balance,
            settings.StampsPerReward,
            balance / perReward,
            balance % perReward == 0 && balance > 0 ? 0 : perReward - balance % perReward,
            await GetExpiringAsync(customer.Id, ct));
    }

    /// <summary>
    /// แต้มที่ใกล้หมดอายุ แยกตามวันหมดอายุ
    /// Phase 2 ยังไม่มีการแลก จึงนับจากรายการ Earn ที่ออเดอร์ไม่ถูกยกเลิก
    /// Phase 3 ต้องหักส่วนที่ถูกใช้ไปตาม StampAllocation ด้วย
    /// </summary>
    public async Task<List<ExpiringStamps>> GetExpiringAsync(int customerId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(ExpiringSoonDays);

        var voidedOrderIds = _db.StampTransactions
            .Where(t => t.CustomerId == customerId &&
                        t.Type == StampTransactionType.Void &&
                        t.OrderId != null)
            .Select(t => t.OrderId!.Value);

        var rows = await _db.StampTransactions
            .Where(t => t.CustomerId == customerId &&
                        t.Type == StampTransactionType.Earn &&
                        t.ExpiresAt != null &&
                        t.ExpiresAt > now &&
                        t.ExpiresAt <= horizon &&
                        (t.OrderId == null || !voidedOrderIds.Contains(t.OrderId.Value)))
            .Select(t => new { t.ExpiresAt, t.Quantity })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => DateOnly.FromDateTime(BangkokTime.ToLocal(r.ExpiresAt!.Value)))
            .Select(g => new ExpiringStamps(g.Key, g.Sum(r => r.Quantity)))
            .OrderBy(e => e.ExpiresOn)
            .ToList();
    }

    public async Task<List<StampHistoryItem>> GetHistoryAsync(int customerId, int limit = 20,
        CancellationToken ct = default)
    {
        return await _db.StampTransactions
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(t => new StampHistoryItem(
                t.CreatedAt,
                t.Type,
                t.Quantity,
                t.Order!.OrderNo,
                t.ExpiresAt,
                t.Note))
            .ToListAsync(ct);
    }

    /// <summary>
    /// ผลรวมของ ledger ต้องเท่ากับยอดใน customer_balances เสมอ
    /// ใช้ตรวจความถูกต้องของข้อมูล (Phase 4 จะทำเป็นรายงาน)
    /// </summary>
    public async Task<int> GetLedgerSumAsync(int customerId, CancellationToken ct = default) =>
        await _db.StampTransactions
            .Where(t => t.CustomerId == customerId)
            .SumAsync(t => t.Quantity, ct);

    /// <summary>
    /// ตัดแต้มแบบ FIFO: ใช้ก้อนที่ใกล้หมดอายุก่อน แล้วค่อยก้อนที่ยังอยู่อีกนาน
    /// ก้อนที่ไม่มีวันหมดอายุถูกใช้ทีหลังสุด เพราะไม่มีความเสี่ยงว่าจะเสียเปล่า
    ///
    /// เพิ่มแถว StampAllocation เข้า change tracker แต่ไม่ SaveChanges
    /// ผู้เรียกต้องบันทึกเองเพื่อให้ทุกอย่างอยู่ transaction เดียวกับการแลก
    /// </summary>
    public async Task<List<StampAllocation>> AllocateFifoAsync(int customerId, int quantity,
        StampTransaction consuming, CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new DomainException("จำนวนแต้มที่ตัดต้องมากกว่า 0");

        var now = DateTime.UtcNow;

        // ก้อนต้นทาง = รายการที่เพิ่มแต้ม (Earn หรือ Adjust ที่เป็นบวก) และยังไม่หมดอายุ
        var lots = await _db.StampTransactions
            .Where(t => t.CustomerId == customerId &&
                        t.Quantity > 0 &&
                        (t.ExpiresAt == null || t.ExpiresAt > now))
            .OrderBy(t => t.ExpiresAt == null)
            .ThenBy(t => t.ExpiresAt)
            .ThenBy(t => t.Id)
            .Select(t => new
            {
                Transaction = t,
                Used = _db.StampAllocations
                    .Where(a => a.SourceTransactionId == t.Id)
                    .Sum(a => (int?)a.Quantity) ?? 0
            })
            .ToListAsync(ct);

        var allocations = new List<StampAllocation>();
        var remaining = quantity;

        foreach (var lot in lots)
        {
            if (remaining == 0) break;

            var available = lot.Transaction.Quantity - lot.Used;
            if (available <= 0) continue;

            var take = Math.Min(available, remaining);
            var allocation = new StampAllocation
            {
                ConsumingTransaction = consuming,
                SourceTransactionId = lot.Transaction.Id,
                Quantity = take,
                CreatedAt = now
            };

            _db.StampAllocations.Add(allocation);
            allocations.Add(allocation);
            remaining -= take;
        }

        if (remaining > 0)
        {
            // ยอดคงเหลือกับก้อนที่ตัดได้ไม่ตรงกัน แปลว่าข้อมูลเพี้ยน ห้ามปล่อยผ่าน
            throw new DomainException(
                $"แต้มที่ใช้ได้ไม่พอ ขาดอีก {remaining} แต้ม (อาจมีแต้มหมดอายุแล้ว) " +
                "กรุณาแจ้งเจ้าของร้านตรวจสอบ");
        }

        return allocations;
    }

    /// <summary>แต้มที่ยังใช้ได้จริงของลูกค้า นับจากก้อนที่ยังไม่หมดอายุและยังไม่ถูกตัด</summary>
    public async Task<int> GetUsableStampsAsync(int customerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var lots = await _db.StampTransactions
            .Where(t => t.CustomerId == customerId &&
                        t.Quantity > 0 &&
                        (t.ExpiresAt == null || t.ExpiresAt > now))
            .Select(t => t.Quantity - (_db.StampAllocations
                .Where(a => a.SourceTransactionId == t.Id)
                .Sum(a => (int?)a.Quantity) ?? 0))
            .ToListAsync(ct);

        return lots.Sum();
    }

    /// <summary>
    /// งานรายวัน: เขียนรายการ Expire ให้แต้มที่เลยวันหมดอายุแล้วแต่ยังไม่ถูกใช้
    /// ทำเป็น append-only เหมือนรายการอื่น และผูก allocation ไว้ว่าหมดอายุจากก้อนไหน
    /// </summary>
    public async Task<int> ExpireDueStampsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var dueLots = await _db.StampTransactions
            .Where(t => t.Quantity > 0 && t.ExpiresAt != null && t.ExpiresAt <= now)
            .Select(t => new
            {
                Lot = t,
                Used = _db.StampAllocations
                    .Where(a => a.SourceTransactionId == t.Id)
                    .Sum(a => (int?)a.Quantity) ?? 0
            })
            .Where(x => x.Lot.Quantity - x.Used > 0)
            .ToListAsync(ct);

        if (dueLots.Count == 0) return 0;

        var totalExpired = 0;

        foreach (var group in dueLots.GroupBy(x => x.Lot.CustomerId))
        {
            var balance = await _db.CustomerBalances
                .FirstOrDefaultAsync(b => b.CustomerId == group.Key, ct);

            if (balance is null) continue;

            foreach (var item in group)
            {
                var amount = item.Lot.Quantity - item.Used;

                var expire = new StampTransaction
                {
                    CustomerId = group.Key,
                    Type = StampTransactionType.Expire,
                    Quantity = -amount,
                    ExpiresAt = null,
                    StaffId = null,
                    Note = $"หมดอายุ {BangkokTime.FormatDate(item.Lot.ExpiresAt!.Value)}",
                    CreatedAt = now
                };

                _db.StampTransactions.Add(expire);
                _db.StampAllocations.Add(new StampAllocation
                {
                    ConsumingTransaction = expire,
                    SourceTransactionId = item.Lot.Id,
                    Quantity = amount,
                    CreatedAt = now
                });

                balance.Stamps -= amount;
                balance.UpdatedAt = now;
                totalExpired += amount;
            }

            _audit.Add("stamp.expire", nameof(StampTransaction), group.Key.ToString(), new
            {
                CustomerId = group.Key,
                Stamps = group.Sum(x => x.Lot.Quantity - x.Used),
                NewBalance = balance.Stamps
            }, staffId: null);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("ตัดแต้มหมดอายุ {Total} แต้ม จาก {Lots} ก้อน",
            totalExpired, dueLots.Count);

        return totalExpired;
    }

    /// <summary>
    /// แก้สแตมป์ที่ผิด โดยไม่ต้องออกบัตรใหม่ (ปัญหาหลักของ reward card เดิมใน LINE)
    /// ledger เป็น append-only จึงไม่ลบรายการเก่า แต่เพิ่มรายการ Adjust/Correction เข้าไป
    ///
    /// ลดแต้ม (delta ลบ) พนักงานทำได้เอง เพราะเสียหายกับร้านไม่ได้
    /// เพิ่มแต้ม (delta บวก) ต้องใช้ PIN ผู้จัดการ เพราะเป็นการสร้างมูลค่าขึ้นมา
    /// </summary>
    public async Task<StampEarnResult> CorrectAsync(int customerId, int delta, string reason,
        string? managerPin, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var settings = await _settings.GetAsync(ct);

        if (delta == 0)
            throw new DomainException("ต้องระบุจำนวนแต้มที่ต้องการแก้");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ต้องระบุเหตุผลในการแก้แต้ม");

        if (Math.Abs(delta) > 50)
            throw new DomainException("แก้ได้ครั้งละไม่เกิน 50 แต้ม");

        var customer = await RequireCustomerByIdAsync(customerId, ct);
        var balance = customer.Balance ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้า");

        Staff? approver = null;
        if (delta > 0)
            approver = await _auth.VerifyManagerPinAsync(managerPin ?? "", ct);

        if (balance.Stamps + delta < 0)
        {
            throw new DomainException(
                $"ลดได้ไม่เกินยอดคงเหลือ ลูกค้ามี {balance.Stamps} แต้ม");
        }

        // แต้มที่เพิ่มด้วยการแก้ไขมีอายุเท่าแต้มปกติ นับจากวันที่แก้
        var expiresAt = delta > 0 && settings.StampExpiryMonths > 0
            ? BangkokTime.EndOfLocalDayUtc(BangkokTime.Today().AddMonths(settings.StampExpiryMonths))
            : (DateTime?)null;

        _db.StampTransactions.Add(new StampTransaction
        {
            CustomerId = customer.Id,
            Type = StampTransactionType.Adjust,
            AdjustKind = AdjustKind.Correction,
            Quantity = delta,
            ExpiresAt = expiresAt,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            ApprovedByStaffId = approver?.Id,
            Note = reason.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        balance.Stamps += delta;
        balance.UpdatedAt = DateTime.UtcNow;

        _audit.Add("stamp.correct", nameof(StampTransaction), customer.Id.ToString(), new
        {
            CustomerId = customer.Id,
            customer.DisplayName,
            Delta = delta,
            NewBalance = balance.Stamps,
            Reason = reason.Trim(),
            ApprovedBy = approver?.Name,
            ApprovedByStaffId = approver?.Id
        });

        await _db.SaveChangesAsync(ct);

        return new StampEarnResult(delta, balance.Stamps, settings.StampsPerReward,
            0, "", customer.Id, customer.DisplayName, customer.LineUserId, expiresAt);
    }

    /// <summary>
    /// สแตมป์ผิดคน: ย้ายแต้มของออเดอร์หนึ่งจากลูกค้าที่ผิดไปให้คนที่ถูก
    /// เขียนสองรายการในครั้งเดียว คือ Void ฝั่งที่ผิด และ Adjust/Correction ฝั่งที่ถูก
    /// ยอดรวมในระบบไม่เปลี่ยน แต่ฝั่งรับถือเป็นการเพิ่มแต้ม จึงต้องใช้ PIN ผู้จัดการ
    /// </summary>
    public async Task<StampEarnResult> TransferOrderStampsAsync(long orderId, int toCustomerId,
        string reason, string? managerPin, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var settings = await _settings.GetAsync(ct);

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ต้องระบุเหตุผลในการย้ายแต้ม");

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new DomainException("ไม่พบออเดอร์");

        if (await _db.DailyCloses.AnyAsync(d => d.BusinessDate == order.BusinessDate, ct))
            throw new DomainException("วันทำการนี้ปิดยอดแล้ว แก้แต้มย้อนหลังไม่ได้");

        var earn = await _db.StampTransactions
                       .FirstOrDefaultAsync(t => t.OrderId == orderId &&
                                                 t.Type == StampTransactionType.Earn, ct)
                   ?? throw new DomainException("ออเดอร์นี้ยังไม่ได้ออกแต้ม");

        if (earn.CustomerId == toCustomerId)
            throw new DomainException("เป็นลูกค้าคนเดิมอยู่แล้ว");

        var alreadyReversed = await _db.StampTransactions
            .AnyAsync(t => t.OrderId == orderId && t.Type == StampTransactionType.Void, ct);

        if (alreadyReversed)
            throw new DomainException("แต้มของออเดอร์นี้ถูกยกเลิกไปแล้ว");

        var approver = await _auth.VerifyManagerPinAsync(managerPin ?? "", ct);

        var fromBalance = await _db.CustomerBalances
                              .FirstOrDefaultAsync(b => b.CustomerId == earn.CustomerId, ct)
                          ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้าเดิม");

        if (fromBalance.Stamps < earn.Quantity)
        {
            throw new DomainException(
                "ลูกค้าเดิมมีแต้มเหลือน้อยกว่าที่จะย้ายออก อาจถูกใช้ไปแล้ว ให้เจ้าของร้านตรวจสอบ");
        }

        var target = await RequireCustomerByIdAsync(toCustomerId, ct);
        var toBalance = target.Balance ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้าที่รับ");
        var now = DateTime.UtcNow;
        var note = $"สแตมป์ผิดคน: {reason.Trim()}";

        _db.StampTransactions.Add(new StampTransaction
        {
            CustomerId = earn.CustomerId,
            Type = StampTransactionType.Void,
            Quantity = -earn.Quantity,
            OrderId = orderId,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            ApprovedByStaffId = approver.Id,
            Note = note,
            CreatedAt = now
        });

        _db.StampTransactions.Add(new StampTransaction
        {
            CustomerId = target.Id,
            Type = StampTransactionType.Adjust,
            AdjustKind = AdjustKind.Correction,
            Quantity = earn.Quantity,
            OrderId = orderId,
            ExpiresAt = earn.ExpiresAt,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            ApprovedByStaffId = approver.Id,
            Note = note,
            CreatedAt = now
        });

        fromBalance.Stamps -= earn.Quantity;
        fromBalance.UpdatedAt = now;
        toBalance.Stamps += earn.Quantity;
        toBalance.UpdatedAt = now;

        // ให้ออเดอร์ชี้ไปที่ลูกค้าที่ถูกต้อง รายงานจึงตรงกับความจริง
        order.CustomerId = target.Id;

        _audit.Add("stamp.transfer", nameof(StampTransaction), order.OrderNo, new
        {
            order.OrderNo,
            FromCustomerId = earn.CustomerId,
            ToCustomerId = target.Id,
            Stamps = earn.Quantity,
            Reason = reason.Trim(),
            ApprovedBy = approver.Name,
            ApprovedByStaffId = approver.Id
        });

        await _db.SaveChangesAsync(ct);

        return new StampEarnResult(earn.Quantity, toBalance.Stamps, settings.StampsPerReward,
            order.Id, order.OrderNo, target.Id, target.DisplayName, target.LineUserId,
            earn.ExpiresAt);
    }

    /// <summary>
    /// ย้ายแต้มจากบัตรกระดาษเดิม เป็นทางเดียวที่แต้มเกิดได้โดยไม่มีออเดอร์
    /// จึงต้องใช้ PIN ของเจ้าของร้าน และทำได้ครั้งเดียวต่อลูกค้าหนึ่งคนตลอดกาล
    /// (บังคับด้วย unique index ux_stamp_transactions_card_migration)
    /// </summary>
    public async Task<StampEarnResult> MigratePaperCardAsync(int customerId, int stamps,
        string ownerPin, string? note, CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();
        var settings = await _settings.GetAsync(ct);

        if (stamps <= 0)
            throw new DomainException("จำนวนแต้มต้องมากกว่า 0");

        // บัตรที่เต็มแล้วให้แลกฟรีหน้าร้านไปเลย ไม่ต้องย้ายเข้าระบบ
        // เพดานนี้จำกัดความเสียหายต่อคนไว้ไม่ให้เกินหนึ่งรอบรางวัล
        if (stamps >= settings.StampsPerReward)
        {
            throw new DomainException(
                $"ย้ายได้ไม่เกิน {settings.StampsPerReward - 1} แต้ม " +
                "ถ้าบัตรเต็มแล้วให้ลูกค้าแลกเครื่องดื่มฟรีหน้าร้านแทนการย้าย");
        }

        var customer = await RequireCustomerByIdAsync(customerId, ct);

        if (await _db.StampTransactions.AnyAsync(
                t => t.CustomerId == customerId && t.AdjustKind == AdjustKind.CardMigration, ct))
        {
            throw new DomainException("สมาชิกคนนี้ย้ายบัตรกระดาษไปแล้ว ย้ายซ้ำไม่ได้");
        }

        var approver = await _auth.VerifyOwnerPinAsync(ownerPin, ct);
        var balance = customer.Balance ?? throw new DomainException("ไม่พบยอดแต้มของลูกค้า");

        // แต้มที่ย้ายมาอายุเท่ากับแต้มใหม่ นับจากวันที่ย้าย
        var expiresAt = settings.StampExpiryMonths <= 0
            ? null
            : (DateTime?)BangkokTime.EndOfLocalDayUtc(
                BangkokTime.Today().AddMonths(settings.StampExpiryMonths));

        _db.StampTransactions.Add(new StampTransaction
        {
            CustomerId = customer.Id,
            Type = StampTransactionType.Adjust,
            AdjustKind = AdjustKind.CardMigration,
            Quantity = stamps,
            ExpiresAt = expiresAt,
            StaffId = staffId,
            DeviceId = _staff.DeviceId,
            ApprovedByStaffId = approver.Id,
            Note = string.IsNullOrWhiteSpace(note) ? "ย้ายจากบัตรกระดาษ" : note.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        balance.Stamps += stamps;
        balance.UpdatedAt = DateTime.UtcNow;

        _audit.Add("stamp.migrate_card", nameof(StampTransaction), customer.Id.ToString(), new
        {
            CustomerId = customer.Id,
            customer.DisplayName,
            Stamps = stamps,
            NewBalance = balance.Stamps,
            ApprovedBy = approver.Name,
            ApprovedByStaffId = approver.Id,
            ExpiresAt = expiresAt,
            Note = note
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateEarn(ex))
        {
            // ชน unique index ที่กันการย้ายซ้ำ แปลว่ามีอีกเครื่องย้ายไปพร้อมกัน
            throw new DomainException("สมาชิกคนนี้ย้ายบัตรกระดาษไปแล้ว ย้ายซ้ำไม่ได้");
        }

        return new StampEarnResult(stamps, balance.Stamps, settings.StampsPerReward,
            0, "", customer.Id, customer.DisplayName, customer.LineUserId ?? "", expiresAt);
    }

    /// <summary>
    /// เพดานแต้มตาม ShopSettings เกินได้ต่อเมื่อมี PIN ผู้จัดการ
    /// เพดานต่อวันนับเฉพาะแต้มที่ได้จากการซื้อในวันทำการเดียวกัน
    /// </summary>
    private async Task EnsureWithinCapsAsync(Customer customer, Order order, int stamps,
        ShopSettings settings, string? managerPin, CancellationToken ct)
    {
        var overLimits = new List<string>();

        if (stamps > settings.MaxStampsPerOrder)
            overLimits.Add($"เกินเพดานต่อออเดอร์ ({settings.MaxStampsPerOrder} แต้ม)");

        var earnedToday = await _db.StampTransactions
            .Where(t => t.CustomerId == customer.Id &&
                        t.Type == StampTransactionType.Earn &&
                        t.Order!.BusinessDate == order.BusinessDate)
            .SumAsync(t => (int?)t.Quantity, ct) ?? 0;

        if (earnedToday + stamps > settings.MaxStampsPerDayPerCustomer)
        {
            overLimits.Add(
                $"เกินเพดานต่อคนต่อวัน (วันนี้ได้ไปแล้ว {earnedToday} จาก " +
                $"{settings.MaxStampsPerDayPerCustomer} แต้ม)");
        }

        if (overLimits.Count == 0) return;

        var reason = string.Join(" และ ", overLimits);

        if (string.IsNullOrWhiteSpace(managerPin))
            throw new DomainException($"{reason} ต้องใช้ PIN ผู้จัดการเพื่ออนุมัติ");

        var approver = await _auth.VerifyManagerPinAsync(managerPin, ct);

        _audit.Add("stamp.cap_override", nameof(StampTransaction), order.OrderNo, new
        {
            order.OrderNo,
            CustomerId = customer.Id,
            Stamps = stamps,
            EarnedToday = earnedToday,
            Reason = reason,
            ApprovedBy = approver.Name,
            ApprovedByStaffId = approver.Id
        });
    }

    private async Task<Customer> RequireCustomerByIdAsync(int customerId, CancellationToken ct) =>
        await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
        ?? throw new DomainException("ไม่พบสมาชิกคนนี้");

    private async Task<Customer> RequireCustomerByPublicIdAsync(Guid publicId,
        CancellationToken ct) =>
        await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.PublicId == publicId && c.IsActive, ct)
        ?? throw new DomainException("ไม่พบบัญชีสมาชิกของ QR นี้");

    private static bool IsDuplicateEarn(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
