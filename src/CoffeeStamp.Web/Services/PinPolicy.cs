namespace CoffeeStamp.Web.Services;

public static class PinPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    /// <summary>คืนข้อความผิดพลาดเป็นภาษาไทย หรือ null ถ้า PIN ผ่านเกณฑ์</summary>
    public static string? Validate(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return "กรุณากรอก PIN";
        if (!pin.All(char.IsAsciiDigit))
            return "PIN ต้องเป็นตัวเลขเท่านั้น";
        if (pin.Length < MinLength || pin.Length > MaxLength)
            return $"PIN ต้องมี {MinLength} ถึง {MaxLength} หลัก";
        if (pin.Distinct().Count() == 1)
            return "PIN ห้ามเป็นเลขซ้ำกันทั้งหมด";
        if (IsSequential(pin))
            return "PIN ห้ามเป็นเลขเรียงกัน";
        return null;
    }

    private static bool IsSequential(string pin)
    {
        var up = true;
        var down = true;
        for (var i = 1; i < pin.Length; i++)
        {
            var diff = pin[i] - pin[i - 1];
            if (diff != 1) up = false;
            if (diff != -1) down = false;
        }
        return up || down;
    }
}
