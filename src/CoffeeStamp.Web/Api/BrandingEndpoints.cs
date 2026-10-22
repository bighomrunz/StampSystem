using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Web.Api;

public static class BrandingEndpoints
{
    /// <summary>
    /// manifest ของ PWA สร้างจากชื่อร้านใน ShopSettings ไม่ใช่ไฟล์ตายตัว
    /// เปลี่ยนชื่อร้านที่หน้าตั้งค่าแล้วชื่อบนไอคอนที่ลูกค้าติดตั้งไว้จะเปลี่ยนตาม
    /// (map ไว้นอก group /api เพราะเบราว์เซอร์ขอไฟล์นี้ที่ราก และต้องเรียกได้โดยไม่ต้องล็อกอิน)
    /// </summary>
    public static void MapWebManifest(this IEndpointRouteBuilder app)
    {
        app.MapGet("/manifest.webmanifest", async (SettingsService settings,
            HttpContext http, CancellationToken ct) =>
        {
            var shopName = await settings.GetShopNameAsync(ct);
            var shortName = ShortName(shopName);

            http.Response.Headers.CacheControl = "no-cache";

            return Results.Json(new
            {
                name = $"{shopName} — แอปพนักงาน",
                short_name = shortName,
                description = $"สะสมแต้มและขายหน้าร้านสำหรับทีมงาน {shopName}",
                lang = "th",
                start_url = "/",
                scope = "/",
                display = "standalone",
                orientation = "any",
                background_color = "#0f3d34",
                theme_color = "#0f3d34",
                icons = new[]
                {
                    new
                    {
                        src = "/icons/icon-192.png",
                        sizes = "192x192",
                        type = "image/png",
                        purpose = "any maskable"
                    },
                    new
                    {
                        src = "/icons/icon-512.png",
                        sizes = "512x512",
                        type = "image/png",
                        purpose = "any maskable"
                    }
                }
            }, contentType: "application/manifest+json");
        }).AllowAnonymous();
    }

    /// <summary>
    /// ชื่อสั้นสำหรับไอคอนบนหน้าจอโฮม Android แนะนำไม่เกิน 12 ตัวอักษร
    /// ต้องตัดตามหน่วยที่คนอ่าน ไม่ใช่ตาม char ของ .NET
    /// เพราะชื่อไทยหนึ่งตัวอักษรมีสระและวรรณยุกต์ต่อท้าย ถ้าตัดดิบ ๆ จะได้ตัวอักษรพิการ
    /// </summary>
    public static string ShortName(string shopName, int maxUnits = 12)
    {
        var name = shopName.Trim();
        var elements = new System.Globalization.StringInfo(name);

        return elements.LengthInTextElements <= maxUnits
            ? name
            : elements.SubstringByTextElements(0, maxUnits).TrimEnd();
    }
}
