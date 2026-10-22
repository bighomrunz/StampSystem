using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeStamp.Web.Services;

public record QrTokenPayload(Guid PublicId, long Step);

/// <summary>
/// QR ของลูกค้าแบบหมุนเวียนทุก 30 วินาที
/// token = CustomerPublicId + timeStep + HMAC-SHA256(secret) ยอมรับคลาดได้ ±1 step
/// การกันใช้ซ้ำอยู่ที่ Customer.LastUsedQrStep (ดู StampService) ไม่ได้อยู่ในตัว token
///
/// เหตุผลที่ต้องหมุน: ระบบเดิมโดนโกงเพราะลูกค้าแคป QR ของร้านไปใช้ซ้ำหรือส่งต่อ
/// QR ที่หมุนทุก 30 วินาทีและใช้ได้ครั้งเดียวทำให้ภาพที่แคปไว้ไม่มีค่า
/// </summary>
public static class CustomerQrToken
{
    public const string Prefix = "CS1";
    public const int StepSeconds = 30;
    public const int AllowedStepDrift = 1;

    /// <summary>ตัด MAC ให้สั้นลงเพื่อให้ QR อ่านง่ายบนจอมือถือ 128 บิตยังเหลือเฟือ</summary>
    private const int MacBytes = 16;

    public static long StepOf(DateTime utcNow) =>
        new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds()
        / StepSeconds;

    /// <summary>วินาทีที่เหลือก่อน token ชุดนี้จะเปลี่ยน ใช้ให้หน้าจอรู้ว่าควรรีเฟรชเมื่อไร</summary>
    public static int SecondsUntilNextStep(DateTime utcNow)
    {
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc))
            .ToUnixTimeSeconds();
        return (int)(StepSeconds - seconds % StepSeconds);
    }

    public static string Issue(byte[] secret, Guid publicId, DateTime utcNow)
    {
        var step = StepOf(utcNow);
        var mac = ComputeMac(secret, publicId, step);
        return string.Join('.', Prefix, Base64Url(publicId.ToByteArray()), step, Base64Url(mac));
    }

    /// <summary>
    /// ตรวจ token แบบไม่สนใจว่าถูกใช้ไปแล้วหรือยัง
    /// โยน DomainException พร้อมข้อความไทยที่เอาไปโชว์พนักงานได้ทันที
    /// </summary>
    public static QrTokenPayload Verify(byte[] secret, string? token, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException("ไม่พบ QR ของลูกค้า");

        var parts = token.Trim().Split('.');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            throw new DomainException("QR นี้ไม่ใช่ QR สมาชิกของร้าน");

        Guid publicId;
        byte[] mac;
        try
        {
            var idBytes = FromBase64Url(parts[1]);

            // ต้องเช็กความยาวเอง ไม่งั้น new Guid() โยน ArgumentException ออกไปเป็น 500
            // ซึ่งเกิดได้ง่ายถ้ามีใครยิง token เพี้ยน ๆ เข้ามาที่ endpoint สแกน
            if (idBytes.Length != 16)
                throw new FormatException("ความยาวของรหัสสมาชิกไม่ถูกต้อง");

            publicId = new Guid(idBytes);
            mac = FromBase64Url(parts[3]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new DomainException("QR นี้ไม่ใช่ QR สมาชิกของร้าน");
        }

        if (!long.TryParse(parts[2], out var step))
            throw new DomainException("QR นี้ไม่ใช่ QR สมาชิกของร้าน");

        // ตรวจลายเซ็นก่อนเสมอ แล้วจึงดูเรื่องเวลา กันการเดา publicId จากข้อความ error
        var expected = ComputeMac(secret, publicId, step);
        if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(mac, expected))
            throw new DomainException("QR นี้ไม่ถูกต้อง อาจเป็นภาพที่แคปมาจากเครื่องอื่น");

        var current = StepOf(utcNow);
        if (Math.Abs(current - step) > AllowedStepDrift)
            throw new DomainException("QR หมดอายุแล้ว ให้ลูกค้าเปิดหน้าบัตรใหม่อีกครั้ง");

        return new QrTokenPayload(publicId, step);
    }

    private static byte[] ComputeMac(byte[] secret, Guid publicId, long step)
    {
        // ผูก prefix เข้าไปใน MAC ด้วย เผื่อวันหลังมี token เวอร์ชันอื่นแล้วเอามาสลับกันใช้
        var message = new byte[Prefix.Length + 16 + sizeof(long)];
        Encoding.ASCII.GetBytes(Prefix).CopyTo(message, 0);
        publicId.ToByteArray().CopyTo(message, Prefix.Length);
        BinaryPrimitives.WriteInt64BigEndian(
            message.AsSpan(Prefix.Length + 16), step);

        return HMACSHA256.HashData(secret, message)[..MacBytes];
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => "",
            _ => throw new FormatException("ความยาว base64url ไม่ถูกต้อง")
        };
        return Convert.FromBase64String(padded);
    }
}
