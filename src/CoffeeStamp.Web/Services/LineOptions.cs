namespace CoffeeStamp.Web.Services;

public class LineOptions
{
    public const string SectionName = "Line";

    /// <summary>LIFF ID ที่ฝั่งหน้าเว็บใช้เรียก liff.init</summary>
    public string? LiffId { get; set; }

    /// <summary>Channel ID ของ LINE Login ใช้เป็น client_id ตอน verify ID token</summary>
    public string? LoginChannelId { get; set; }

    /// <summary>Channel access token ของ Messaging API ใช้ push แจ้งแต้ม</summary>
    public string? MessagingChannelAccessToken { get; set; }

    /// <summary>ปิด push ชั่วคราวได้ โดยไม่ต้องลบ token ออกจาก config</summary>
    public bool EnablePush { get; set; } = true;

    /// <summary>
    /// เปิดให้ล็อกอิน LIFF ด้วย userId ปลอมเพื่อทดสอบ ใช้ได้เฉพาะ Development
    /// Program.cs จะไม่ยอมให้ค่านี้มีผลนอก Development
    /// </summary>
    public bool AllowDevMockLogin { get; set; }

    public bool IsLoginConfigured => !string.IsNullOrWhiteSpace(LoginChannelId);

    public bool IsPushConfigured =>
        EnablePush && !string.IsNullOrWhiteSpace(MessagingChannelAccessToken);
}
