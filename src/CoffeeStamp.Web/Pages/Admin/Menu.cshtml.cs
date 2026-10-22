using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MenuItemEntity = CoffeeStamp.Web.Domain.MenuItem;

namespace CoffeeStamp.Web.Pages.Admin;

public class MenuModel : PageModel
{
    private readonly MenuService _menu;

    public MenuModel(MenuService menu) => _menu = menu;

    public class NewMenuItem
    {
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public decimal Price { get; set; }
        public bool EarnsStamp { get; set; } = true;
        public bool IsRewardEligible { get; set; } = true;
        public int SortOrder { get; set; }
    }

    [BindProperty]
    public NewMenuItem NewItem { get; set; } = new();

    public List<MenuItemEntity> Items { get; private set; } = [];
    public List<string> Categories { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        try
        {
            await _menu.CreateAsync(new MenuItemInput(
                NewItem.Name,
                NewItem.Category,
                NewItem.Price,
                NewItem.EarnsStamp,
                NewItem.IsRewardEligible,
                NewItem.SortOrder), ct);

            Message = $"เพิ่มเมนู {NewItem.Name} แล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, string? category,
        decimal price, bool earnsStamp, bool isRewardEligible, int sortOrder, CancellationToken ct)
    {
        try
        {
            await _menu.UpdateAsync(id,
                new MenuItemInput(name, category, price, earnsStamp, isRewardEligible, sortOrder), ct);
            Message = $"บันทึกเมนู {name} แล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int id, bool isActive, CancellationToken ct)
    {
        try
        {
            await _menu.SetActiveAsync(id, isActive, ct);
            Message = isActive ? "เปิดขายเมนูแล้ว" : "ปิดการขายเมนูแล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            Items = await _menu.GetAllAsync(ct);
            Categories = Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Category))
                .Select(i => i.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }
        catch (Exception)
        {
            Error ??= "โหลดรายการเมนูไม่สำเร็จ";
        }
    }
}
