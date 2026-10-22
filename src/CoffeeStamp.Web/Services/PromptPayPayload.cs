using System.Globalization;
using System.Text;
using CoffeeStamp.Web.Domain;

namespace CoffeeStamp.Web.Services;

/// <summary>
/// สร้าง payload ของ Thai QR Payment (EMVCo) สำหรับพร้อมเพย์แบบระบุยอด
/// โครงสร้าง: 00 payload format, 01 point of initiation, 29 พร้อมเพย์,
/// 53 สกุลเงิน, 54 ยอดเงิน, 58 ประเทศ, 59 ชื่อผู้รับ, 63 CRC
/// </summary>
public static class PromptPayPayload
{
    public const string AnyIdApplicationId = "A000000677010111";
    public const string CurrencyThb = "764";
    public const string CountryTh = "TH";

    private const string TagPayloadFormat = "00";
    private const string TagPointOfInitiation = "01";
    private const string TagMerchantAccount = "29";
    private const string TagCurrency = "53";
    private const string TagAmount = "54";
    private const string TagCountry = "58";
    private const string TagMerchantName = "59";
    private const string TagCrc = "63";

    public static string Build(string promptPayId, PromptPayIdType idType, decimal? amount,
        string? merchantName = null)
    {
        if (string.IsNullOrWhiteSpace(promptPayId))
            throw new ArgumentException("ยังไม่ได้ตั้งเลขพร้อมเพย์ของร้าน", nameof(promptPayId));

        var (subTag, value) = NormalizeTarget(promptPayId, idType);

        var sb = new StringBuilder();
        sb.Append(Tlv(TagPayloadFormat, "01"));
        // 11 = QR คงที่สแกนซ้ำได้, 12 = QR ใช้ครั้งเดียว ระบุยอดแล้วต้องเป็น 12
        sb.Append(Tlv(TagPointOfInitiation, amount.HasValue ? "12" : "11"));
        sb.Append(Tlv(TagMerchantAccount,
            Tlv(TagPayloadFormat, AnyIdApplicationId) + Tlv(subTag, value)));
        sb.Append(Tlv(TagCurrency, CurrencyThb));

        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
                throw new ArgumentOutOfRangeException(nameof(amount), "ยอดเงินต้องมากกว่า 0");
            if (amount.Value > 9_999_999.99m)
                throw new ArgumentOutOfRangeException(nameof(amount), "ยอดเงินเกินที่ QR รองรับ");

            sb.Append(Tlv(TagAmount,
                Money.Round(amount.Value).ToString("0.00", CultureInfo.InvariantCulture)));
        }

        sb.Append(Tlv(TagCountry, CountryTh));

        // tag 59 นับความยาวเป็นไบต์ ใส่ได้เฉพาะอักขระ ASCII เท่านั้น ชื่อไทยจึงข้ามไป
        var asciiName = ToAsciiName(merchantName);
        if (asciiName.Length > 0)
            sb.Append(Tlv(TagMerchantName, asciiName));

        sb.Append(TagCrc).Append("04");
        sb.Append(Crc16Ccitt.ComputeHex(sb.ToString()));
        return sb.ToString();
    }

    /// <summary>ตรวจว่า CRC ท้าย payload ถูกต้อง ใช้ทั้งใน test และตอน debug</summary>
    public static bool IsValid(string? payload)
    {
        if (payload is null || payload.Length < 8) return false;
        var body = payload[..^4];
        if (!body.EndsWith(TagCrc + "04", StringComparison.Ordinal)) return false;
        var expected = Crc16Ccitt.ComputeHex(body);
        return string.Equals(expected, payload[^4..], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>อ่าน TLV ชั้นบนสุดออกมาเป็น dictionary</summary>
    public static Dictionary<string, string> ReadTags(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i + 4 <= payload.Length)
        {
            var tag = payload.Substring(i, 2);
            if (!int.TryParse(payload.AsSpan(i + 2, 2), out var len)) break;
            i += 4;
            if (i + len > payload.Length) break;
            result[tag] = payload.Substring(i, len);
            i += len;
        }
        return result;
    }

    public static (string SubTag, string Value) NormalizeTarget(string rawId, PromptPayIdType idType)
    {
        var digits = new string(rawId.Where(char.IsAsciiDigit).ToArray());

        return idType switch
        {
            PromptPayIdType.Mobile => ("01", NormalizeMobile(digits)),
            PromptPayIdType.NationalId => ("02", digits.Length == 13
                ? digits
                : throw new ArgumentException("เลขบัตรประชาชนหรือเลขผู้เสียภาษีต้องมี 13 หลัก")),
            PromptPayIdType.EWalletId => ("03", digits.Length == 15
                ? digits
                : throw new ArgumentException("เลข e-Wallet ต้องมี 15 หลัก")),
            _ => throw new ArgumentOutOfRangeException(nameof(idType))
        };
    }

    /// <summary>เบอร์มือถือในพร้อมเพย์ใช้รูปแบบ 13 หลัก คือ 0066 + เบอร์ที่ตัด 0 หน้าออก</summary>
    public static string NormalizeMobile(string digits)
    {
        if (digits.Length == 13 && digits.StartsWith("0066", StringComparison.Ordinal))
            return digits;
        if (digits.Length == 11 && digits.StartsWith("66", StringComparison.Ordinal))
            return "00" + digits;
        if (digits.Length == 10 && digits.StartsWith('0'))
            return "0066" + digits[1..];
        if (digits.Length == 9)
            return "0066" + digits;

        throw new ArgumentException($"เบอร์พร้อมเพย์ไม่ถูกต้อง: {digits}");
    }

    private static string Tlv(string tag, string value) =>
        tag + value.Length.ToString("D2", CultureInfo.InvariantCulture) + value;

    private static string ToAsciiName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var cleaned = new string(name.Trim()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '-' or '&')
            .ToArray())
            .Trim();
        return cleaned.Length > 25 ? cleaned[..25].Trim() : cleaned;
    }
}
