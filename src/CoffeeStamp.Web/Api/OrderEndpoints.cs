using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

public record VoidOrderRequest(string Reason, string ManagerPin);

public record CancelOrderRequest(string? Reason);

/// <summary>body ของการออกแต้ม: token ที่อ่านได้จาก QR บนจอลูกค้า</summary>
public record StampRequest(string QrToken, string? ManagerPin);

/// <summary>ออกแต้มโดยพนักงานเลือกลูกค้าจากรายชื่อ ใช้ช่วงที่ยังไม่เปิด LIFF</summary>
public record StampByCustomerRequest(int CustomerId, string? ManagerPin);

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/menu", async (MenuService menu, CancellationToken ct) =>
        {
            var items = await menu.GetActiveAsync(ct);
            return Results.Ok(items.Select(m => new
            {
                m.Id,
                m.Name,
                m.Category,
                m.Price,
                m.EarnsStamp
            }));
        });

        var group = app.MapGroup("/orders");

        group.MapGet("/", async (string? date, OrderService orders, CancellationToken ct) =>
        {
            var businessDate = ParseDate(date);
            var list = await orders.GetForDateAsync(businessDate, ct);
            return Results.Ok(list.Select(ToDto));
        });

        group.MapGet("/{id:long}", async (long id, OrderService orders, CancellationToken ct) =>
        {
            var order = await orders.GetAsync(id, ct);
            return order is null
                ? Results.NotFound(new { error = "ไม่พบออเดอร์" })
                : Results.Ok(ToDto(order));
        });

        group.MapPost("/", async (CreateOrderRequest request, OrderService orders,
            CancellationToken ct) =>
        {
            var order = await orders.CreateAsync(request, ct);
            return Results.Created($"/api/orders/{order.Id}", ToDto(order));
        });

        group.MapPost("/{id:long}/confirm-payment", async (long id, OrderService orders,
            CancellationToken ct) =>
        {
            var order = await orders.ConfirmPaymentAsync(id, ct);
            return Results.Ok(ToDto(order));
        });

        group.MapPost("/{id:long}/cancel", async (long id, CancelOrderRequest? request,
            OrderService orders, CancellationToken ct) =>
        {
            var order = await orders.CancelPendingAsync(id, request?.Reason, ct);
            return Results.Ok(ToDto(order));
        });

        group.MapPost("/{id:long}/void", async (long id, VoidOrderRequest request,
            OrderService orders, CancellationToken ct) =>
        {
            var order = await orders.VoidAsync(id, request.Reason, request.ManagerPin, ct);
            return Results.Ok(ToDto(order));
        });

        // ออกแต้มให้ลูกค้าจากออเดอร์นี้ ร้านเป็นฝ่ายสแกน QR ของลูกค้าเท่านั้น
        group.MapPost("/{id:long}/stamps", async (long id, StampRequest request,
            StampService stamps, LinePushService push, SettingsService settings,
            CancellationToken ct) =>
        {
            var result = await stamps.EarnForOrderAsync(id, request.QrToken, request.ManagerPin, ct);
            var pushed = await NotifyAsync(result, push, settings, ct);

            return Results.Ok(ToEarnDto(result, pushed));
        }).RequireRateLimiting("scan");

        // ออกแต้มโดยเลือกลูกค้าจากรายชื่อ (ช่วงคีย์มือ ลูกค้ายังไม่มี QR ของตัวเอง)
        // กฎทุกข้อเหมือนทาง QR: ต้องรับเงินแล้ว วันทำการเดียวกัน ยังไม่ปิดยอด และออเดอร์ละครั้งเดียว
        group.MapPost("/{id:long}/stamps-by-customer", async (long id,
            StampByCustomerRequest request, StampService stamps, LinePushService push,
            SettingsService settings, CancellationToken ct) =>
        {
            var result = await stamps.EarnForOrderByCustomerAsync(id, request.CustomerId, request.ManagerPin, ct);
            var pushed = await NotifyAsync(result, push, settings, ct);

            return Results.Ok(ToEarnDto(result, pushed));
        });

        // QR พร้อมเพย์ของออเดอร์ ระบุยอดไว้ในตัว QR แล้ว ลูกค้าแก้ยอดไม่ได้
        group.MapGet("/{id:long}/promptpay-qr", async (long id, string? format,
            OrderService orders, PromptPayQrService qr, CancellationToken ct) =>
        {
            var order = await orders.GetAsync(id, ct);
            if (order is null)
                return Results.NotFound(new { error = "ไม่พบออเดอร์" });

            if (order.Status is not (OrderStatus.Pending or OrderStatus.Paid))
                return Results.BadRequest(new { error = "ออเดอร์นี้ถูกยกเลิกแล้ว" });

            var payload = await qr.BuildPayloadForAmountAsync(order.Total, ct);

            if (string.Equals(format, "payload", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { payload, order.Total, order.OrderNo });

            var png = PromptPayQrService.RenderPng(payload);
            return Results.File(png, "image/png");
        });
    }

    /// <summary>
    /// push แจ้งลูกค้าหลังบันทึกแต้มแล้ว ถ้าส่งไม่สำเร็จก็ไม่กระทบแต้มที่บันทึกไว้
    /// สมาชิกที่สมัครหน้าร้านยังไม่มี LINE จึงข้ามการแจ้งเตือนไป
    /// </summary>
    private static async Task<bool> NotifyAsync(StampEarnResult result, LinePushService push,
        SettingsService settings, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(result.LineUserId)) return false;

        var shop = await settings.GetAsync(ct);
        return await push.SendTextAsync(
            result.LineUserId,
            LinePushService.BuildEarnMessage(
                result.Earned, result.Balance, result.StampsPerReward, shop.ShopName),
            ct);
    }

    private static object ToEarnDto(StampEarnResult result, bool pushed) => new
    {
        earned = result.Earned,
        balance = result.Balance,
        stampsPerReward = result.StampsPerReward,
        customerName = result.CustomerName,
        orderNo = result.OrderNo,
        expiresAt = result.ExpiresAt is null
            ? null
            : BangkokTime.FormatDate(result.ExpiresAt.Value),
        notified = pushed
    };

    internal static DateOnly ParseDate(string? date) =>
        DateOnly.TryParse(date, out var parsed) ? parsed : BangkokTime.Today();

    private static object ToDto(Order order) => new
    {
        order.Id,
        order.OrderNo,
        BusinessDate = order.BusinessDate.ToString("yyyy-MM-dd"),
        order.SubTotal,
        order.Total,
        PaymentMethod = order.PaymentMethod.ToString(),
        Status = order.Status.ToString(),
        order.Note,
        CreatedAt = order.CreatedAt,
        CreatedAtLocal = BangkokTime.FormatTime(order.CreatedAt),
        StaffName = order.Staff?.Name,
        order.VoidReason,
        order.EligibleStampCount,
        Items = order.Items.Select(i => new
        {
            i.Id,
            i.NameSnapshot,
            i.UnitPrice,
            i.Quantity,
            i.LineTotal,
            i.IsReward
        })
    };
}
