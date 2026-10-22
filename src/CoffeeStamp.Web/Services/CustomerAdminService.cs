using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public record CustomerListItem(
    int Id,
    string Name,
    string? Phone,
    int Balance,
    bool IsLineLinked,
    bool HasMigratedCard,
    DateTime CreatedAt);

public record CreateCustomerRequest(string Name, string? Phone, bool ConsentGivenAtCounter);

/// <summary>
/// จัดการสมาชิกฝั่งพนักงาน ใช้ในช่วงที่ยังไม่เปิด LIFF
/// ลูกค้ากลุ่มนี้ยังไม่มีบัญชี LINE จึงไม่มี QR ของตัวเอง พนักงานค้นชื่อหรือเบอร์แทน
/// </summary>
public class CustomerAdminService
{
    public const int MaxSearchResults = 25;

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _staff;
    private readonly CustomerAuthService _auth;

    public CustomerAdminService(AppDbContext db, AuditService audit, StaffContext staff,
        CustomerAuthService auth)
    {
        _db = db;
        _audit = audit;
        _staff = staff;
        _auth = auth;
    }

    /// <summary>ค้นด้วยชื่อหรือเบอร์โทร ถ้าไม่ส่งคำค้นมาจะคืนสมาชิกที่เพิ่งสร้างล่าสุด</summary>
    public async Task<List<CustomerListItem>> SearchAsync(string? query,
        CancellationToken ct = default)
    {
        var customers = _db.Customers.AsNoTracking().Where(c => c.IsActive);

        var term = query?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var digits = PhoneNumber.Normalize(term);

            customers = digits is not null
                // พิมพ์เป็นตัวเลข = ค้นจากเบอร์ (ใส่ไม่ครบก็เจอ)
                ? customers.Where(c => c.Phone != null && c.Phone.Contains(digits))
                : customers.Where(c => c.DisplayName != null &&
                                       EF.Functions.ILike(c.DisplayName, $"%{term}%"));
        }

        var rows = await customers
            .OrderByDescending(c => c.LastSeenAt ?? c.CreatedAt)
            .Take(MaxSearchResults)
            .Select(c => new
            {
                c.Id,
                c.DisplayName,
                c.Phone,
                Balance = c.Balance == null ? 0 : c.Balance.Stamps,
                c.LineUserId,
                c.CreatedAt,
                HasMigratedCard = _db.StampTransactions.Any(t =>
                    t.CustomerId == c.Id && t.AdjustKind == AdjustKind.CardMigration)
            })
            .ToListAsync(ct);

        return rows.Select(r => new CustomerListItem(
            r.Id,
            string.IsNullOrWhiteSpace(r.DisplayName) ? $"สมาชิก #{r.Id}" : r.DisplayName,
            r.Phone,
            r.Balance,
            !string.IsNullOrEmpty(r.LineUserId),
            r.HasMigratedCard,
            r.CreatedAt)).ToList();
    }

    /// <summary>เบอร์ซ้ำไม่ห้าม (คนในบ้านเดียวกันใช้เบอร์เดียวกันได้) แต่ต้องเตือนพนักงานก่อน</summary>
    public async Task<List<CustomerListItem>> FindByPhoneAsync(string? phone,
        CancellationToken ct = default)
    {
        var digits = PhoneNumber.Normalize(phone);
        if (digits is null) return [];
        return await SearchAsync(digits, ct);
    }

    public async Task<Customer> CreateAsync(CreateCustomerRequest request,
        CancellationToken ct = default)
    {
        var staffId = _staff.RequireStaffId();

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("กรุณากรอกชื่อลูกค้า");

        if (request.Name.Trim().Length > 100)
            throw new DomainException("ชื่อลูกค้ายาวเกินไป");

        if (PhoneNumber.Validate(request.Phone) is { } phoneError)
            throw new DomainException(phoneError);

        // ลูกค้ากลุ่มนี้ไม่เคยเห็นหน้าขอความยินยอมใน LIFF
        // พนักงานต้องยืนยันว่าแจ้งข้อความที่เคาน์เตอร์แล้ว ไม่งั้นเก็บข้อมูลไม่ได้
        if (!request.ConsentGivenAtCounter)
        {
            throw new DomainException(
                "ต้องแจ้งข้อความขอความยินยอมให้ลูกค้าทราบที่เคาน์เตอร์ก่อน จึงจะบันทึกข้อมูลได้");
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            DisplayName = request.Name.Trim(),
            Phone = PhoneNumber.Normalize(request.Phone),
            Source = CustomerSource.Counter,
            CreatedByStaffId = staffId,
            ConsentVersion = _auth.ConsentVersion,
            ConsentAt = now,
            ConsentSource = Domain.ConsentSource.Counter,
            CreatedAt = now,
            LastSeenAt = now,
            Balance = new CustomerBalance { Stamps = 0, UpdatedAt = now }
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        _audit.Add("customer.create_at_counter", nameof(Customer), customer.Id.ToString(), new
        {
            customer.DisplayName,
            customer.Phone,
            customer.PublicId,
            ConsentVersion = customer.ConsentVersion,
            ConsentSource = customer.ConsentSource.ToString()
        });
        await _db.SaveChangesAsync(ct);

        return customer;
    }

    public async Task<Customer> UpdateAsync(int id, string name, string? phone,
        CancellationToken ct = default)
    {
        var customer = await RequireAsync(id, ct);

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("กรุณากรอกชื่อลูกค้า");

        if (PhoneNumber.Validate(phone) is { } phoneError)
            throw new DomainException(phoneError);

        var before = new { customer.DisplayName, customer.Phone };
        customer.DisplayName = name.Trim();
        customer.Phone = PhoneNumber.Normalize(phone);

        _audit.Add("customer.update", nameof(Customer), customer.Id.ToString(),
            new { Before = before, After = new { customer.DisplayName, customer.Phone } });

        await _db.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>
    /// ผูกบัญชี LINE ของลูกค้าเข้ากับระเบียนที่คีย์มือไว้ ทำที่เคาน์เตอร์โดยพนักงานสแกน QR ของลูกค้า
    /// ทั้งลูกค้าและพนักงานต้องอยู่ตรงนั้นพร้อมกัน จึงไม่มีช่องให้อ้างสิทธิ์แต้มของคนอื่น
    ///
    /// ย้าย line_user_id มาที่ระเบียนเดิมแทนการย้าย ledger เพราะ ledger เป็น append-only
    /// และอนุญาตเฉพาะกรณีที่ระเบียนฝั่ง LINE ยังไม่มีประวัติแต้มเลย
    /// </summary>
    public async Task<Customer> LinkLineAccountAsync(int counterCustomerId, Guid linePublicId,
        CancellationToken ct = default)
    {
        var target = await RequireAsync(counterCustomerId, ct);

        if (target.IsLineLinked)
            throw new DomainException("สมาชิกคนนี้ผูกบัญชี LINE ไว้แล้ว");

        var lineCustomer = await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.PublicId == linePublicId, ct)
            ?? throw new DomainException("ไม่พบบัญชีจาก QR นี้");

        if (lineCustomer.Id == target.Id)
            throw new DomainException("เป็นสมาชิกคนเดียวกันอยู่แล้ว");

        if (!lineCustomer.IsLineLinked)
            throw new DomainException("QR นี้ไม่ได้มาจากบัญชี LINE");

        var hasHistory = await _db.StampTransactions
            .AnyAsync(t => t.CustomerId == lineCustomer.Id, ct);

        if (hasHistory || (lineCustomer.Balance?.Stamps ?? 0) != 0)
        {
            throw new DomainException(
                "บัญชี LINE นี้มีประวัติแต้มอยู่แล้ว รวมบัญชีเองไม่ได้ ให้เจ้าของร้านตรวจสอบก่อน");
        }

        // ย้ายข้อมูลฝั่ง LINE มาที่ระเบียนเดิมที่ถือแต้มอยู่
        target.LineUserId = lineCustomer.LineUserId;
        target.PictureUrl = lineCustomer.PictureUrl;
        target.LastSeenAt = DateTime.UtcNow;

        if (lineCustomer.ConsentAt is not null)
        {
            target.ConsentVersion = lineCustomer.ConsentVersion;
            target.ConsentAt = lineCustomer.ConsentAt;
            target.ConsentSource = lineCustomer.ConsentSource;
        }

        // ปิดระเบียนเปล่าฝั่ง LINE และปล่อย line_user_id ให้ไม่ชน unique index
        lineCustomer.LineUserId = null;
        lineCustomer.IsActive = false;
        lineCustomer.DisplayName = null;
        lineCustomer.PictureUrl = null;

        _audit.Add("customer.link_line", nameof(Customer), target.Id.ToString(), new
        {
            MergedFromCustomerId = lineCustomer.Id,
            target.PublicId,
            target.DisplayName
        });

        await _db.SaveChangesAsync(ct);
        return target;
    }

    /// <summary>ย้ายบัตรกระดาษไปแล้วหรือยัง ใช้ซ่อนฟอร์มย้ายไม่ให้พนักงานกดซ้ำ</summary>
    public Task<bool> HasMigratedCardAsync(int customerId, CancellationToken ct = default) =>
        _db.StampTransactions.AnyAsync(
            t => t.CustomerId == customerId && t.AdjustKind == AdjustKind.CardMigration, ct);

    public async Task<Customer> RequireAsync(int id, CancellationToken ct = default) =>
        await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, ct)
        ?? throw new DomainException("ไม่พบสมาชิกคนนี้");
}
