using System.Security.Cryptography;

namespace CoffeeStamp.Web.Services;

/// <summary>
/// อ่าน secret ของ QR ลูกค้าจาก config ครั้งเดียวตอนบูต
/// ห้าม commit ค่านี้ ให้เก็บใน user-secrets หรือ environment variable
/// </summary>
public class QrSecretProvider
{
    public const string ConfigKey = "Qr:Secret";
    private const int MinimumSecretBytes = 32;

    public byte[] Secret { get; }

    public QrSecretProvider(IConfiguration config, ILogger<QrSecretProvider> logger,
        IHostEnvironment environment)
    {
        var configured = config[ConfigKey];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            Secret = Decode(configured);

            if (Secret.Length < MinimumSecretBytes)
            {
                throw new InvalidOperationException(
                    $"{ConfigKey} สั้นเกินไป ต้องมีอย่างน้อย {MinimumSecretBytes} ไบต์");
            }

            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"ยังไม่ได้ตั้ง {ConfigKey} ตั้งผ่าน dotnet user-secrets หรือ environment variable " +
                "(ถ้า secret เปลี่ยน QR ที่ลูกค้าเปิดค้างไว้จะใช้ไม่ได้ทันที)");
        }

        // เฉพาะ Development เท่านั้น สุ่มใหม่ทุกครั้งที่รีสตาร์ต
        Secret = RandomNumberGenerator.GetBytes(MinimumSecretBytes);
        logger.LogWarning(
            "ไม่พบ {Key} จึงสุ่ม secret ชั่วคราวสำหรับ Development " +
            "QR ที่ออกก่อนรีสตาร์ตจะใช้ไม่ได้", ConfigKey);
    }

    private static byte[] Decode(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            // ยอมรับข้อความธรรมดาด้วย เพื่อให้ตั้งค่าตอน dev ได้ง่าย
            return System.Text.Encoding.UTF8.GetBytes(value);
        }
    }
}
