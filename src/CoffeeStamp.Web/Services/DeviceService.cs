using System.Security.Cryptography;
using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public class DeviceService
{
    public const string CookieName = "cs_device";

    /// <summary>ตัวอักษรที่ไม่กำกวมตอนอ่านจากหน้าจอ ตัด I O 0 1 ออก</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public DeviceService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public static string GenerateCode(int length = 6) =>
        new(Enumerable.Range(0, length)
            .Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
            .ToArray());

    public Task<List<Device>> GetAllAsync(CancellationToken ct = default) =>
        _db.Devices.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);

    /// <summary>ยังไม่มีอุปกรณ์ที่ลงทะเบียนและเปิดใช้งาน = อยู่ในช่วงติดตั้งครั้งแรก</summary>
    public Task<bool> IsBootstrapAsync(CancellationToken ct = default) =>
        _db.Devices.AllAsync(d => d.RegisteredAt == null || !d.IsActive, ct);

    public async Task<Device> CreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("กรุณาตั้งชื่ออุปกรณ์");

        var device = new Device { Name = name.Trim(), RegistrationCode = GenerateCode() };
        _db.Devices.Add(device);
        _audit.Add("device.create", nameof(Device), device.Id.ToString(), new { device.Name });
        await _db.SaveChangesAsync(ct);
        return device;
    }

    /// <summary>ใช้รหัสลงทะเบียนผูกเครื่องนี้ รหัสใช้ได้ครั้งเดียวแล้วถูกล้างทิ้ง</summary>
    public async Task<Device> RegisterAsync(string code, string? userAgent, CancellationToken ct = default)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            throw new DomainException("กรุณากรอกรหัสลงทะเบียน");

        var device = await _db.Devices
                         .FirstOrDefaultAsync(d => d.RegistrationCode == normalized && d.IsActive, ct)
                     ?? throw new DomainException("รหัสลงทะเบียนไม่ถูกต้องหรือถูกใช้ไปแล้ว");

        device.RegistrationCode = null;
        device.RegisteredAt = DateTime.UtcNow;
        device.LastSeenAt = DateTime.UtcNow;
        device.UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        _audit.Add("device.register", nameof(Device), device.Id.ToString(), new { device.Name });
        await _db.SaveChangesAsync(ct);
        return device;
    }

    public async Task<Device?> GetActiveAsync(Guid? deviceId, CancellationToken ct = default)
    {
        if (deviceId is null) return null;
        return await _db.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.IsActive && d.RegisteredAt != null, ct);
    }

    public async Task TouchAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null) return;
        device.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid deviceId, bool isActive, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct)
                     ?? throw new DomainException("ไม่พบอุปกรณ์");
        device.IsActive = isActive;
        _audit.Add(isActive ? "device.enable" : "device.disable", nameof(Device),
            device.Id.ToString(), new { device.Name });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>ออกรหัสลงทะเบียนใหม่ เช่น ย้ายเครื่องหรือเครื่องถูกล้างข้อมูล</summary>
    public async Task<string> ReissueCodeAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct)
                     ?? throw new DomainException("ไม่พบอุปกรณ์");

        device.RegistrationCode = GenerateCode();
        device.RegisteredAt = null;
        _audit.Add("device.reissue_code", nameof(Device), device.Id.ToString(), new { device.Name });
        await _db.SaveChangesAsync(ct);
        return device.RegistrationCode;
    }
}
