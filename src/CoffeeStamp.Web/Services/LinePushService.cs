using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Web.Services;

/// <summary>
/// Push ข้อความแจ้งแต้มผ่าน Messaging API
/// ความล้มเหลวของ push ต้องไม่ทำให้การออกแต้มล้มเหลว แต้มถูกบันทึกลง ledger แล้ว
/// </summary>
public class LinePushService
{
    public const string PushUrl = "https://api.line.me/v2/bot/message/push";

    private readonly HttpClient _http;
    private readonly LineOptions _options;
    private readonly ILogger<LinePushService> _logger;

    public LinePushService(HttpClient http, IOptions<LineOptions> options,
        ILogger<LinePushService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>คืน true เมื่อส่งสำเร็จ ไม่โยน exception ออกไปไม่ว่ากรณีใด</summary>
    public async Task<bool> SendTextAsync(string lineUserId, string text,
        CancellationToken ct = default)
    {
        if (!_options.IsPushConfigured)
        {
            _logger.LogInformation("ข้าม push เพราะยังไม่ได้ตั้ง Messaging API: {Text}", text);
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PushUrl)
            {
                Content = JsonContent.Create(new
                {
                    to = lineUserId,
                    messages = new[] { new { type = "text", text } }
                })
            };
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.MessagingChannelAccessToken);

            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("push LINE ไม่สำเร็จ {Status}: {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "push LINE ไม่สำเร็จ แต้มถูกบันทึกเรียบร้อยแล้ว");
            return false;
        }
    }

    /// <summary>ข้อความแจ้งแต้มตามที่ README กำหนด: ได้รับ X แต้ม (รวม Y/10)</summary>
    public static string BuildEarnMessage(int earned, int balance, int stampsPerReward,
        string shopName)
    {
        var lines = new List<string>
        {
            $"{shopName}",
            $"ได้รับ {earned} แต้ม (รวม {balance}/{stampsPerReward})"
        };

        if (stampsPerReward > 0)
        {
            var rewards = balance / stampsPerReward;
            var remaining = stampsPerReward - balance % stampsPerReward;

            if (rewards > 0)
                lines.Add($"แลกเครื่องดื่มฟรีได้ {rewards} แก้ว");
            else
                lines.Add($"อีก {remaining} แต้มแลกฟรี 1 แก้ว");
        }

        return string.Join('\n', lines);
    }
}
