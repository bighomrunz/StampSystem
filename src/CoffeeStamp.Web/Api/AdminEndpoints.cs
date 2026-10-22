using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

public record ResetPinRequest(string NewPin);

public record UpdateStaffRequest(string Name, StaffRole Role);

public record CreateDeviceRequest(string Name);

public record SetActiveRequest(bool IsActive);

public static class AdminEndpoints
{
    public static void MapDailyCloseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/daily-close");

        group.MapGet("/{date}", async (string date, DailyCloseService service, CancellationToken ct) =>
        {
            var businessDate = OrderEndpoints.ParseDate(date);
            var summary = await service.GetSummaryAsync(businessDate, ct);

            return Results.Ok(new
            {
                BusinessDate = summary.BusinessDate.ToString("yyyy-MM-dd"),
                summary.ExpectedCash,
                summary.ExpectedPromptPay,
                summary.ExpectedPaoTang,
                summary.ExpectedTotal,
                summary.PaidOrderCount,
                summary.VoidedOrderCount,
                summary.PendingOrderCount,
                summary.VoidedAmount,
                summary.IsClosed,
                Close = summary.Close is null
                    ? null
                    : new
                    {
                        summary.Close.ActualCash,
                        summary.Close.ActualPromptPay,
                        summary.Close.ActualPaoTang,
                        summary.Close.VarianceCash,
                        summary.Close.VariancePromptPay,
                        summary.Close.VariancePaoTang,
                        summary.Close.VarianceTotal,
                        summary.Close.Note,
                        ClosedBy = summary.Close.ClosedByStaff?.Name,
                        ClosedAt = BangkokTime.FormatDateTime(summary.Close.ClosedAt)
                    }
            });
        });

        group.MapPost("/{date}", async (string date, DailyCloseRequest request,
            DailyCloseService service, CancellationToken ct) =>
        {
            var businessDate = OrderEndpoints.ParseDate(date);
            var close = await service.CloseAsync(businessDate, request, ct);

            return Results.Ok(new
            {
                BusinessDate = close.BusinessDate.ToString("yyyy-MM-dd"),
                close.VarianceCash,
                close.VariancePromptPay,
                close.VariancePaoTang,
                close.VarianceTotal,
                ClosedAt = BangkokTime.FormatDateTime(close.ClosedAt)
            });
        }).RequireAuthorization("Manager");
    }

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").RequireAuthorization("Manager");

        // --- ตั้งค่าร้าน ---
        group.MapGet("/settings", async (SettingsService settings, CancellationToken ct) =>
        {
            var s = await settings.GetAsync(ct);
            return Results.Ok(new
            {
                s.ShopName,
                s.PromptPayId,
                PromptPayIdType = s.PromptPayIdType.ToString(),
                s.PromptPayMerchantName,
                s.StampsPerReward,
                s.StampExpiryMonths,
                RewardMode = s.RewardMode.ToString(),
                s.RewardPriceCap,
                s.AllowRewardTopUp,
                s.MaxStampsPerOrder,
                s.MaxStampsPerDayPerCustomer,
                OpenTime = s.OpenTime.ToString("HH:mm"),
                CloseTime = s.CloseTime.ToString("HH:mm"),
                s.VarianceAlertThreshold,
                s.StaffIdleTimeoutMinutes
            });
        });

        group.MapPut("/settings", async (SettingsInput input, SettingsService settings,
            CancellationToken ct) =>
        {
            await settings.UpdateAsync(input, ct);
            return Results.Ok(new { ok = true });
        });

        // --- เมนู ---
        group.MapGet("/menu", async (MenuService menu, CancellationToken ct) =>
        {
            var items = await menu.GetAllAsync(ct);
            return Results.Ok(items.Select(m => new
            {
                m.Id,
                m.Name,
                m.Category,
                m.Price,
                m.EarnsStamp,
                m.IsRewardEligible,
                m.IsActive,
                m.SortOrder
            }));
        });

        group.MapPost("/menu", async (MenuItemInput input, MenuService menu, CancellationToken ct) =>
        {
            var item = await menu.CreateAsync(input, ct);
            return Results.Created($"/api/admin/menu/{item.Id}", new { item.Id });
        });

        group.MapPut("/menu/{id:int}", async (int id, MenuItemInput input, MenuService menu,
            CancellationToken ct) =>
        {
            await menu.UpdateAsync(id, input, ct);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/menu/{id:int}/active", async (int id, SetActiveRequest request,
            MenuService menu, CancellationToken ct) =>
        {
            await menu.SetActiveAsync(id, request.IsActive, ct);
            return Results.Ok(new { ok = true });
        });

        // --- พนักงาน ---
        group.MapGet("/staff", async (StaffAdminService admin, CancellationToken ct) =>
        {
            var list = await admin.GetAllAsync(ct);
            return Results.Ok(list.Select(s => new
            {
                s.Id,
                s.Name,
                Role = s.Role.ToString(),
                s.IsActive,
                s.MustChangePin,
                IsLocked = s.LockedUntil > DateTime.UtcNow,
                LastLoginAt = s.LastLoginAt is null ? null : BangkokTime.FormatDateTime(s.LastLoginAt.Value)
            }));
        });

        group.MapPost("/staff", async (StaffInput input, StaffAdminService admin,
            CancellationToken ct) =>
        {
            var staff = await admin.CreateAsync(input, ct);
            return Results.Created($"/api/admin/staff/{staff.Id}", new { staff.Id });
        });

        group.MapPut("/staff/{id:int}", async (int id, UpdateStaffRequest request,
            StaffAdminService admin, CancellationToken ct) =>
        {
            await admin.UpdateAsync(id, request.Name, request.Role, ct);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/staff/{id:int}/reset-pin", async (int id, ResetPinRequest request,
            StaffAdminService admin, CancellationToken ct) =>
        {
            await admin.ResetPinAsync(id, request.NewPin, ct);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/staff/{id:int}/active", async (int id, SetActiveRequest request,
            StaffAdminService admin, CancellationToken ct) =>
        {
            await admin.SetActiveAsync(id, request.IsActive, ct);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/staff/{id:int}/unlock", async (int id, StaffAdminService admin,
            CancellationToken ct) =>
        {
            await admin.UnlockAsync(id, ct);
            return Results.Ok(new { ok = true });
        });

        // --- อุปกรณ์ ---
        group.MapGet("/devices", async (DeviceService devices, CancellationToken ct) =>
        {
            var list = await devices.GetAllAsync(ct);
            return Results.Ok(list.Select(d => new
            {
                d.Id,
                d.Name,
                d.IsActive,
                d.RegistrationCode,
                d.IsRegistered,
                RegisteredAt = d.RegisteredAt is null
                    ? null
                    : BangkokTime.FormatDateTime(d.RegisteredAt.Value),
                LastSeenAt = d.LastSeenAt is null ? null : BangkokTime.FormatDateTime(d.LastSeenAt.Value)
            }));
        });

        group.MapPost("/devices", async (CreateDeviceRequest request, DeviceService devices,
            CancellationToken ct) =>
        {
            var device = await devices.CreateAsync(request.Name, ct);
            return Results.Created($"/api/admin/devices/{device.Id}",
                new { device.Id, device.Name, device.RegistrationCode });
        });

        group.MapPost("/devices/{id:guid}/active", async (Guid id, SetActiveRequest request,
            DeviceService devices, CancellationToken ct) =>
        {
            await devices.SetActiveAsync(id, request.IsActive, ct);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/devices/{id:guid}/reissue-code", async (Guid id, DeviceService devices,
            CancellationToken ct) =>
        {
            var code = await devices.ReissueCodeAsync(id, ct);
            return Results.Ok(new { code });
        });
    }
}
