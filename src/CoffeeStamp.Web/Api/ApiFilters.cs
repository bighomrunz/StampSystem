using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace CoffeeStamp.Web.Api;

/// <summary>แปลง DomainException เป็น HTTP 400 พร้อมข้อความไทยที่เอาไปโชว์ได้ทันที</summary>
public class DomainErrorFilter : IEndpointFilter
{
    private readonly ILogger<DomainErrorFilter> _logger;

    public DomainErrorFilter(ILogger<DomainErrorFilter> logger) => _logger = logger;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DomainException ex)
        {
            _logger.LogInformation("ปฏิเสธคำขอตามกฎธุรกิจ: {Message}", ex.Message);
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

/// <summary>
/// ตรวจ antiforgery token ของทุก request ที่เปลี่ยนข้อมูล
/// เพราะ API ใช้ cookie เป็นตัวยืนยันตัวตน
/// </summary>
public class ApiAntiforgeryFilter : IEndpointFilter
{
    private readonly IAntiforgery _antiforgery;

    public ApiAntiforgeryFilter(IAntiforgery antiforgery) => _antiforgery = antiforgery;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            try
            {
                await _antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "เซสชันหมดอายุ กรุณารีเฟรชหน้าจอแล้วลองใหม่" });
            }
        }

        return await next(context);
    }
}
