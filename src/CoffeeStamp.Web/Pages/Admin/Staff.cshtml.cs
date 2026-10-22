using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffEntity = CoffeeStamp.Web.Domain.Staff;

namespace CoffeeStamp.Web.Pages.Admin;

public class StaffModel : PageModel
{
    private readonly StaffAdminService _admin;
    private readonly StaffContext _current;

    public StaffModel(StaffAdminService admin, StaffContext current)
    {
        _admin = admin;
        _current = current;
    }

    public class NewStaffForm
    {
        public string Name { get; set; } = "";
        public StaffRole Role { get; set; } = StaffRole.Staff;
        public string? Pin { get; set; }
    }

    [BindProperty]
    public NewStaffForm NewStaff { get; set; } = new();

    public List<StaffEntity> StaffList { get; private set; } = [];
    public bool CanManageOwners => _current.Role == StaffRole.Owner;

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        try
        {
            await _admin.CreateAsync(new StaffInput(NewStaff.Name, NewStaff.Role, NewStaff.Pin), ct);
            Message = $"เพิ่มพนักงาน {NewStaff.Name} แล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, StaffRole role,
        CancellationToken ct)
    {
        try
        {
            await _admin.UpdateAsync(id, name, role, ct);
            Message = "บันทึกข้อมูลพนักงานแล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPinAsync(int id, string newPin, CancellationToken ct)
    {
        try
        {
            await _admin.ResetPinAsync(id, newPin, ct);
            Message = "รีเซ็ต PIN แล้ว พนักงานต้องเปลี่ยน PIN เองเมื่อล็อกอินครั้งถัดไป";
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
            await _admin.SetActiveAsync(id, isActive, ct);
            Message = isActive ? "เปิดใช้งานบัญชีแล้ว" : "ปิดใช้งานบัญชีแล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnlockAsync(int id, CancellationToken ct)
    {
        try
        {
            await _admin.UnlockAsync(id, ct);
            Message = "ปลดล็อกบัญชีแล้ว";
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
            StaffList = await _admin.GetAllAsync(ct);
        }
        catch (Exception)
        {
            Error ??= "โหลดรายชื่อพนักงานไม่สำเร็จ";
        }
    }
}
