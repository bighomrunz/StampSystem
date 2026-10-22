using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DeviceEntity = CoffeeStamp.Web.Domain.Device;

namespace CoffeeStamp.Web.Pages.Admin;

public class DevicesModel : PageModel
{
    private readonly DeviceService _devices;

    public DevicesModel(DeviceService devices) => _devices = devices;

    public List<DeviceEntity> Devices { get; private set; } = [];
    public bool IsBootstrap { get; private set; }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(string name, CancellationToken ct)
    {
        try
        {
            var device = await _devices.CreateAsync(name, ct);
            Message = $"สร้างอุปกรณ์ {device.Name} แล้ว รหัสลงทะเบียนคือ {device.RegistrationCode}";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        try
        {
            await _devices.SetActiveAsync(id, isActive, ct);
            Message = isActive ? "เปิดใช้งานอุปกรณ์แล้ว" : "ปิดใช้งานอุปกรณ์แล้ว";
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReissueAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var code = await _devices.ReissueCodeAsync(id, ct);
            Message = $"รหัสลงทะเบียนใหม่คือ {code}";
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
            Devices = await _devices.GetAllAsync(ct);
            IsBootstrap = await _devices.IsBootstrapAsync(ct);
        }
        catch (Exception)
        {
            Error ??= "โหลดรายการอุปกรณ์ไม่สำเร็จ";
        }
    }
}
