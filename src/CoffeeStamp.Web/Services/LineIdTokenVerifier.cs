using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Web.Services;

/// <summary>ผลการ verify ID token กับ LINE — userId ที่เชื่อถือได้มาจากที่นี่ที่เดียว</summary>
public record LineProfile(string UserId, string? DisplayName, string? PictureUrl);

public class LineVerifyResponse
{
    [JsonPropertyName("sub")]
    public string? Sub { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("aud")]
    public string? Aud { get; set; }

    [JsonPropertyName("iss")]
    public string? Iss { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// ส่ง ID token ไปให้ LINE ตรวจทุกครั้ง ห้ามเชื่อ userId ที่ client ส่งมา
/// (ถ้าเชื่อ ใครก็ปลอมเป็นสมาชิกคนอื่นแล้วขอแต้มได้)
/// </summary>
public class LineIdTokenVerifier
{
    public const string VerifyUrl = "https://api.line.me/oauth2/v2.1/verify";
    public const string ExpectedIssuer = "https://access.line.me";

    private readonly HttpClient _http;
    private readonly LineOptions _options;
    private readonly ILogger<LineIdTokenVerifier> _logger;

    public LineIdTokenVerifier(HttpClient http, IOptions<LineOptions> options,
        ILogger<LineIdTokenVerifier> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LineProfile> VerifyAsync(string? idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new DomainException("ไม่พบ ID token จาก LINE");

        if (!_options.IsLoginConfigured)
            throw new DomainException("ยังไม่ได้ตั้งค่า Line:LoginChannelId ของร้าน");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_token"] = idToken,
            ["client_id"] = _options.LoginChannelId!
        });

        LineVerifyResponse? result;
        try
        {
            using var response = await _http.PostAsync(VerifyUrl, content, ct);
            result = await response.Content.ReadFromJsonAsync<LineVerifyResponse>(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LINE ปฏิเสธ ID token: {Status} {Error} {Description}",
                    (int)response.StatusCode, result?.Error, result?.ErrorDescription);
                throw new DomainException("ยืนยันตัวตนกับ LINE ไม่สำเร็จ กรุณาเปิดจากแอป LINE อีกครั้ง");
            }
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ติดต่อ LINE เพื่อ verify ID token ไม่สำเร็จ");
            throw new DomainException("ติดต่อ LINE ไม่ได้ กรุณาลองใหม่อีกครั้ง");
        }

        return Validate(result);
    }

    /// <summary>แยกออกมาเพื่อให้เทสได้โดยไม่ต้องยิงเน็ต</summary>
    public LineProfile Validate(LineVerifyResponse? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Sub))
            throw new DomainException("ยืนยันตัวตนกับ LINE ไม่สำเร็จ");

        // LINE ตรวจลายเซ็น อายุ และ nonce ให้แล้ว แต่ aud/iss ต้องตรวจเองกันโทเคนจาก channel อื่น
        if (!string.IsNullOrWhiteSpace(result.Aud) &&
            !string.Equals(result.Aud, _options.LoginChannelId, StringComparison.Ordinal))
        {
            _logger.LogWarning("ID token มาจาก channel อื่น: aud={Aud}", result.Aud);
            throw new DomainException("ยืนยันตัวตนกับ LINE ไม่สำเร็จ");
        }

        if (!string.IsNullOrWhiteSpace(result.Iss) &&
            !string.Equals(result.Iss, ExpectedIssuer, StringComparison.Ordinal))
        {
            _logger.LogWarning("ID token มี issuer ผิด: iss={Iss}", result.Iss);
            throw new DomainException("ยืนยันตัวตนกับ LINE ไม่สำเร็จ");
        }

        return new LineProfile(result.Sub, result.Name, result.Picture);
    }
}
