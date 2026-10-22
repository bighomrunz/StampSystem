namespace CoffeeStamp.Web.Services;

/// <summary>
/// เบอร์โทรเก็บเป็นตัวเลขล้วนเสมอ เพื่อให้ค้นหาเจอไม่ว่าพนักงานจะพิมพ์ 081-234-5678
/// หรือ 0812345678 และกันข้อมูลซ้ำที่ต่างกันแค่เครื่องหมาย
/// </summary>
public static class PhoneNumber
{
    /// <summary>คืนเฉพาะตัวเลข หรือ null ถ้าว่าง</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>คืนข้อความผิดพลาดเป็นภาษาไทย หรือ null ถ้าผ่าน (เบอร์ไม่บังคับกรอก)</summary>
    public static string? Validate(string? input)
    {
        var digits = Normalize(input);
        if (digits is null) return null;

        // มือถือไทย 10 หลักขึ้นต้น 0 หรือเบอร์บ้าน 9 หลัก
        if (digits.Length is < 9 or > 10)
            return "เบอร์โทรต้องมี 9 ถึง 10 หลัก";

        if (!digits.StartsWith('0'))
            return "เบอร์โทรต้องขึ้นต้นด้วย 0";

        return null;
    }

    /// <summary>จัดรูปแบบให้อ่านง่ายบนหน้าจอ เช่น 081-234-5678</summary>
    public static string Format(string? digits)
    {
        var normalized = Normalize(digits);
        if (normalized is null) return "-";

        return normalized.Length switch
        {
            10 => $"{normalized[..3]}-{normalized[3..6]}-{normalized[6..]}",
            9 => $"{normalized[..2]}-{normalized[2..5]}-{normalized[5..]}",
            _ => normalized
        };
    }
}
