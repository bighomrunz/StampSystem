using CoffeeStamp.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace CoffeeStamp.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ShopSettings> ShopSettings => Set<ShopSettings>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<DailyClose> DailyCloses => Set<DailyClose>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();
    public DbSet<StampTransaction> StampTransactions => Set<StampTransaction>();
    public DbSet<StampAllocation> StampAllocations => Set<StampAllocation>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // เงินทุกช่องใช้ decimal(10,2) เท่านั้น ห้ามใช้ double/float
        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetColumnType("numeric(10,2)");
        }

        b.Entity<ShopSettings>(e =>
        {
            e.ToTable("shop_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ShopName).HasMaxLength(100).IsRequired();
            e.Property(x => x.PromptPayId).HasMaxLength(20);
            e.Property(x => x.PromptPayMerchantName).HasMaxLength(50);
            e.Property(x => x.PromptPayIdType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RewardMode).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Staff>(e =>
        {
            e.ToTable("staff");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.PinHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.IsActive);
        });

        b.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.RegistrationCode).HasMaxLength(20);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasIndex(x => x.RegistrationCode);
        });

        b.Entity<MenuItem>(e =>
        {
            e.ToTable("menu_items");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasMaxLength(50);
            e.HasIndex(x => new { x.IsActive, x.SortOrder });
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.Property(x => x.OrderNo).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.OrderNo).IsUnique();
            e.HasIndex(x => new { x.BusinessDate, x.Status });
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.VoidReason).HasMaxLength(300);

            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne(x => x.Order).HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CustomerId);
        });

        b.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.Property(x => x.NameSnapshot).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.MenuItem).WithMany().HasForeignKey(x => x.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MenuItemId);
        });

        b.Entity<DailyClose>(e =>
        {
            e.ToTable("daily_closes");
            e.HasKey(x => x.BusinessDate);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.ClosedByStaff).WithMany().HasForeignKey(x => x.ClosedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");

            // สมาชิกที่พนักงานสร้างที่เคาน์เตอร์ยังไม่มี LINE จึงเป็น null ได้
            // แต่ถ้ามีค่าต้องไม่ซ้ำ ใช้ partial unique index เพราะ null ซ้ำกันได้หลายแถว
            e.Property(x => x.LineUserId).HasMaxLength(60);
            e.HasIndex(x => x.LineUserId)
                .IsUnique()
                .HasFilter("line_user_id IS NOT NULL")
                .HasDatabaseName("ux_customers_line_user_id");

            e.HasIndex(x => x.PublicId).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PictureUrl).HasMaxLength(500);
            e.Property(x => x.ConsentVersion).HasMaxLength(40);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasIndex(x => x.Phone);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ConsentSource).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.CreatedByStaff).WithMany().HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Balance).WithOne(x => x.Customer)
                .HasForeignKey<CustomerBalance>(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CustomerBalance>(e =>
        {
            e.ToTable("customer_balances");
            e.HasKey(x => x.CustomerId);
            e.Property(x => x.CustomerId).ValueGeneratedNever();
            // กันการออกแต้มหรือแลกแต้มซ้อนกันสองรายการพร้อมกัน ถ้ายอดถูกแก้ไปแล้วจะ throw
            // Npgsql 10 ไม่มี UseXminAsConcurrencyToken แล้ว ใช้วิธี map shadow property
            // ชนิด uint แบบ OnAddOrUpdate ไปที่คอลัมน์ระบบ xmin ซึ่ง convention ของ Npgsql รองรับ
            // คอลัมน์ระบบจะไม่ถูกสร้างใน migration
            e.Property<uint>("xmin")
                .HasColumnName("xmin")
                .IsRowVersion();
        });

        b.Entity<StampTransaction>(e =>
        {
            e.ToTable("stamp_transactions");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // หนึ่งออเดอร์ออกแต้มได้ครั้งเดียว บังคับที่ระดับฐานข้อมูล ไม่ใช่แค่เช็กในโค้ด
            e.HasIndex(x => x.OrderId)
                .IsUnique()
                .HasFilter("type = 'Earn'")
                .HasDatabaseName("ux_stamp_transactions_order_earn");

            e.Property(x => x.AdjustKind).HasMaxLength(30);

            // ย้ายบัตรกระดาษได้ครั้งเดียวต่อลูกค้าหนึ่งคนตลอดกาล
            // บังคับที่ฐานข้อมูล ไม่ใช่แค่เช็กในโค้ด เพราะนี่คือจุดที่โกงง่ายที่สุด
            e.HasIndex(x => x.CustomerId)
                .IsUnique()
                .HasFilter($"adjust_kind = '{AdjustKind.CardMigration}'")
                .HasDatabaseName("ux_stamp_transactions_card_migration");

            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            // ใช้ตอนหา Earn ที่ยังไม่หมดอายุแบบ FIFO ใน Phase 3
            e.HasIndex(x => new { x.CustomerId, x.Type, x.ExpiresAt });
        });

        b.Entity<StampAllocation>(e =>
        {
            e.ToTable("stamp_allocations");

            e.HasOne(x => x.ConsumingTransaction).WithMany()
                .HasForeignKey(x => x.ConsumingTransactionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceTransaction).WithMany()
                .HasForeignKey(x => x.SourceTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // ใช้ตอนคำนวณว่าก้อนต้นทางเหลือเท่าไร ซึ่งทำทุกครั้งที่แลกและตอน job หมดอายุ
            e.HasIndex(x => x.SourceTransactionId);
            e.HasIndex(x => x.ConsumingTransactionId);
        });

        b.Entity<Redemption>(e =>
        {
            e.ToTable("redemptions");
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MenuNameSnapshot).HasMaxLength(100).IsRequired();
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MenuItem).WithMany().HasForeignKey(x => x.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // หนึ่งออเดอร์แลกรางวัลได้ครั้งเดียว กันกดซ้ำแบบเดียวกับการออกแต้ม
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OrderId).IsUnique();

            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.Property(x => x.Action).HasMaxLength(60).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(60);
            e.Property(x => x.IpAddress).HasMaxLength(60);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });
    }
}
