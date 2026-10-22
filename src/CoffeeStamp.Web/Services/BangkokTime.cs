namespace CoffeeStamp.Web.Services;

/// <summary>
/// เวลาเก็บเป็น UTC เสมอ แสดงผลและคิดวันทำการเป็น Asia/Bangkok
/// </summary>
public static class BangkokTime
{
    public static readonly TimeZoneInfo Zone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Bangkok", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // ไทยไม่มี DST ตั้งแต่ปี 2495 ใช้ offset คงที่เป็นทางหนีทีไล่
        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>วันทำการ = วันที่ตามปฏิทินไทยของเวลานั้น</summary>
    public static DateOnly BusinessDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public static DateOnly Today() => BusinessDate(DateTime.UtcNow);

    public static TimeOnly LocalTimeOf(DateTime utc) => TimeOnly.FromDateTime(ToLocal(utc));

    /// <summary>
    /// สิ้นสุดวันตามเวลาไทยในรูป UTC ใช้กำหนดวันหมดอายุของแต้ม
    /// ให้ลูกค้าได้ใช้แต้มจนหมดวันสุดท้าย ไม่ใช่หมดกลางวันแบบนับจากวินาทีที่ซื้อ
    /// </summary>
    public static DateTime EndOfLocalDayUtc(DateOnly localDate)
    {
        var localEnd = localDate.ToDateTime(new TimeOnly(23, 59, 59));
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), Zone);
    }

    public static string FormatDate(DateTime utc) => ToLocal(utc).ToString("dd/MM/yyyy");

    public static string FormatDateTime(DateTime utc) => ToLocal(utc).ToString("dd/MM/yyyy HH:mm");

    public static string FormatTime(DateTime utc) => ToLocal(utc).ToString("HH:mm");
}
