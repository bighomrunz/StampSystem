using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

public record ResolveCustomerRequest(string QrToken);

public record UpdateCustomerRequest(string Name, string? Phone);

public record PaperCardRequest(int Stamps, string OwnerPin, string? Note);

public record LinkLineRequest(string QrToken);

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers");

        // อ่าน QR ลูกค้าเพื่อดูยอดแต้มก่อนออกแต้มจริง
        // ไม่ใช้ step ของ token ทิ้ง พนักงานจึงสแกนดูได้โดยไม่ทำให้ QR ใช้ไม่ได้
        group.MapPost("/resolve", async (ResolveCustomerRequest request, StampService stamps,
            CancellationToken ct) =>
        {
            var summary = await stamps.ResolveByQrAsync(request.QrToken, ct);
            return Results.Ok(ToSummaryDto(summary));
        }).RequireRateLimiting("scan");

        // --- โหมดคีย์มือ: พนักงานค้นหาและสร้างสมาชิกเองที่เคาน์เตอร์ ---

        group.MapGet("/", async (string? q, CustomerAdminService customers,
            CancellationToken ct) =>
        {
            var results = await customers.SearchAsync(q, ct);
            return Results.Ok(results.Select(c => new
            {
                c.Id,
                c.Name,
                Phone = c.Phone,
                PhoneDisplay = PhoneNumber.Format(c.Phone),
                c.Balance,
                c.IsLineLinked,
                c.HasMigratedCard
            }));
        });

        group.MapPost("/", async (CreateCustomerRequest request, CustomerAdminService customers,
            CancellationToken ct) =>
        {
            var customer = await customers.CreateAsync(request, ct);
            return Results.Created($"/api/customers/{customer.Id}", new
            {
                customer.Id,
                Name = customer.DisplayLabel,
                customer.Phone
            });
        });

        group.MapGet("/{id:int}", async (int id, CustomerAdminService customers,
            StampService stamps, CancellationToken ct) =>
        {
            var customer = await customers.RequireAsync(id, ct);
            var summary = await stamps.GetSummaryAsync(id, ct);
            var history = await stamps.GetHistoryAsync(id, 30, ct);

            return Results.Ok(new
            {
                customer.Id,
                Name = customer.DisplayLabel,
                customer.Phone,
                PhoneDisplay = PhoneNumber.Format(customer.Phone),
                Source = customer.Source.ToString(),
                IsLineLinked = customer.IsLineLinked,
                HasMigratedCard = await customers.HasMigratedCardAsync(id, ct),
                ConsentSource = customer.ConsentSource?.ToString(),
                ConsentAt = customer.ConsentAt is null
                    ? null
                    : BangkokTime.FormatDateTime(customer.ConsentAt.Value),
                Summary = ToSummaryDto(summary),
                History = history.Select(h => new
                {
                    CreatedAt = BangkokTime.FormatDateTime(h.CreatedAt),
                    Type = h.Type.ToString(),
                    h.Quantity,
                    h.OrderNo,
                    ExpiresAt = h.ExpiresAt is null ? null : BangkokTime.FormatDate(h.ExpiresAt.Value),
                    h.Note
                })
            });
        });

        group.MapPut("/{id:int}", async (int id, UpdateCustomerRequest request,
            CustomerAdminService customers, CancellationToken ct) =>
        {
            await customers.UpdateAsync(id, request.Name, request.Phone, ct);
            return Results.Ok(new { ok = true });
        });

        // ย้ายแต้มจากบัตรกระดาษ ต้องใช้ PIN ของเจ้าของร้าน และทำได้ครั้งเดียวต่อคน
        group.MapPost("/{id:int}/paper-card", async (int id, PaperCardRequest request,
            StampService stamps, CancellationToken ct) =>
        {
            var result = await stamps.MigratePaperCardAsync(
                id, request.Stamps, request.OwnerPin, request.Note, ct);

            return Results.Ok(new
            {
                migrated = result.Earned,
                balance = result.Balance,
                stampsPerReward = result.StampsPerReward,
                expiresAt = result.ExpiresAt is null
                    ? null
                    : BangkokTime.FormatDate(result.ExpiresAt.Value)
            });
        }).RequireRateLimiting("correct");

        // ผูกบัญชี LINE เข้ากับระเบียนที่คีย์มือไว้ ทำที่เคาน์เตอร์โดยสแกน QR ของลูกค้า
        group.MapPost("/{id:int}/link-line", async (int id, LinkLineRequest request,
            CustomerAdminService customers, StampService stamps, QrSecretProvider qrSecret,
            CancellationToken ct) =>
        {
            var payload = CustomerQrToken.Verify(qrSecret.Secret, request.QrToken, DateTime.UtcNow);
            var customer = await customers.LinkLineAccountAsync(id, payload.PublicId, ct);

            return Results.Ok(new
            {
                ok = true,
                customerId = customer.Id,
                name = customer.DisplayLabel,
                isLineLinked = customer.IsLineLinked
            });
        }).RequireRateLimiting("scan");
    }

    private static object ToSummaryDto(CustomerStampSummary summary) => new
    {
        customerId = summary.CustomerId,
        displayName = summary.DisplayName,
        balance = summary.Balance,
        stampsPerReward = summary.StampsPerReward,
        rewardsAvailable = summary.RewardsAvailable,
        stampsToNextReward = summary.StampsToNextReward,
        expiring = summary.Expiring.Select(e => new
        {
            expiresOn = e.ExpiresOn.ToString("dd/MM/yyyy"),
            stamps = e.Stamps
        })
    };
}
