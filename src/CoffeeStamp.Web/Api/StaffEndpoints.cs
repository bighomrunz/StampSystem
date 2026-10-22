using System.Security.Claims;
using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CoffeeStamp.Web.Api;

public record LoginRequest(int StaffId, string Pin);

public record ChangePinRequest(string CurrentPin, string NewPin);

public static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/staff");

        // หน้าล็อกอินต้องเห็นรายชื่อก่อน จึงเปิดให้เรียกได้โดยไม่ต้องล็อกอิน
        // แต่ยังต้องผ่านการตรวจอุปกรณ์ที่ลงทะเบียน และคืนแค่ชื่อกับ id
        group.MapGet("/list", async (StaffAuthService auth, DeviceService devices,
            HttpContext http, CancellationToken ct) =>
        {
            if (!await IsDeviceAllowedAsync(http, devices, ct))
                return Results.BadRequest(new { error = "อุปกรณ์นี้ยังไม่ได้ลงทะเบียน" });

            var staff = await auth.GetActiveStaffAsync(ct);
            return Results.Ok(staff.Select(s => new
            {
                s.Id,
                s.Name,
                Role = s.Role.ToString()
            }));
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, StaffAuthService auth,
            DeviceService devices, HttpContext http, CancellationToken ct) =>
        {
            var device = await ResolveDeviceAsync(http, devices, ct);
            if (device is null && !await devices.IsBootstrapAsync(ct))
                return Results.BadRequest(new { error = "อุปกรณ์นี้ยังไม่ได้ลงทะเบียน" });

            var staff = await auth.LoginAsync(request.StaffId, request.Pin, ct);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, staff.Id.ToString()),
                new(ClaimTypes.Name, staff.Name),
                new(ClaimTypes.Role, staff.Role.ToString()),
                new(StaffContext.MustChangePinClaim, staff.MustChangePin ? "true" : "false")
            };

            if (device is not null)
                claims.Add(new Claim(StaffContext.DeviceClaim, device.Id.ToString()));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            if (device is not null)
                await devices.TouchAsync(device.Id, ct);

            return Results.Ok(new
            {
                staff.Id,
                staff.Name,
                Role = staff.Role.ToString(),
                staff.MustChangePin,
                redirectTo = staff.MustChangePin ? "/Staff/ChangePin" : "/"
            });
        }).AllowAnonymous().RequireRateLimiting("login");

        group.MapPost("/logout", async (HttpContext http, AuditService audit,
            StaffContext staff, CancellationToken ct) =>
        {
            if (staff.StaffId is not null)
                await audit.LogAsync("staff.logout", nameof(Staff), staff.StaffId.ToString(), ct: ct);

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { redirectTo = "/Staff/Login" });
        }).AllowAnonymous();

        group.MapPost("/change-pin", async (ChangePinRequest request, StaffAuthService auth,
            StaffContext staff, HttpContext http, CancellationToken ct) =>
        {
            var staffId = staff.RequireStaffId();
            await auth.ChangePinAsync(staffId, request.CurrentPin, request.NewPin, ct);

            // เปลี่ยน PIN แล้วต้องล็อกอินใหม่ เพื่อให้ claim must_change_pin อัปเดต
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { redirectTo = "/Staff/Login" });
        }).RequireRateLimiting("login");

        group.MapGet("/me", (StaffContext staff) => Results.Ok(new
        {
            staff.StaffId,
            staff.Name,
            Role = staff.Role?.ToString(),
            staff.DeviceId,
            staff.MustChangePin
        }));
    }

    private static async Task<Device?> ResolveDeviceAsync(HttpContext http, DeviceService devices,
        CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(DeviceService.CookieName, out var raw) ||
            !Guid.TryParse(raw, out var deviceId))
        {
            return null;
        }

        return await devices.GetActiveAsync(deviceId, ct);
    }

    private static async Task<bool> IsDeviceAllowedAsync(HttpContext http, DeviceService devices,
        CancellationToken ct) =>
        await ResolveDeviceAsync(http, devices, ct) is not null || await devices.IsBootstrapAsync(ct);
}
