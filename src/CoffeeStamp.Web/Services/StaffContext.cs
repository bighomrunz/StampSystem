using System.Security.Claims;
using CoffeeStamp.Web.Domain;

namespace CoffeeStamp.Web.Services;

/// <summary>ข้อมูลพนักงานและอุปกรณ์ของ request ปัจจุบัน อ่านจาก claim ของ cookie</summary>
public class StaffContext
{
    public const string DeviceClaim = "device_id";
    public const string MustChangePinClaim = "must_change_pin";

    private readonly IHttpContextAccessor _accessor;

    public StaffContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public bool IsAuthenticated => StaffId is not null;

    public int? StaffId =>
        int.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public string? Name => User?.FindFirst(ClaimTypes.Name)?.Value;

    public StaffRole? Role =>
        Enum.TryParse<StaffRole>(User?.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : null;

    public Guid? DeviceId =>
        Guid.TryParse(User?.FindFirst(DeviceClaim)?.Value, out var d) ? d : null;

    public bool MustChangePin =>
        string.Equals(User?.FindFirst(MustChangePinClaim)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsManagerOrAbove => Role is StaffRole.Manager or StaffRole.Owner;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public int RequireStaffId() => StaffId ?? throw new DomainException("ยังไม่ได้ล็อกอิน");

    public Guid RequireDeviceId() => DeviceId ?? throw new DomainException("อุปกรณ์นี้ยังไม่ได้ลงทะเบียน");
}
