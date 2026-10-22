using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public class StaffAuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly IPasswordHasher<Staff> _hasher;

    public StaffAuthService(AppDbContext db, AuditService audit, IPasswordHasher<Staff> hasher)
    {
        _db = db;
        _audit = audit;
        _hasher = hasher;
    }

    public string HashPin(string pin) => _hasher.HashPassword(new Staff(), pin);

    public Task<List<Staff>> GetActiveStaffAsync(CancellationToken ct = default) =>
        _db.Staff.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    /// <summary>ตรวจ PIN ของพนักงานที่เลือก พร้อมนับครั้งที่ผิดและล็อกชั่วคราว</summary>
    public async Task<Staff> LoginAsync(int staffId, string pin, CancellationToken ct = default)
    {
        var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == staffId && s.IsActive, ct)
                    ?? throw new DomainException("ไม่พบพนักงานคนนี้");

        var now = DateTime.UtcNow;
        if (staff.LockedUntil is { } until && until > now)
        {
            var seconds = (int)Math.Ceiling((until - now).TotalSeconds);
            throw new DomainException($"PIN ผิดหลายครั้ง กรุณารออีก {seconds} วินาที");
        }

        var result = _hasher.VerifyHashedPassword(staff, staff.PinHash, pin);
        if (result == PasswordVerificationResult.Failed)
        {
            staff.FailedPinCount++;
            if (staff.FailedPinCount >= MaxFailedAttempts)
            {
                staff.LockedUntil = now.Add(LockoutDuration);
                staff.FailedPinCount = 0;
                _audit.Add("staff.lockout", nameof(Staff), staff.Id.ToString(),
                    new { staff.Name, LockedUntil = staff.LockedUntil }, staff.Id);
            }
            else
            {
                _audit.Add("staff.login_failed", nameof(Staff), staff.Id.ToString(),
                    new { staff.Name, staff.FailedPinCount }, staff.Id);
            }
            await _db.SaveChangesAsync(ct);
            throw new DomainException("PIN ไม่ถูกต้อง");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            staff.PinHash = _hasher.HashPassword(staff, pin);

        staff.FailedPinCount = 0;
        staff.LockedUntil = null;
        staff.LastLoginAt = now;
        _audit.Add("staff.login", nameof(Staff), staff.Id.ToString(), new { staff.Name }, staff.Id);
        await _db.SaveChangesAsync(ct);
        return staff;
    }

    /// <summary>
    /// ตรวจ PIN ผู้จัดการสำหรับ override เช่น การยกเลิกออเดอร์
    /// คืนผู้อนุมัติที่ตรวจผ่าน เพื่อบันทึกว่าใครอนุมัติ
    /// </summary>
    public async Task<Staff> VerifyManagerPinAsync(string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
            throw new DomainException("ต้องใช้ PIN ผู้จัดการ");

        var managers = await _db.Staff
            .Where(s => s.IsActive && (s.Role == StaffRole.Manager || s.Role == StaffRole.Owner))
            .ToListAsync(ct);

        foreach (var manager in managers)
        {
            if (_hasher.VerifyHashedPassword(manager, manager.PinHash, pin)
                != PasswordVerificationResult.Failed)
            {
                return manager;
            }
        }

        _audit.Add("staff.manager_pin_failed", nameof(Staff), null);
        await _db.SaveChangesAsync(ct);
        throw new DomainException("PIN ผู้จัดการไม่ถูกต้อง");
    }

    /// <summary>
    /// ตรวจ PIN ของเจ้าของร้านเท่านั้น ใช้กับงานที่สร้างแต้มขึ้นมาโดยไม่มีออเดอร์
    /// เช่นการย้ายบัตรกระดาษ ผู้จัดการอนุมัติเองไม่ได้
    /// </summary>
    public async Task<Staff> VerifyOwnerPinAsync(string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
            throw new DomainException("ต้องใช้ PIN ของเจ้าของร้าน");

        var owners = await _db.Staff
            .Where(s => s.IsActive && s.Role == StaffRole.Owner)
            .ToListAsync(ct);

        foreach (var owner in owners)
        {
            if (_hasher.VerifyHashedPassword(owner, owner.PinHash, pin)
                != PasswordVerificationResult.Failed)
            {
                return owner;
            }
        }

        _audit.Add("staff.owner_pin_failed", nameof(Staff), null);
        await _db.SaveChangesAsync(ct);
        throw new DomainException("PIN ของเจ้าของร้านไม่ถูกต้อง");
    }

    public async Task ChangePinAsync(int staffId, string currentPin, string newPin,
        CancellationToken ct = default)
    {
        var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == staffId && s.IsActive, ct)
                    ?? throw new DomainException("ไม่พบพนักงานคนนี้");

        if (_hasher.VerifyHashedPassword(staff, staff.PinHash, currentPin)
            == PasswordVerificationResult.Failed)
            throw new DomainException("PIN เดิมไม่ถูกต้อง");

        if (PinPolicy.Validate(newPin) is { } error)
            throw new DomainException(error);

        staff.PinHash = _hasher.HashPassword(staff, newPin);
        staff.MustChangePin = false;
        _audit.Add("staff.change_pin", nameof(Staff), staff.Id.ToString(), new { staff.Name });
        await _db.SaveChangesAsync(ct);
    }
}
