using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

public class BangkokTimeTests
{
    [Fact]
    public void Zone_IsSevenHoursAheadOfUtc()
    {
        var offset = BangkokTime.Zone.GetUtcOffset(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(7), offset);
    }

    [Fact]
    public void BusinessDate_UsesThaiCalendarDay()
    {
        // 16 ต.ค. 17:30 UTC = 17 ต.ค. 00:30 เวลาไทย จึงต้องเป็นวันทำการถัดไป
        var late = new DateTime(2026, 10, 16, 17, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 10, 17), BangkokTime.BusinessDate(late));

        // 16 ต.ค. 16:59 UTC = 16 ต.ค. 23:59 เวลาไทย ยังเป็นวันเดิม
        var beforeMidnight = new DateTime(2026, 10, 16, 16, 59, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 10, 16), BangkokTime.BusinessDate(beforeMidnight));
    }

    [Fact]
    public void BusinessDate_MorningUtcStillSameThaiDay()
    {
        // 02:00 UTC = 09:00 เวลาไทย ช่วงเปิดร้าน
        var morning = new DateTime(2026, 10, 16, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 10, 16), BangkokTime.BusinessDate(morning));
        Assert.Equal(new TimeOnly(9, 0), BangkokTime.LocalTimeOf(morning));
    }

    [Fact]
    public void FormatTime_ShowsThaiLocalTime()
    {
        var utc = new DateTime(2026, 10, 16, 8, 5, 0, DateTimeKind.Utc);
        Assert.Equal("15:05", BangkokTime.FormatTime(utc));
        Assert.Equal("16/10/2026 15:05", BangkokTime.FormatDateTime(utc));
    }
}

public class MoneyTests
{
    [Theory]
    [InlineData(45.004, 45.00)]
    [InlineData(45.005, 45.01)]
    [InlineData(45.015, 45.02)]
    [InlineData(0.125, 0.13)]
    public void Round_UsesAwayFromZeroAtMidpoint(decimal input, decimal expected)
    {
        Assert.Equal(expected, Money.Round(input));
    }

    [Fact]
    public void Round_KeepsExactTwoDecimalValues()
    {
        Assert.Equal(123.45m, Money.Round(123.45m));
    }
}

public class PinPolicyTests
{
    [Theory]
    [InlineData("4071")]
    [InlineData("907213")]
    public void Validate_AcceptsReasonablePins(string pin)
    {
        Assert.Null(PinPolicy.Validate(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("1111")]
    [InlineData("1234")]
    [InlineData("4321")]
    [InlineData("987654")]
    public void Validate_RejectsWeakOrMalformedPins(string? pin)
    {
        Assert.NotNull(PinPolicy.Validate(pin));
    }
}

public class OrderStampCountTests
{
    private static OrderItem Line(string name, int quantity, bool earnsStamp = true,
        bool isReward = false) => new()
    {
        NameSnapshot = name,
        Quantity = quantity,
        UnitPrice = 50m,
        LineTotal = 50m * quantity,
        EarnsStampSnapshot = earnsStamp,
        IsReward = isReward
    };

    [Fact]
    public void EligibleStampCount_CountsQuantityOfStampEarningLines()
    {
        var order = new Order { Items = [Line("ลาเต้", 2), Line("อเมริกาโน่", 1)] };
        Assert.Equal(3, order.EligibleStampCount);
    }

    [Fact]
    public void EligibleStampCount_IgnoresRewardCupAndNonStampItems()
    {
        var order = new Order
        {
            Items =
            [
                Line("ลาเต้", 1),
                Line("ลาเต้ฟรี", 1, isReward: true),
                Line("น้ำเปล่า", 2, earnsStamp: false)
            ]
        };

        // แก้วที่แลกฟรีไม่ได้แต้ม และเมนูที่ตั้งว่าไม่นับแต้มก็ไม่นับ
        Assert.Equal(1, order.EligibleStampCount);
    }

    [Fact]
    public void EligibleStampCount_EmptyOrderIsZero()
    {
        Assert.Equal(0, new Order().EligibleStampCount);
    }
}

public class DailyCloseTotalsTests
{
    [Fact]
    public void Totals_SumAllThreePaymentChannels()
    {
        var close = new DailyClose
        {
            ExpectedCash = 1000m,
            ExpectedPromptPay = 500.50m,
            ExpectedPaoTang = 250.25m,
            ActualCash = 980m,
            ActualPromptPay = 500.50m,
            ActualPaoTang = 250.25m
        };

        Assert.Equal(1750.75m, close.ExpectedTotal);
        Assert.Equal(1730.75m, close.ActualTotal);
    }
}

public class DeviceCodeTests
{
    [Fact]
    public void GenerateCode_HasRequestedLengthAndUnambiguousAlphabet()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = DeviceService.GenerateCode();
            Assert.Equal(6, code.Length);
            // ตัด I O 0 1 ออกเพื่อไม่ให้พนักงานอ่านรหัสผิด
            Assert.DoesNotContain(code, c => c is 'I' or 'O' or '0' or '1');
            Assert.All(code, c => Assert.True(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void GenerateCode_ProducesDifferentCodes()
    {
        var codes = Enumerable.Range(0, 30).Select(_ => DeviceService.GenerateCode()).ToHashSet();
        Assert.True(codes.Count > 25, "รหัสลงทะเบียนต้องสุ่มจริง ไม่ซ้ำกันเกือบทั้งหมด");
    }
}
