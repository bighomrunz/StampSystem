using System.Security.Claims;

namespace CoffeeStamp.Web.Services;

/// <summary>
/// เซสชันของลูกค้าใน LIFF แยก cookie และ scheme ออกจากฝั่งพนักงานโดยสิ้นเชิง
/// ลูกค้าต้องไม่สามารถเรียก API ของพนักงานได้ และกลับกัน
/// </summary>
public class CustomerContext
{
    public const string AuthenticationScheme = "Customer";
    public const string CookieName = "cs_customer";
    public const string PublicIdClaim = "customer_public_id";

    private readonly IHttpContextAccessor _accessor;

    public CustomerContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            return user?.Identities.FirstOrDefault(i =>
                i.IsAuthenticated &&
                string.Equals(i.AuthenticationType, AuthenticationScheme, StringComparison.Ordinal))
                is { } identity
                ? new ClaimsPrincipal(identity)
                : null;
        }
    }

    public int? CustomerId =>
        int.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public Guid? PublicId =>
        Guid.TryParse(Principal?.FindFirst(PublicIdClaim)?.Value, out var pid) ? pid : null;

    public bool IsAuthenticated => CustomerId is not null;

    public int RequireCustomerId() =>
        CustomerId ?? throw new DomainException("ยังไม่ได้เข้าสู่ระบบ กรุณาเปิดหน้านี้จากแอป LINE");
}
