using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages;

public class DailyCloseModel : PageModel
{
    private readonly DailyCloseService _dailyClose;
    private readonly SettingsService _settings;

    public DailyCloseModel(DailyCloseService dailyClose, SettingsService settings)
    {
        _dailyClose = dailyClose;
        _settings = settings;
    }

    public DateOnly BusinessDate { get; private set; } = BangkokTime.Today();
    public DailyCloseSummary? Summary { get; private set; }
    public decimal VarianceThreshold { get; private set; } = 20m;
    public string? LoadError { get; private set; }

    public async Task OnGetAsync(string? date, CancellationToken ct)
    {
        if (DateOnly.TryParse(date, out var parsed))
            BusinessDate = parsed;

        try
        {
            Summary = await _dailyClose.GetSummaryAsync(BusinessDate, ct);
            VarianceThreshold = (await _settings.GetAsync(ct)).VarianceAlertThreshold;
        }
        catch (Exception)
        {
            LoadError = "โหลดยอดของวันนี้ไม่สำเร็จ กรุณาลองใหม่";
        }
    }

    public string VarianceClass(decimal variance) => variance switch
    {
        < 0 => "variance-neg",
        > 0 => "variance-pos",
        _ => ""
    };
}
