using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Web.Services;

public class CustomerAuthService
{
    public const string DefaultConsentVersion = "2026-10-16";

    private readonly AppDbContext _db;
    private readonly LineIdTokenVerifier _verifier;
    private readonly AuditService _audit;
    private readonly LineOptions _line;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CustomerAuthService> _logger;

    public CustomerAuthService(AppDbContext db, LineIdTokenVerifier verifier, AuditService audit,
        IOptions<LineOptions> line, IHostEnvironment environment, IConfiguration config,
        ILogger<CustomerAuthService> logger)
    {
        _db = db;
        _verifier = verifier;
        _audit = audit;
        _line = line.Value;
        _environment = environment;
        _logger = logger;
        ConsentVersion = config["Pdpa:Version"] ?? DefaultConsentVersion;
    }

    /// <summary>เวอร์ชันข้อความขอความยินยอมที่ใช้อยู่ ถ้าเปลี่ยนค่านี้ ลูกค้าทุกคนต้องยินยอมใหม่</summary>
    public string ConsentVersion { get; }

    /// <summary>
    /// ล็อกอินลูกค้าด้วย ID token จาก LIFF
    /// userId ที่ใช้ต้องมาจากการ verify กับ LINE เท่านั้น ไม่รับจาก client
    /// </summary>
    public async Task<Customer> AuthenticateAsync(string? idToken, CancellationToken ct = default)
    {
        var profile = await _verifier.VerifyAsync(idToken, ct);
        return await UpsertAsync(profile, ct);
    }

    /// <summary>
    /// ล็อกอินปลอมสำหรับทดสอบในเครื่อง ใช้ได้เฉพาะ Development และต้องเปิด Line:AllowDevMockLogin
    /// </summary>
    public async Task<Customer> AuthenticateDevAsync(string? devUserId, string? displayName,
        CancellationToken ct = default)
    {
        if (!_environment.IsDevelopment() || !_line.AllowDevMockLogin)
            throw new DomainException("โหมดทดสอบถูกปิดอยู่");

        if (string.IsNullOrWhiteSpace(devUserId))
            throw new DomainException("ต้องระบุ devUserId");

        _logger.LogWarning("ล็อกอินลูกค้าด้วยโหมดทดสอบ devUserId={UserId}", devUserId);

        var profile = new LineProfile(
            $"dev-{devUserId.Trim()}",
            string.IsNullOrWhiteSpace(displayName) ? $"ลูกค้าทดสอบ {devUserId}" : displayName.Trim(),
            null);

        return await UpsertAsync(profile, ct);
    }

    private async Task<Customer> UpsertAsync(LineProfile profile, CancellationToken ct)
    {
        var customer = await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.LineUserId == profile.UserId, ct);

        var now = DateTime.UtcNow;

        if (customer is null)
        {
            customer = new Customer
            {
                LineUserId = profile.UserId,
                DisplayName = Truncate(profile.DisplayName, 100),
                PictureUrl = Truncate(profile.PictureUrl, 500),
                CreatedAt = now,
                LastSeenAt = now,
                Balance = new CustomerBalance { Stamps = 0, UpdatedAt = now }
            };

            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);

            _audit.Add("customer.register", nameof(Customer), customer.Id.ToString(),
                new { customer.PublicId, customer.DisplayName });
            await _db.SaveChangesAsync(ct);
            return customer;
        }

        if (!customer.IsActive)
            throw new DomainException("บัญชีสมาชิกนี้ถูกปิดใช้งาน กรุณาติดต่อร้าน");

        customer.DisplayName = Truncate(profile.DisplayName, 100) ?? customer.DisplayName;
        customer.PictureUrl = Truncate(profile.PictureUrl, 500) ?? customer.PictureUrl;
        customer.LastSeenAt = now;

        // บัญชีเก่าที่ยังไม่มีแถวยอดแต้ม เช่นข้อมูลที่ย้ายมา
        customer.Balance ??= new CustomerBalance { CustomerId = customer.Id, UpdatedAt = now };

        await _db.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>
    /// หาบัญชีที่ยังใช้งานได้ คืน null ถ้าไม่มีหรือถูกปิดไปแล้ว
    /// เกิดได้เมื่อพนักงานผูกบัญชี LINE เข้ากับระเบียนที่คีย์มือไว้ แล้วระเบียนฝั่ง LINE เดิมถูกปิด
    /// เซสชันที่ค้างอยู่จึงต้องถูกตัดให้ล็อกอินใหม่ ไม่ใช่โชว์ error
    /// </summary>
    public Task<Customer?> FindActiveAsync(int customerId, CancellationToken ct = default) =>
        _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct);

    public async Task<Customer> RequireAsync(int customerId, CancellationToken ct = default) =>
        await _db.Customers
            .Include(c => c.Balance)
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
        ?? throw new DomainException("ไม่พบบัญชีสมาชิก");

    /// <summary>บันทึกความยินยอม PDPA พร้อมเวอร์ชันของข้อความที่ลูกค้าเห็นตอนกดยอมรับ</summary>
    public async Task<Customer> RecordConsentAsync(int customerId, string? acceptedVersion,
        CancellationToken ct = default)
    {
        var customer = await RequireAsync(customerId, ct);

        if (!string.Equals(acceptedVersion, ConsentVersion, StringComparison.Ordinal))
            throw new DomainException("ข้อความขอความยินยอมมีการอัปเดต กรุณารีเฟรชหน้านี้");

        customer.ConsentVersion = ConsentVersion;
        customer.ConsentAt = DateTime.UtcNow;

        _audit.Add("customer.consent", nameof(Customer), customer.Id.ToString(),
            new { customer.ConsentVersion, customer.ConsentAt });

        await _db.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>
    /// ลูกค้าขอถอนความยินยอมและหยุดใช้งาน ไม่ลบ ledger เพราะเป็นหลักฐานทางบัญชี
    /// แต่ปิดบัญชีและลบข้อมูลที่ระบุตัวตนที่ไม่จำเป็นออก
    /// </summary>
    public async Task WithdrawConsentAsync(int customerId, CancellationToken ct = default)
    {
        var customer = await RequireAsync(customerId, ct);

        customer.IsActive = false;
        customer.DisplayName = null;
        customer.PictureUrl = null;
        customer.ConsentVersion = null;
        customer.ConsentAt = null;

        // LINE user id เป็นข้อมูลที่ระบุตัวบุคคลได้ ต้องลบออกด้วยถึงจะตรงกับที่แจ้งลูกค้าไว้
        // ใส่ค่าสุ่มแทน null เพราะคอลัมน์เป็น NOT NULL + unique และ ledger ยังต้องอ้างแถวนี้อยู่
        // ผลคือถ้าลูกค้ากลับมาสมัครใหม่ จะได้บัตรใบใหม่ แต้มเดิมไม่โอนมา (แจ้งไว้ในข้อความยินยอมแล้ว)
        customer.LineUserId = $"withdrawn-{Guid.NewGuid():N}";

        _audit.Add("customer.withdraw_consent", nameof(Customer), customer.Id.ToString(),
            new { customer.PublicId, Anonymized = true });

        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
