using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

/// <summary>สแตมป์หนึ่งครั้ง: ขายเร็ว + ออกแต้มให้ลูกค้าในคำขอเดียว</summary>
public record QuickStampRequest(
    int? CustomerId,
    string? QrToken,
    int Cups,
    PaymentMethod PaymentMethod,
    decimal? Amount,
    string? Note,
    string? ManagerPin);

/// <summary>แก้แต้มที่สแตมป์ผิด ลด = พนักงานทำได้เอง เพิ่ม = ต้อง PIN ผู้จัดการ</summary>
public record CorrectStampRequest(int Delta, string Reason, string? ManagerPin);

/// <summary>สแตมป์ผิดคน ย้ายแต้มของออเดอร์ไปให้ลูกค้าที่ถูกต้อง</summary>
public record TransferStampRequest(int ToCustomerId, string Reason, string? ManagerPin);

public static class StampEndpoints
{
    public static void MapStampEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stamp");

        // ทางหลักของช่วงที่เปิดใช้แค่สแตมป์: หนึ่งคำขอได้ทั้งออเดอร์และแต้ม
        group.MapPost("/", async (QuickStampRequest request, OrderService orders,
            StampService stamps, LinePushService push, SettingsService settings,
            CancellationToken ct) =>
        {
            if (request.CustomerId is null && string.IsNullOrWhiteSpace(request.QrToken))
                throw new DomainException("ต้องเลือกลูกค้าหรือสแกน QR ก่อน");

            var order = await orders.CreateQuickSaleAsync(
                new QuickSaleRequest(request.Cups, request.PaymentMethod, request.Amount,
                    request.Note), ct);

            var result = request.CustomerId is { } customerId
                ? await stamps.EarnForOrderByCustomerAsync(order.Id, customerId, request.ManagerPin, ct)
                : await stamps.EarnForOrderAsync(order.Id, request.QrToken, request.ManagerPin, ct);

            var shop = await settings.GetAsync(ct);
            var pushed = !string.IsNullOrEmpty(result.LineUserId) && await push.SendTextAsync(
                result.LineUserId,
                LinePushService.BuildEarnMessage(
                    result.Earned, result.Balance, result.StampsPerReward, shop.ShopName),
                ct);

            return Results.Ok(new
            {
                orderId = order.Id,
                orderNo = order.OrderNo,
                earned = result.Earned,
                balance = result.Balance,
                stampsPerReward = result.StampsPerReward,
                rewardsAvailable = result.StampsPerReward > 0
                    ? result.Balance / result.StampsPerReward
                    : 0,
                customerId = result.CustomerId,
                customerName = result.CustomerName,
                expiresAt = result.ExpiresAt is null
                    ? null
                    : BangkokTime.FormatDate(result.ExpiresAt.Value),
                notified = pushed
            });
        }).RequireRateLimiting("scan");

        // แก้แต้มโดยไม่ต้องออกบัตรใหม่ ledger เก็บทั้งรายการเดิมและรายการแก้
        group.MapPost("/correct/{customerId:int}", async (int customerId,
            CorrectStampRequest request, StampService stamps, CancellationToken ct) =>
        {
            var result = await stamps.CorrectAsync(
                customerId, request.Delta, request.Reason, request.ManagerPin, ct);

            return Results.Ok(new
            {
                delta = result.Earned,
                balance = result.Balance,
                stampsPerReward = result.StampsPerReward,
                customerName = result.CustomerName
            });
        }).RequireRateLimiting("correct");

        group.MapPost("/transfer/{orderId:long}", async (long orderId,
            TransferStampRequest request, StampService stamps, CancellationToken ct) =>
        {
            var result = await stamps.TransferOrderStampsAsync(
                orderId, request.ToCustomerId, request.Reason, request.ManagerPin, ct);

            return Results.Ok(new
            {
                moved = result.Earned,
                orderNo = result.OrderNo,
                toCustomerId = result.CustomerId,
                toCustomerName = result.CustomerName,
                balance = result.Balance
            });
        }).RequireRateLimiting("correct");
    }
}
