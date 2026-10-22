using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

/// <summary>ยอดที่ระบบคาดไว้ของวันทำการหนึ่งวัน แยกตามช่องทางชำระ</summary>
public record DailyCloseSummary(
    DateOnly BusinessDate,
    decimal ExpectedCash,
    decimal ExpectedPromptPay,
    decimal ExpectedPaoTang,
    int PaidOrderCount,
    int VoidedOrderCount,
    int PendingOrderCount,
    decimal VoidedAmount,
    DailyClose? Close)
{
    public decimal ExpectedTotal => ExpectedCash + ExpectedPromptPay + ExpectedPaoTang;
    public bool IsClosed => Close is not null;
}

public record DailyCloseRequest(
    decimal ActualCash,
    decimal ActualPromptPay,
    decimal ActualPaoTang,
    string? Note);

public class DailyCloseService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;

    public DailyCloseService(AppDbContext db, AuditService audit, StaffContext staff)
    {
        _db = db;
        _audit = audit;
        _staff = staff;
    }

    public async Task<DailyCloseSummary> GetSummaryAsync(DateOnly businessDate,
        CancellationToken ct = default)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.BusinessDate == businessDate)
            .Select(o => new { o.Status, o.PaymentMethod, o.Total })
            .ToListAsync(ct);

        var paid = orders.Where(o => o.Status == OrderStatus.Paid).ToList();

        decimal SumOf(PaymentMethod method) =>
            Money.Round(paid.Where(o => o.PaymentMethod == method).Sum(o => o.Total));

        var close = await _db.DailyCloses.AsNoTracking()
            .Include(d => d.ClosedByStaff)
            .FirstOrDefaultAsync(d => d.BusinessDate == businessDate, ct);

        return new DailyCloseSummary(
            businessDate,
            SumOf(PaymentMethod.Cash),
            SumOf(PaymentMethod.PromptPay),
            SumOf(PaymentMethod.PaoTang),
            paid.Count,
            orders.Count(o => o.Status == OrderStatus.Voided),
            orders.Count(o => o.Status == OrderStatus.Pending),
            Money.Round(orders.Where(o => o.Status == OrderStatus.Voided).Sum(o => o.Total)),
            close);
    }

    /// <summary>
    /// ปิดยอดวันทำการ บันทึกยอดจริงที่นับได้ทีละช่องทางพร้อมส่วนต่าง
    /// ปิดแล้วออเดอร์ของวันนั้นแก้ไขไม่ได้อีก (ดู OrderService.EnsureDayOpenAsync)
    /// </summary>
    public async Task<DailyClose> CloseAsync(DateOnly businessDate, DailyCloseRequest request,
        CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();

        if (!_staff.IsManagerOrAbove)
            throw new DomainException("ปิดยอดได้เฉพาะผู้จัดการหรือเจ้าของร้าน");

        if (businessDate > BangkokTime.Today())
            throw new DomainException("ปิดยอดล่วงหน้าไม่ได้");

        if (await _db.DailyCloses.AnyAsync(d => d.BusinessDate == businessDate, ct))
            throw new DomainException($"วันทำการ {businessDate:dd/MM/yyyy} ปิดยอดไปแล้ว");

        if (request.ActualCash < 0 || request.ActualPromptPay < 0 || request.ActualPaoTang < 0)
            throw new DomainException("ยอดจริงต้องไม่เป็นค่าลบ");

        var summary = await GetSummaryAsync(businessDate, ct);

        if (summary.PendingOrderCount > 0)
            throw new DomainException(
                $"ยังมีออเดอร์ที่รอยืนยันการรับเงิน {summary.PendingOrderCount} รายการ " +
                "กรุณายืนยันหรือยกเลิกให้ครบก่อนปิดยอด");

        var close = new DailyClose
        {
            BusinessDate = businessDate,
            ExpectedCash = summary.ExpectedCash,
            ExpectedPromptPay = summary.ExpectedPromptPay,
            ExpectedPaoTang = summary.ExpectedPaoTang,
            ActualCash = Money.Round(request.ActualCash),
            ActualPromptPay = Money.Round(request.ActualPromptPay),
            ActualPaoTang = Money.Round(request.ActualPaoTang),
            OrderCount = summary.PaidOrderCount,
            VoidCount = summary.VoidedOrderCount,
            ClosedByStaffId = staffId,
            ClosedAt = DateTime.UtcNow,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        close.VarianceCash = Money.Round(close.ActualCash - close.ExpectedCash);
        close.VariancePromptPay = Money.Round(close.ActualPromptPay - close.ExpectedPromptPay);
        close.VariancePaoTang = Money.Round(close.ActualPaoTang - close.ExpectedPaoTang);
        close.VarianceTotal = Money.Round(
            close.VarianceCash + close.VariancePromptPay + close.VariancePaoTang);

        _db.DailyCloses.Add(close);
        _audit.Add("daily_close.create", nameof(DailyClose), businessDate.ToString("yyyy-MM-dd"), new
        {
            BusinessDate = businessDate.ToString("yyyy-MM-dd"),
            close.ExpectedCash,
            close.ExpectedPromptPay,
            close.ExpectedPaoTang,
            close.ActualCash,
            close.ActualPromptPay,
            close.ActualPaoTang,
            close.VarianceTotal,
            close.OrderCount,
            close.VoidCount,
            close.Note
        });

        await _db.SaveChangesAsync(ct);
        return close;
    }

    /// <summary>รายการวันทำการที่ยังไม่ปิดยอดและเลยวันไปแล้ว ใช้เตือนผู้จัดการ</summary>
    public async Task<List<DateOnly>> GetUnclosedPastDatesAsync(CancellationToken ct = default)
    {
        var today = BangkokTime.Today();

        var orderDates = await _db.Orders.AsNoTracking()
            .Where(o => o.BusinessDate < today)
            .Select(o => o.BusinessDate)
            .Distinct()
            .ToListAsync(ct);

        var closedDates = await _db.DailyCloses.AsNoTracking()
            .Where(d => d.BusinessDate < today)
            .Select(d => d.BusinessDate)
            .ToListAsync(ct);

        return orderDates.Except(closedDates).OrderBy(d => d).ToList();
    }
}
