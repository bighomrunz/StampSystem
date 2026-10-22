using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public record StaffInput(string Name, StaffRole Role, string? Pin);

public class StaffAdminService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly StaffContext _current;
    private readonly StaffAuthService _auth;

    public StaffAdminService(AppDbContext db, AuditService audit, StaffContext current,
        StaffAuthService auth)
    {
        _db = db;
        _audit = audit;
        _current = current;
        _auth = auth;
    }

    public Task<List<Staff>> GetAllAsync(CancellationToken ct = default) =>
        _db.Staff.AsNoTracking()
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<Staff> CreateAsync(StaffInput input, CancellationToken ct = default)
    {
        EnsureCanAssignRole(input.Role);

        if (string.IsNullOrWhiteSpace(input.Name))
            throw new DomainException("กรุณากรอกชื่อพนักงาน");

        if (PinPolicy.Validate(input.Pin) is { } pinError)
            throw new DomainException(pinError);

        var staff = new Staff
        {
            Name = input.Name.Trim(),
            Role = input.Role,
            PinHash = _auth.HashPin(input.Pin!),
            IsActive = true,
            // PIN ตั้งต้นมาจากผู้จัดการ จึงต้องให้พนักงานเปลี่ยนเองตอนล็อกอินครั้งแรก
            MustChangePin = true
        };

        // ต้องผ่าน execution strategy เพราะเปิด EnableRetryOnFailure ไว้
        // ไม่งั้น transaction ที่เปิดเองจะถูกปฏิเสธ
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.Staff.Add(staff);
            await _db.SaveChangesAsync(ct);

            _audit.Add("staff.create", nameof(Staff), staff.Id.ToString(),
                new { staff.Name, Role = staff.Role.ToString() });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return staff;
    }

    public async Task<Staff> UpdateAsync(int id, string name, StaffRole role,
        CancellationToken ct = default)
    {
        var staff = await RequireStaffAsync(id, ct);
        EnsureCanAssignRole(role);
        EnsureCanAssignRole(staff.Role);

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("กรุณากรอกชื่อพนักงาน");

        if (staff.Role == StaffRole.Owner && role != StaffRole.Owner)
            await EnsureNotLastOwnerAsync(staff.Id, ct);

        var before = new { staff.Name, Role = staff.Role.ToString() };
        staff.Name = name.Trim();
        staff.Role = role;

        _audit.Add("staff.update", nameof(Staff), staff.Id.ToString(), new
        {
            Before = before,
            After = new { staff.Name, Role = staff.Role.ToString() }
        });

        await _db.SaveChangesAsync(ct);
        return staff;
    }

    /// <summary>ผู้จัดการตั้ง PIN ใหม่ให้พนักงานที่ลืม PIN และบังคับให้เปลี่ยนเองเมื่อล็อกอินครั้งถัดไป</summary>
    public async Task ResetPinAsync(int id, string newPin, CancellationToken ct = default)
    {
        var staff = await RequireStaffAsync(id, ct);
        EnsureCanAssignRole(staff.Role);

        if (PinPolicy.Validate(newPin) is { } error)
            throw new DomainException(error);

        staff.PinHash = _auth.HashPin(newPin);
        staff.MustChangePin = true;
        staff.FailedPinCount = 0;
        staff.LockedUntil = null;

        _audit.Add("staff.reset_pin", nameof(Staff), staff.Id.ToString(), new { staff.Name });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>ไม่ลบพนักงานจริง เพราะออเดอร์และ audit log อ้างถึง ใช้ปิดใช้งานแทน</summary>
    public async Task SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var staff = await RequireStaffAsync(id, ct);
        EnsureCanAssignRole(staff.Role);

        if (!isActive)
        {
            if (staff.Id == _current.StaffId)
                throw new DomainException("ปิดใช้งานบัญชีตัวเองไม่ได้");
            if (staff.Role == StaffRole.Owner)
                await EnsureNotLastOwnerAsync(staff.Id, ct);
        }

        staff.IsActive = isActive;
        _audit.Add(isActive ? "staff.enable" : "staff.disable", nameof(Staff), staff.Id.ToString(),
            new { staff.Name });
        await _db.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(int id, CancellationToken ct = default)
    {
        var staff = await RequireStaffAsync(id, ct);
        staff.LockedUntil = null;
        staff.FailedPinCount = 0;
        _audit.Add("staff.unlock", nameof(Staff), staff.Id.ToString(), new { staff.Name });
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Staff> RequireStaffAsync(int id, CancellationToken ct) =>
        await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new DomainException("ไม่พบพนักงานคนนี้");

    /// <summary>มีได้เฉพาะเจ้าของร้านที่จัดการบัญชีระดับเจ้าของร้าน</summary>
    private void EnsureCanAssignRole(StaffRole role)
    {
        if (role == StaffRole.Owner && _current.Role != StaffRole.Owner)
            throw new DomainException("เฉพาะเจ้าของร้านเท่านั้นที่จัดการบัญชีระดับเจ้าของร้านได้");
    }

    private async Task EnsureNotLastOwnerAsync(int staffId, CancellationToken ct)
    {
        var otherOwners = await _db.Staff
            .CountAsync(s => s.IsActive && s.Role == StaffRole.Owner && s.Id != staffId, ct);

        if (otherOwners == 0)
            throw new DomainException("ต้องมีบัญชีเจ้าของร้านที่ใช้งานได้อย่างน้อย 1 บัญชี");
    }
}
