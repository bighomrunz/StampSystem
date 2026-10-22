using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CoffeeStamp.Web.Api;
using CoffeeStamp.Web.Data;
using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- ฐานข้อมูล ---
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    // ค่าว่างเป็นกรณีที่เจอบ่อยกว่าค่า null เช่นลืม set user-secrets หรือรันผิด environment
    throw new InvalidOperationException(
        "ยังไม่ได้ตั้ง ConnectionStrings:Default สำหรับ PostgreSQL " +
        "(ตั้งผ่าน dotnet user-secrets หรือ environment variable)");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString,
            npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null))
        // ชื่อตารางและคอลัมน์เป็น snake_case เพื่อให้เขียน SQL ตรวจยอดด้วยมือได้สะดวก
        .UseSnakeCaseNamingConvention();

    if (builder.Environment.IsDevelopment())
        options.EnableDetailedErrors();
});

// --- service layer แยกจาก endpoint ทั้งหมด ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPasswordHasher<Staff>, PasswordHasher<Staff>>();
builder.Services.AddScoped<StaffContext>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<StaffAuthService>();
builder.Services.AddScoped<StaffAdminService>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<DailyCloseService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<PromptPayQrService>();

// --- Phase 2: สมาชิก LIFF และแต้ม ---
builder.Services.Configure<LineOptions>(builder.Configuration.GetSection(LineOptions.SectionName));
builder.Services.AddSingleton<QrSecretProvider>();
builder.Services.AddScoped<CustomerContext>();
builder.Services.AddScoped<CustomerAuthService>();
builder.Services.AddScoped<CustomerAdminService>();
builder.Services.AddScoped<StampService>();
builder.Services.AddScoped<RewardService>();

// งานรายวันตัดแต้มหมดอายุ ทำงานตอนตี 3 เวลาไทย และตรวจทันทีตอนสตาร์ตเผื่อเครื่องเคยปิดข้ามวัน
builder.Services.AddHostedService<StampExpiryJob>();

builder.Services.AddHttpClient<LineIdTokenVerifier>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<LinePushService>(http =>
{
    // push ช้าไม่ควรทำให้พนักงานรอ แต้มบันทึกเสร็จไปแล้ว
    http.Timeout = TimeSpan.FromSeconds(5);
});

// --- ล็อกอินพนักงานด้วย cookie + ล็อกเอาต์เมื่อ idle ---
var idleTimeout = TimeSpan.FromMinutes(
    builder.Configuration.GetValue("Staff:IdleTimeoutMinutes", 15));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "cs_staff";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/Staff/Login";
        options.AccessDeniedPath = "/Staff/Login";
        options.ExpireTimeSpan = idleTimeout;
        options.SlidingExpiration = true;

        // ถ้าอุปกรณ์ถูกปิดใช้งานหรือลบรหัสออก ให้ตัดเซสชันทิ้งทันที
        options.Events.OnValidatePrincipal = async context =>
        {
            var deviceClaim = context.Principal?.FindFirst(StaffContext.DeviceClaim)?.Value;
            if (!Guid.TryParse(deviceClaim, out var deviceId))
                return;

            var devices = context.HttpContext.RequestServices.GetRequiredService<DeviceService>();
            if (await devices.GetActiveAsync(deviceId) is null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };

        // API ควรได้ 401 ไม่ใช่ HTML ของหน้าล็อกอิน
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        // ล็อกอินแล้วแต่สิทธิ์ไม่ถึง ฝั่ง API ต้องได้ 403 ไม่ใช่ 302 ไปหน้าล็อกอิน
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthentication()
    .AddCookie(CustomerContext.AuthenticationScheme, options =>
    {
        options.Cookie.Name = CustomerContext.CookieName;
        options.Cookie.HttpOnly = true;
        // LIFF เปิดในเว็บวิวของ LINE จึงใช้ Lax ไม่ได้แคบเท่าฝั่งพนักงาน
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;

        // ฝั่งลูกค้าเป็น API ล้วน ไม่ต้อง redirect ไปหน้าไหน
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Staff", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("Manager", policy => policy.RequireRole(
        StaffRole.Manager.ToString(), StaffRole.Owner.ToString()))
    .AddPolicy("Owner", policy => policy.RequireRole(StaffRole.Owner.ToString()))
    // ลูกค้าต้องผ่าน scheme ของตัวเองเท่านั้น cookie พนักงานใช้แทนกันไม่ได้
    .AddPolicy("Customer", policy => policy
        .AddAuthenticationSchemes(CustomerContext.AuthenticationScheme)
        .RequireAuthenticatedUser());

// หน้าจอส่ง enum เป็นสตริง เช่น "Cash" หรือ "PromptPay" ให้อ่านง่ายและ log ตรงกับที่เห็น
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "cs_csrf";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // กันการเดา PIN แบบไล่สุ่มจากเครื่องเดียว
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // endpoint ที่รับ QR ของลูกค้า กันการยิง token สุ่มเพื่อเดา MAC
    options.AddPolicy("scan", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("liff-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // การแก้แต้มมี PIN คุมอยู่แล้ว แต่ยังกันการไล่เดา PIN ผู้จัดการ
    // ไม่ใช้ policy เดียวกับ login เพราะ 10 ครั้ง/นาทีแคบเกินไปเวลาหน้าร้านยุ่ง
    options.AddPolicy("correct", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // หน้าบัตรขอ token ใหม่ทุก 30 วินาที เผื่อเปิดหลายแท็บจึงให้พอสมควร
    options.AddPolicy("qr-token", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", "Staff");
    options.Conventions.AllowAnonymousToPage("/Staff/Login");
    options.Conventions.AllowAnonymousToPage("/Device/Register");
    options.Conventions.AllowAnonymousToPage("/Error");
    // หน้าฝั่งลูกค้าเปิดได้โดยไม่ต้องมี cookie พนักงาน ตัวหน้าเว็บจะล็อกอินด้วย LINE เอง
    options.Conventions.AllowAnonymousToFolder("/Liff");
    options.Conventions.AuthorizeFolder("/Admin", "Manager");
    options.Conventions.AuthorizePage("/DailyClose", "Manager");
});

var app = builder.Build();

// ตรวจค่า config ที่กระทบความปลอดภัยตอนบูต ดีกว่าไปพังกลางวันที่ร้านเปิด
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var lineOptions = app.Services.GetRequiredService<IOptions<LineOptions>>().Value;

    if (lineOptions.AllowDevMockLogin)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Line:AllowDevMockLogin เปิดได้เฉพาะ Development เท่านั้น " +
                "ถ้าเปิดบนเครื่องจริงใครก็ปลอมเป็นสมาชิกคนไหนก็ได้");
        }

        startupLogger.LogWarning(
            "เปิดโหมดล็อกอินลูกค้าแบบทดสอบไว้ (Line:AllowDevMockLogin) ห้ามใช้ค่านี้บนเครื่องจริง");
    }

    if (!lineOptions.IsLoginConfigured)
    {
        startupLogger.LogWarning(
            "ยังไม่ได้ตั้ง Line:LoginChannelId ลูกค้าจะล็อกอินผ่าน LIFF ไม่ได้");
    }

    if (!lineOptions.IsPushConfigured)
    {
        startupLogger.LogWarning(
            "ยังไม่ได้ตั้ง Line:MessagingChannelAccessToken ระบบจะไม่ push แจ้งแต้มให้ลูกค้า");
    }

    // ดึง secret ของ QR ตอนนี้เลย เพื่อให้ config ที่ขาดทำให้บูตไม่ผ่านทันที
    _ = app.Services.GetRequiredService<QrSecretProvider>();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// บังคับเปลี่ยน PIN ก่อนใช้งานอย่างอื่น สำหรับบัญชีที่ถูกรีเซ็ต PIN มา
app.Use(async (context, next) =>
{
    var staff = context.RequestServices.GetRequiredService<StaffContext>();
    var path = context.Request.Path;

    if (staff.IsAuthenticated && staff.MustChangePin &&
        !path.StartsWithSegments("/Staff") &&
        !path.StartsWithSegments("/api/staff") &&
        !path.StartsWithSegments("/Liff") &&
        !path.StartsWithSegments("/api/liff") &&
        !path.StartsWithSegments("/css") &&
        !path.StartsWithSegments("/js") &&
        !path.StartsWithSegments("/icons"))
    {
        context.Response.Redirect("/Staff/ChangePin");
        return;
    }

    await next();
});

var api = app.MapGroup("/api")
    .RequireAuthorization("Staff")
    .AddEndpointFilter<DomainErrorFilter>()
    .AddEndpointFilter<ApiAntiforgeryFilter>();

api.MapStaffEndpoints();
api.MapOrderEndpoints();
api.MapCustomerEndpoints();
api.MapStampEndpoints();
api.MapRewardEndpoints();
api.MapDailyCloseEndpoints();
api.MapAdminEndpoints();

// API ฝั่งลูกค้าอยู่คนละ group เพราะใช้ cookie scheme ของลูกค้า ไม่ใช่ของพนักงาน
var liffApi = app.MapGroup("/api/liff")
    .RequireAuthorization("Customer")
    .AddEndpointFilter<DomainErrorFilter>()
    .AddEndpointFilter<ApiAntiforgeryFilter>();

liffApi.MapLiffEndpoints();

app.MapWebManifest();
app.MapRazorPages();

// ย้าย migration และ seed ไว้หลัง build เพื่อให้แอปสตาร์ตได้แม้ DB ยังไม่พร้อม
await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        if (app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("ตรวจและอัปเดต schema ของฐานข้อมูลแล้ว");
        }

        await DbSeeder.SeedAsync(db, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "เชื่อมต่อหรือเตรียมฐานข้อมูลไม่สำเร็จ ตรวจ ConnectionStrings:Default และ PostgreSQL ว่าทำงานอยู่");
    }
}

// เปิดให้ test project อ้างถึง Program ได้ในอนาคต (integration test)
public partial class Program;
