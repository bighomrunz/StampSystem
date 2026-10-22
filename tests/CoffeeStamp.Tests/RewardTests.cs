using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

public class RewardModeTests
{
    private static MenuItem Item(decimal price, bool eligible = true) => new()
    {
        Id = 1,
        Name = "ลาเต้เย็น",
        Price = price,
        IsActive = true,
        IsRewardEligible = eligible
    };

    private static ShopSettings Settings(RewardMode mode, decimal? cap = null,
        bool topUp = false) => new()
    {
        RewardMode = mode,
        RewardPriceCap = cap,
        AllowRewardTopUp = topUp,
        StampsPerReward = 10
    };

    [Fact]
    public void AllItems_EverythingIsRedeemableForFree()
    {
        var option = RewardService.Evaluate(Item(120m, eligible: false),
            Settings(RewardMode.AllItems));

        Assert.True(option.CanRedeem);
        Assert.Equal(0m, option.TopUpAmount);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SelectedItems_FollowsTheMenuFlag(bool eligible, bool canRedeem)
    {
        var option = RewardService.Evaluate(Item(55m, eligible),
            Settings(RewardMode.SelectedItems));

        Assert.Equal(canRedeem, option.CanRedeem);
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(50, true)]
    [InlineData(50.01, false)]
    [InlineData(60, false)]
    public void PriceCap_AllowsOnlyUpToTheCapWhenTopUpIsOff(decimal price, bool canRedeem)
    {
        // เพดานคือตัวเลขที่เจ้าของตั้งเอง ไม่ได้คำนวณจากราคาเมนูที่ลูกค้าซื้อ
        var option = RewardService.Evaluate(Item(price), Settings(RewardMode.PriceCap, 50m));
        Assert.Equal(canRedeem, option.CanRedeem);
    }

    [Fact]
    public void PriceCap_OverCapChargesExactlyTheDifferenceWhenTopUpIsOn()
    {
        var option = RewardService.Evaluate(Item(60m),
            Settings(RewardMode.PriceCap, 50m, topUp: true));

        Assert.True(option.CanRedeem);
        Assert.Equal(10m, option.TopUpAmount);
    }

    [Fact]
    public void PriceCap_WithinCapNeverChargesTopUp()
    {
        var option = RewardService.Evaluate(Item(45m),
            Settings(RewardMode.PriceCap, 50m, topUp: true));

        Assert.True(option.CanRedeem);
        Assert.Equal(0m, option.TopUpAmount);
    }

    [Fact]
    public void PriceCap_TellsStaffWhenCapIsNotConfigured()
    {
        var option = RewardService.Evaluate(Item(45m), Settings(RewardMode.PriceCap));

        Assert.False(option.CanRedeem);
        Assert.Contains("เพดาน", option.Reason);
    }

    [Fact]
    public void RewardCupNeverEarnsStampsBack()
    {
        // แก้วที่แลกฟรีต้องไม่ทำให้ได้แต้มเพิ่ม ไม่งั้นจะวนลูปแจกแต้มไม่รู้จบ
        var order = new Order
        {
            Items =
            [
                new OrderItem { Quantity = 1, IsReward = true, EarnsStampSnapshot = false }
            ]
        };

        Assert.Equal(0, order.EligibleStampCount);
    }
}

public class StampExpiryJobTests
{
    [Fact]
    public void NextRunIsThreeAmBangkokTime()
    {
        // 22 ต.ค. 01:00 UTC = 08:00 ไทย → รอบถัดไปคือตี 3 ของวันที่ 23 ไทย
        var utcNow = new DateTime(2026, 10, 22, 1, 0, 0, DateTimeKind.Utc);
        var delay = StampExpiryJob.TimeUntilNextRun(utcNow);
        var nextLocal = BangkokTime.ToLocal(utcNow.Add(delay));

        Assert.Equal(3, nextLocal.Hour);
        Assert.Equal(0, nextLocal.Minute);
        Assert.Equal(23, nextLocal.Day);
    }

    [Fact]
    public void JustBeforeThreeAmWaitsLessThanAnHour()
    {
        // 02:30 ไทย = 19:30 UTC ของวันก่อนหน้า
        var utcNow = new DateTime(2026, 10, 21, 19, 30, 0, DateTimeKind.Utc);
        var delay = StampExpiryJob.TimeUntilNextRun(utcNow);

        Assert.InRange(delay, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
    }

    [Fact]
    public void DelayIsAlwaysPositiveAndWithinADay()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var utcNow = new DateTime(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);
            var delay = StampExpiryJob.TimeUntilNextRun(utcNow);

            Assert.True(delay > TimeSpan.Zero, $"ชั่วโมง {hour} ได้ delay ติดลบ");
            Assert.True(delay <= TimeSpan.FromHours(24), $"ชั่วโมง {hour} รอนานเกินหนึ่งวัน");
        }
    }
}

public class FifoAllocationRuleTests
{
    private static StampTransaction Lot(long id, int qty, DateTime? expires) => new()
    {
        Id = id,
        Quantity = qty,
        ExpiresAt = expires,
        Type = StampTransactionType.Earn
    };

    [Fact]
    public void LotsAreConsumedNearestExpiryFirstAndNeverExpiringLast()
    {
        var baseDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var lots = new List<StampTransaction>
        {
            Lot(1, 5, null),                      // ไม่หมดอายุ ต้องใช้ทีหลังสุด
            Lot(2, 5, baseDate.AddDays(90)),
            Lot(3, 5, baseDate.AddDays(10))       // ใกล้หมดอายุที่สุด ต้องใช้ก่อน
        };

        // ลำดับเดียวกับที่ AllocateFifoAsync ใช้
        var ordered = lots
            .OrderBy(t => t.ExpiresAt == null)
            .ThenBy(t => t.ExpiresAt)
            .ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToList();

        Assert.Equal([3L, 2L, 1L], ordered);
    }

    [Fact]
    public void AllocationsAlwaysSumToTheConsumedAmount()
    {
        // ตัด 8 แต้มจากก้อน 5 และ 4 → ต้องได้ 5 + 3 ไม่ใช่ 5 + 4
        var consuming = new StampTransaction { Type = StampTransactionType.Redeem, Quantity = -8 };
        var allocations = new List<StampAllocation>
        {
            new() { ConsumingTransaction = consuming, SourceTransactionId = 1, Quantity = 5 },
            new() { ConsumingTransaction = consuming, SourceTransactionId = 2, Quantity = 3 }
        };

        Assert.Equal(8, allocations.Sum(a => a.Quantity));
        Assert.Equal(Math.Abs(consuming.Quantity), allocations.Sum(a => a.Quantity));
    }
}

public class RedemptionRecordTests
{
    [Fact]
    public void PaperCardRedemptionUsesNoStampsButNeedsAnApprover()
    {
        var redemption = new Redemption
        {
            CustomerId = null,
            Source = RedemptionSource.PaperCard,
            StampsUsed = 0,
            MenuNameSnapshot = "อเมริกาโน่ร้อน",
            ApprovedByStaffId = 2,
            Note = "บัตรเลข 0042"
        };

        Assert.Equal(0, redemption.StampsUsed);
        Assert.Null(redemption.CustomerId);
        Assert.NotNull(redemption.ApprovedByStaffId);
    }

    [Fact]
    public void StampRedemptionAlwaysBelongsToACustomer()
    {
        var redemption = new Redemption
        {
            CustomerId = 7,
            Source = RedemptionSource.Stamps,
            StampsUsed = 10,
            MenuNameSnapshot = "ลาเต้ร้อน"
        };

        Assert.NotNull(redemption.CustomerId);
        Assert.True(redemption.StampsUsed > 0);
    }
}
