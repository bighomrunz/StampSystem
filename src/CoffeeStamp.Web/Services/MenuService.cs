using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Services;

public record MenuItemInput(
    string Name,
    string? Category,
    decimal Price,
    bool EarnsStamp,
    bool IsRewardEligible,
    int SortOrder);

public class MenuService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public MenuService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<MenuItem>> GetActiveAsync(CancellationToken ct = default) =>
        _db.MenuItems.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(ct);

    public Task<List<MenuItem>> GetAllAsync(CancellationToken ct = default) =>
        _db.MenuItems.AsNoTracking()
            .OrderByDescending(m => m.IsActive)
            .ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync(ct);

    public async Task<MenuItem> CreateAsync(MenuItemInput input, CancellationToken ct = default)
    {
        Validate(input);

        var item = new MenuItem
        {
            Name = input.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim(),
            Price = Money.Round(input.Price),
            EarnsStamp = input.EarnsStamp,
            IsRewardEligible = input.IsRewardEligible,
            SortOrder = input.SortOrder,
            IsActive = true
        };

        // Id ยังไม่เกิดก่อน insert จึงต้องบันทึก audit หลัง insert แต่ให้อยู่ transaction เดียวกัน
        // และต้องเรียกผ่าน execution strategy เพราะเปิด EnableRetryOnFailure ไว้
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.MenuItems.Add(item);
            await _db.SaveChangesAsync(ct);

            _audit.Add("menu.create", nameof(MenuItem), item.Id.ToString(),
                new { item.Name, item.Price, item.EarnsStamp, item.IsRewardEligible });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return item;
    }

    public async Task<MenuItem> UpdateAsync(int id, MenuItemInput input, CancellationToken ct = default)
    {
        Validate(input);

        var item = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct)
                   ?? throw new DomainException("ไม่พบเมนูนี้");

        var before = new { item.Name, item.Price, item.EarnsStamp, item.IsRewardEligible, item.Category };

        item.Name = input.Name.Trim();
        item.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        item.Price = Money.Round(input.Price);
        item.EarnsStamp = input.EarnsStamp;
        item.IsRewardEligible = input.IsRewardEligible;
        item.SortOrder = input.SortOrder;

        _audit.Add("menu.update", nameof(MenuItem), item.Id.ToString(), new
        {
            Before = before,
            After = new { item.Name, item.Price, item.EarnsStamp, item.IsRewardEligible, item.Category }
        });

        await _db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>ไม่ลบเมนูจริง เพราะออเดอร์เก่ายังอ้างถึง ใช้ปิดการขายแทน</summary>
    public async Task SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct)
                   ?? throw new DomainException("ไม่พบเมนูนี้");

        item.IsActive = isActive;
        _audit.Add(isActive ? "menu.enable" : "menu.disable", nameof(MenuItem), item.Id.ToString(),
            new { item.Name });
        await _db.SaveChangesAsync(ct);
    }

    private static void Validate(MenuItemInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new DomainException("กรุณากรอกชื่อเมนู");
        if (input.Name.Trim().Length > 100)
            throw new DomainException("ชื่อเมนูยาวเกินไป");
        if (input.Price < 0)
            throw new DomainException("ราคาต้องไม่เป็นค่าลบ");
        if (input.Price > 99_999_999m)
            throw new DomainException("ราคาสูงเกินกว่าที่ระบบรองรับ");
    }
}
