using System.Security.Cryptography;
using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Data;

public static class DbSeeder
{
    /// <summary>
    /// สร้างแถวตั้งค่า บัญชีเจ้าของร้านแรก และเมนูตัวอย่าง (ถ้าเปิดไว้)
    /// ปลอดภัยต่อการเรียกซ้ำ ไม่เขียนทับข้อมูลที่มีอยู่
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, IConfiguration config, ILogger logger,
        CancellationToken ct = default)
    {
        if (!await db.ShopSettings.AnyAsync(ct))
        {
            db.ShopSettings.Add(new ShopSettings
            {
                Id = ShopSettings.SingletonId,
                ShopName = config["Shop:Name"] ?? ShopSettings.DefaultShopName
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("สร้างแถวตั้งค่าร้านเริ่มต้นแล้ว");
        }

        if (!await db.Staff.AnyAsync(ct))
        {
            var pin = config["Bootstrap:OwnerPin"];
            var generated = false;

            if (PinPolicy.Validate(pin) is not null)
            {
                pin = GeneratePin();
                generated = true;
            }

            var hasher = new PasswordHasher<Staff>();
            var owner = new Staff
            {
                Name = config["Bootstrap:OwnerName"] ?? "เจ้าของร้าน",
                Role = StaffRole.Owner,
                PinHash = hasher.HashPassword(new Staff(), pin!),
                MustChangePin = true,
                IsActive = true
            };
            db.Staff.Add(owner);
            await db.SaveChangesAsync(ct);

            if (generated)
            {
                // แสดงครั้งเดียวตอนติดตั้ง ระบบบังคับเปลี่ยน PIN ทันทีที่ล็อกอินครั้งแรก
                logger.LogWarning(
                    "สร้างบัญชีเจ้าของร้าน {Name} พร้อม PIN ชั่วคราว {Pin} กรุณาเปลี่ยนทันทีหลังล็อกอิน",
                    owner.Name, pin);
            }
            else
            {
                logger.LogInformation("สร้างบัญชีเจ้าของร้าน {Name} ด้วย PIN จาก Bootstrap:OwnerPin",
                    owner.Name);
            }
        }

        var seedMenu = config.GetValue("Bootstrap:SeedSampleMenu", false);
        if (seedMenu && !await db.MenuItems.AnyAsync(ct))
        {
            db.MenuItems.AddRange(SampleMenu());
            await db.SaveChangesAsync(ct);
            logger.LogInformation("ใส่เมนูตัวอย่างแล้ว");
        }
    }

    private static string GeneratePin()
    {
        // เลี่ยง PIN ที่ไม่ผ่านนโยบาย เช่น เลขซ้ำหรือเลขเรียง
        while (true)
        {
            var pin = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
            if (PinPolicy.Validate(pin) is null) return pin;
        }
    }

    private static IEnumerable<MenuItem> SampleMenu() =>
    [
        new() { Name = "อเมริกาโน่ร้อน", Category = "กาแฟ", Price = 45m, SortOrder = 10 },
        new() { Name = "อเมริกาโน่เย็น", Category = "กาแฟ", Price = 50m, SortOrder = 20 },
        new() { Name = "ลาเต้ร้อน", Category = "กาแฟ", Price = 50m, SortOrder = 30 },
        new() { Name = "ลาเต้เย็น", Category = "กาแฟ", Price = 55m, SortOrder = 40 },
        new() { Name = "คาปูชิโน่", Category = "กาแฟ", Price = 55m, SortOrder = 50 },
        new() { Name = "มอคค่าเย็น", Category = "กาแฟ", Price = 60m, SortOrder = 60 },
        new() { Name = "ชาไทยเย็น", Category = "ชา", Price = 45m, SortOrder = 70 },
        new() { Name = "ชาเขียวเย็น", Category = "ชา", Price = 50m, SortOrder = 80 },
        new() { Name = "โกโก้เย็น", Category = "อื่น ๆ", Price = 50m, SortOrder = 90 },
        new()
        {
            Name = "น้ำเปล่า",
            Category = "อื่น ๆ",
            Price = 10m,
            EarnsStamp = false,
            IsRewardEligible = false,
            SortOrder = 100
        }
    ];
}
