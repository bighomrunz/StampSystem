using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoffeeStamp.Web.Data;

/// <summary>
/// ใช้ตอน design-time (dotnet ef migrations) โดยไม่ต้องต่อ PostgreSQL จริง
/// ตั้ง connection string เองได้ด้วย environment variable COFFEESTAMP_CONNECTION
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("COFFEESTAMP_CONNECTION")
                   ?? "Host=localhost;Port=5432;Database=coffeestamp;Username=postgres;Password=";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
