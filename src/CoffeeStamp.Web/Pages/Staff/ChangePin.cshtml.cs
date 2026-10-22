using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CoffeeStamp.Web.Pages.Staff;

public class ChangePinModel : PageModel
{
    private readonly StaffContext _staff;

    public ChangePinModel(StaffContext staff) => _staff = staff;

    public bool MustChange => _staff.MustChangePin;

    public void OnGet() { }
}
