namespace CoffeeStamp.Web.Services;

/// <summary>
/// ข้อผิดพลาดเชิงกฎธุรกิจ ข้อความเป็นภาษาไทยและเอาไปแสดงให้พนักงานเห็นได้เลย
/// endpoint จะ map เป็น HTTP 400
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
