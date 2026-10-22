namespace CoffeeStamp.Web.Services;

/// <summary>
/// งานรายวันที่ตัดแต้มหมดอายุ ตั้งให้ทำงานตอนตี 3 เวลาไทย ซึ่งร้านปิดแล้วแน่นอน
///
/// ร้านนี้ไม่ได้เปิดเครื่องทิ้งไว้ 24 ชั่วโมง งานจึงต้อง
/// 1. ตรวจทันทีตอนแอปสตาร์ต เผื่อข้ามวันที่เครื่องปิดอยู่
/// 2. ตรวจซ้ำทุกวัน ถ้าเครื่องเปิดค้างไว้
/// ทั้งสองทางใช้ ExpireDueStampsAsync ตัวเดียวกัน ซึ่งมองจากวันหมดอายุของแต่ละก้อน
/// ไม่ได้นับว่า job เคยรันไปกี่ครั้ง จึงรันซ้ำได้โดยไม่ตัดแต้มเกิน
/// </summary>
public class StampExpiryJob : BackgroundService
{
    private static readonly TimeOnly RunAtLocal = new(3, 0);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StampExpiryJob> _logger;

    public StampExpiryJob(IServiceScopeFactory scopeFactory, ILogger<StampExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // รอให้ migration และ seed ทำงานเสร็จก่อน
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            _logger.LogInformation("ตัดแต้มหมดอายุรอบถัดไปในอีก {Hours} ชั่วโมง",
                Math.Round(delay.TotalHours, 1));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>เวลาที่เหลือจนถึงตี 3 ไทยรอบถัดไป คำนวณแยกไว้ให้เทสได้</summary>
    public static TimeSpan TimeUntilNextRun(DateTime utcNow)
    {
        var local = BangkokTime.ToLocal(utcNow);
        var next = local.Date.Add(RunAtLocal.ToTimeSpan());

        if (next <= local) next = next.AddDays(1);

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(next, DateTimeKind.Unspecified), BangkokTime.Zone);

        return nextUtc - DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var stamps = scope.ServiceProvider.GetRequiredService<StampService>();
            var expired = await stamps.ExpireDueStampsAsync(ct);

            if (expired > 0)
                _logger.LogInformation("ตัดแต้มหมดอายุไป {Expired} แต้ม", expired);
        }
        catch (Exception ex)
        {
            // ห้ามให้ job ล้มทั้งตัว ไม่งั้นจะไม่มีใครตัดแต้มหมดอายุอีกเลยจนกว่าจะรีสตาร์ต
            _logger.LogError(ex, "ตัดแต้มหมดอายุไม่สำเร็จ จะลองใหม่รอบถัดไป");
        }
    }
}
