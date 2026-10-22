using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

public record RedeemRequest(int CustomerId, int MenuItemId, PaymentMethod TopUpPaymentMethod);

public record PaperCardRedeemRequest(
    int MenuItemId,
    string Reason,
    string ManagerPin,
    PaymentMethod TopUpPaymentMethod);

public static class RewardEndpoints
{
    public static void MapRewardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rewards");

        // เมนูที่แลกได้ตามโหมดใน ShopSettings พร้อมบอกว่าต้องจ่ายเพิ่มเท่าไร
        group.MapGet("/options", async (RewardService rewards, SettingsService settings,
            CancellationToken ct) =>
        {
            var shop = await settings.GetAsync(ct);
            var options = await rewards.GetOptionsAsync(ct);

            return Results.Ok(new
            {
                mode = shop.RewardMode.ToString(),
                stampsPerReward = shop.StampsPerReward,
                priceCap = shop.RewardPriceCap,
                allowTopUp = shop.AllowRewardTopUp,
                items = options.Select(o => new
                {
                    menuItemId = o.MenuItemId,
                    name = o.Name,
                    category = o.Category,
                    price = o.Price,
                    canRedeem = o.CanRedeem,
                    topUpAmount = o.TopUpAmount,
                    reason = o.Reason
                })
            });
        });

        // สิทธิ์ของลูกค้าคนหนึ่ง: แลกได้กี่แก้ว และแต้มที่ใช้ได้จริงเหลือเท่าไร
        group.MapGet("/eligibility/{customerId:int}", async (int customerId,
            StampService stamps, SettingsService settings, CancellationToken ct) =>
        {
            var shop = await settings.GetAsync(ct);
            var usable = await stamps.GetUsableStampsAsync(customerId, ct);
            var perReward = Math.Max(1, shop.StampsPerReward);

            return Results.Ok(new
            {
                usableStamps = usable,
                stampsPerReward = shop.StampsPerReward,
                rewardsAvailable = usable / perReward,
                stampsToNextReward = usable % perReward == 0 && usable > 0
                    ? 0
                    : perReward - usable % perReward
            });
        });

        group.MapPost("/redeem", async (RedeemRequest request, RewardService rewards,
            LinePushService push, SettingsService settings, CancellationToken ct) =>
        {
            var result = await rewards.RedeemAsync(
                request.CustomerId, request.MenuItemId, request.TopUpPaymentMethod, ct);

            var pushed = false;
            if (!string.IsNullOrEmpty(result.LineUserId))
            {
                var shop = await settings.GetAsync(ct);
                var message = $"{shop.ShopName}\n" +
                              $"แลก {result.MenuName} เรียบร้อย ใช้ {result.StampsUsed} แต้ม\n" +
                              $"แต้มคงเหลือ {result.Balance}";
                pushed = await push.SendTextAsync(result.LineUserId, message, ct);
            }

            return Results.Ok(new
            {
                orderNo = result.OrderNo,
                menuName = result.MenuName,
                stampsUsed = result.StampsUsed,
                balance = result.Balance,
                rewardsAvailable = result.RewardsAvailable,
                topUpAmount = result.TopUpAmount,
                customerName = result.CustomerName,
                notified = pushed
            });
        }).RequireRateLimiting("scan");

        // ช่วง dual-run: รับบัตรกระดาษเดิมมาแลก ไม่ตัดแต้มเพราะไม่มีแต้มในระบบ
        group.MapPost("/redeem-paper-card", async (PaperCardRedeemRequest request,
            RewardService rewards, CancellationToken ct) =>
        {
            var result = await rewards.RedeemWithPaperCardAsync(
                request.MenuItemId, request.Reason, request.ManagerPin,
                request.TopUpPaymentMethod, ct);

            return Results.Ok(new
            {
                orderNo = result.OrderNo,
                menuName = result.MenuName,
                topUpAmount = result.TopUpAmount
            });
        }).RequireRateLimiting("correct");

        // ปุ่มสั่งตัดแต้มหมดอายุเอง เผื่อ job รายวันไม่ได้ทำงาน (เช่นเครื่องปิดข้ามคืน)
        group.MapPost("/expire-now", async (StampService stamps, CancellationToken ct) =>
        {
            var expired = await stamps.ExpireDueStampsAsync(ct);
            return Results.Ok(new { expired });
        }).RequireAuthorization("Manager").RequireRateLimiting("correct");
    }
}
