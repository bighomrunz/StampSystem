namespace CoffeeStamp.Web.Services;

public static class Money
{
    /// <summary>ปัดเป็น 2 ตำแหน่งแบบปัดขึ้นที่ครึ่ง ให้ตรงกับ decimal(10,2) ใน DB</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string Format(decimal value) => value.ToString("#,##0.00");
}
