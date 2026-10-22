using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Web.Pages;

public class CustomersModel : PageModel
{
    private readonly LineOptions _line;

    public CustomersModel(IOptions<LineOptions> line) => _line = line.Value;

    /// <summary>ยังไม่ได้ตั้ง LIFF = อยู่ในช่วงคีย์มือ ต้องบอกพนักงานให้ชัด</summary>
    public bool IsLiffEnabled => !string.IsNullOrWhiteSpace(_line.LiffId);

    public void OnGet() { }
}
