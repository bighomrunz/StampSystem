using System.Security.Claims;
using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CoffeeStamp.Web.Api;

public record LiffAuthRequest(string? IdToken, string? DevUserId, string? DevDisplayName);

public record ConsentRequest(string Version);

public static class LiffEndpoints
{
    /// <summary>
    /// รับ group ที่ Program สร้างไว้ให้แล้ว (prefix /api/liff + policy "Customer")
    /// ต้องแยก group จาก API ฝั่งพนักงาน เพราะ policy ของ group เป็นแบบบวกกัน
    /// ถ้าอยู่ group เดียวกัน ลูกค้าจะติด policy "Staff" ไปด้วย
    /// </summary>
    public static void MapLiffEndpoints(this IEndpointRouteBuilder group)
    {
        // antiforgery token ผูกกับตัวตนของผู้เรียก พอลูกค้าล็อกอินเสร็จ token ที่ออกตอน
        // ยังไม่ล็อกอินจะใช้ไม่ได้ หน้าบัตรจึงต้องขอ token ใหม่หลังล็อกอิน
        group.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext http) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken }))
            .AllowAnonymous();

        // ล็อกอินยังไม่มี cookie จึงต้องเปิดให้เรียกได้ก่อน
        group.MapPost("/auth", async (LiffAuthRequest request, CustomerAuthService auth,
            StampService stamps, HttpContext http, CancellationToken ct) =>
        {
            var customer = string.IsNullOrWhiteSpace(request.DevUserId)
                ? await auth.AuthenticateAsync(request.IdToken, ct)
                : await auth.AuthenticateDevAsync(request.DevUserId, request.DevDisplayName, ct);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, customer.Id.ToString()),
                new(CustomerContext.PublicIdClaim, customer.PublicId.ToString())
            };

            var identity = new ClaimsIdentity(claims, CustomerContext.AuthenticationScheme);
            await http.SignInAsync(CustomerContext.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            var needsConsent = !customer.HasConsented(auth.ConsentVersion);

            return Results.Ok(new
            {
                needsConsent,
                consentVersion = auth.ConsentVersion,
                displayName = customer.DisplayName,
                summary = needsConsent ? null : await stamps.GetSummaryAsync(customer.Id, ct)
            });
        }).AllowAnonymous().RequireRateLimiting("liff-auth");

        group.MapPost("/consent", async (ConsentRequest request, CustomerAuthService auth,
            CustomerContext context, CancellationToken ct) =>
        {
            var customer = await auth.RecordConsentAsync(
                context.RequireCustomerId(), request.Version, ct);

            return Results.Ok(new
            {
                ok = true,
                consentVersion = customer.ConsentVersion,
                consentAt = customer.ConsentAt
            });
        });

        // ถอนความยินยอมตาม PDPA: ปิดบัญชีและลบข้อมูลที่ระบุตัวตน
        // แต่ ledger ยังอยู่เพราะเป็นหลักฐานทางบัญชีของร้าน
        group.MapPost("/withdraw-consent", async (CustomerAuthService auth,
            CustomerContext context, HttpContext http, CancellationToken ct) =>
        {
            await auth.WithdrawConsentAsync(context.RequireCustomerId(), ct);
            await http.SignOutAsync(CustomerContext.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/me", async (CustomerAuthService auth, StampService stamps,
            CustomerContext context, HttpContext http, CancellationToken ct) =>
        {
            var customer = await ResolveOrSignOutAsync(auth, context, http, ct);
            if (customer is null) return Results.Unauthorized();

            if (!customer.HasConsented(auth.ConsentVersion))
            {
                return Results.Ok(new
                {
                    needsConsent = true,
                    consentVersion = auth.ConsentVersion,
                    displayName = customer.DisplayName
                });
            }

            return Results.Ok(new
            {
                needsConsent = false,
                consentVersion = customer.ConsentVersion,
                consentAt = customer.ConsentAt is null
                    ? null
                    : BangkokTime.FormatDateTime(customer.ConsentAt.Value),
                displayName = customer.DisplayName,
                summary = await stamps.GetSummaryAsync(customer.Id, ct),
                history = (await stamps.GetHistoryAsync(customer.Id, 20, ct)).Select(h => new
                {
                    createdAt = BangkokTime.FormatDateTime(h.CreatedAt),
                    type = h.Type.ToString(),
                    typeLabel = HistoryLabel(h.Type),
                    quantity = h.Quantity,
                    orderNo = h.OrderNo,
                    expiresAt = h.ExpiresAt is null ? null : BangkokTime.FormatDate(h.ExpiresAt.Value)
                })
            });
        });

        // QR ต้องสร้างจาก endpoint ที่ยืนยันตัวตนแล้วเท่านั้น ห้ามให้ client ประกอบเอง
        group.MapGet("/qr-token", async (CustomerAuthService auth, StampService stamps,
            CustomerContext context, HttpContext http, CancellationToken ct) =>
        {
            var customer = await ResolveOrSignOutAsync(auth, context, http, ct);
            if (customer is null) return Results.Unauthorized();

            if (!customer.HasConsented(auth.ConsentVersion))
                throw new DomainException("กรุณากดยอมรับเงื่อนไขก่อนใช้บัตรสะสมแต้ม");

            return Results.Ok(new
            {
                token = stamps.IssueQrToken(customer.PublicId),
                refreshInSeconds = StampService.SecondsUntilQrRefresh(),
                validForSeconds = CustomerQrToken.StepSeconds
            });
        }).RequireRateLimiting("qr-token");

        // รูป QR เรนเดอร์ที่เซิร์ฟเวอร์ ใช้ตัวเดียวกับที่ทำ QR พร้อมเพย์
        // ฝั่งหน้าเว็บจึงไม่ต้องมี library เข้ารหัส QR เลย
        group.MapGet("/qr.png", async (CustomerAuthService auth, StampService stamps,
            CustomerContext context, HttpContext http, CancellationToken ct) =>
        {
            var customer = await ResolveOrSignOutAsync(auth, context, http, ct);
            if (customer is null) return Results.Unauthorized();

            if (!customer.HasConsented(auth.ConsentVersion))
                throw new DomainException("กรุณากดยอมรับเงื่อนไขก่อนใช้บัตรสะสมแต้ม");

            var token = stamps.IssueQrToken(customer.PublicId);
            var png = PromptPayQrService.RenderPng(token, pixelsPerModule: 8);

            // QR หมุนทุก 30 วินาที ห้าม cache ไม่ว่าชั้นไหน
            http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            http.Response.Headers["X-Qr-Refresh-Seconds"] =
                StampService.SecondsUntilQrRefresh().ToString();

            return Results.File(png, "image/png");
        }).RequireRateLimiting("qr-token");

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CustomerContext.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous();
    }

    /// <summary>
    /// คืนบัญชีของเซสชันปัจจุบัน ถ้าบัญชีถูกปิดไปแล้ว (เช่นถูกผูกรวมกับระเบียนที่คีย์มือไว้)
    /// ให้ตัดเซสชันทิ้งแล้วคืน null เพื่อให้ฝั่งหน้าเว็บล็อกอินใหม่และเจอระเบียนที่ถูกต้อง
    /// </summary>
    private static async Task<Customer?> ResolveOrSignOutAsync(CustomerAuthService auth,
        CustomerContext context, HttpContext http, CancellationToken ct)
    {
        var customer = await auth.FindActiveAsync(context.RequireCustomerId(), ct);
        if (customer is not null) return customer;

        await http.SignOutAsync(CustomerContext.AuthenticationScheme);
        return null;
    }

    private static string HistoryLabel(StampTransactionType type) => type switch
    {
        StampTransactionType.Earn => "ได้รับแต้ม",
        StampTransactionType.Redeem => "แลกรางวัล",
        StampTransactionType.Void => "ยกเลิกออเดอร์",
        StampTransactionType.Expire => "แต้มหมดอายุ",
        StampTransactionType.Adjust => "ปรับโดยร้าน",
        _ => type.ToString()
    };
}
