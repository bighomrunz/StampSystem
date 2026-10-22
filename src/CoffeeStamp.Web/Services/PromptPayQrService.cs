using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CoffeeStamp.Web.Services;

public class PromptPayQrService
{
    private readonly AppDbContext _db;

    public PromptPayQrService(AppDbContext db) => _db = db;

    public async Task<string> BuildPayloadForAmountAsync(decimal amount, CancellationToken ct = default)
    {
        var settings = await _db.ShopSettings.AsNoTracking()
            .FirstAsync(s => s.Id == ShopSettings.SingletonId, ct);

        return PromptPayPayload.Build(
            settings.PromptPayId ?? throw new InvalidOperationException(
                "ยังไม่ได้ตั้งเลขพร้อมเพย์ในหน้าตั้งค่า"),
            settings.PromptPayIdType,
            amount,
            settings.PromptPayMerchantName);
    }

    public static byte[] RenderPng(string payload, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
