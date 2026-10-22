using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeStamp.Tests;

public class ConsentTests
{
    private const string Current = "2026-10-16";

    [Fact]
    public void HasConsented_TrueOnlyForCurrentVersion()
    {
        var customer = new Customer
        {
            ConsentVersion = Current,
            ConsentAt = DateTime.UtcNow
        };

        Assert.True(customer.HasConsented(Current));
        // ข้อความยินยอมเวอร์ชันใหม่ ต้องขอความยินยอมใหม่ ไม่ใช้ของเก่าต่อ
        Assert.False(customer.HasConsented("2027-01-01"));
    }

    [Fact]
    public void HasConsented_FalseWhenNeverAccepted()
    {
        Assert.False(new Customer().HasConsented(Current));
    }

    [Fact]
    public void HasConsented_FalseWhenVersionSetButTimestampMissing()
    {
        // แถวที่ข้อมูลไม่ครบ ต้องถือว่ายังไม่ยินยอม
        var customer = new Customer { ConsentVersion = Current, ConsentAt = null };
        Assert.False(customer.HasConsented(Current));
    }
}

public class StampExpiryTests
{
    [Fact]
    public void ExpiryIsEndOfThaiDayNotMomentOfPurchase()
    {
        var businessDate = new DateOnly(2026, 10, 16);
        var expires = BangkokTime.EndOfLocalDayUtc(businessDate.AddMonths(12));

        // 17 ต.ค. 2027 00:00 ไทย = 16 ต.ค. 17:00 UTC ดังนั้นสิ้นวัน 16 ต.ค. 2027 ไทย
        // ต้องเป็น 16 ต.ค. 2027 16:59:59 UTC
        Assert.Equal(new DateTime(2027, 10, 16, 16, 59, 59, DateTimeKind.Utc), expires);
        Assert.Equal(new DateOnly(2027, 10, 16),
            DateOnly.FromDateTime(BangkokTime.ToLocal(expires)));
    }

    [Fact]
    public void ExpiryKeepsStampAliveThroughTheWholeLastDay()
    {
        var expires = BangkokTime.EndOfLocalDayUtc(new DateOnly(2026, 12, 31));
        var lastMinuteOfThatDay = new DateTime(2026, 12, 31, 16, 30, 0, DateTimeKind.Utc);

        var txn = new StampTransaction { ExpiresAt = expires };
        Assert.False(txn.IsExpired(lastMinuteOfThatDay));
        Assert.True(txn.IsExpired(expires.AddSeconds(1)));
    }

    [Fact]
    public void IsExpired_NeverTrueWhenExpiryIsNull()
    {
        var txn = new StampTransaction { ExpiresAt = null };
        Assert.False(txn.IsExpired(DateTime.UtcNow.AddYears(50)));
    }
}

public class EarnMessageTests
{
    [Fact]
    public void Message_FollowsReadmeFormat()
    {
        var text = LinePushService.BuildEarnMessage(2, 7, 10, "ร้านกาแฟมุมถนน");

        Assert.Contains("ได้รับ 2 แต้ม (รวม 7/10)", text);
        Assert.Contains("อีก 3 แต้มแลกฟรี 1 แก้ว", text);
        Assert.StartsWith("ร้านกาแฟมุมถนน", text);
    }

    [Fact]
    public void Message_TellsCustomerWhenRewardIsReady()
    {
        var text = LinePushService.BuildEarnMessage(1, 10, 10, "ร้านกาแฟ");

        Assert.Contains("(รวม 10/10)", text);
        Assert.Contains("แลกเครื่องดื่มฟรีได้ 1 แก้ว", text);
    }

    [Fact]
    public void Message_CountsMultipleRewards()
    {
        var text = LinePushService.BuildEarnMessage(2, 21, 10, "ร้านกาแฟ");
        Assert.Contains("แลกเครื่องดื่มฟรีได้ 2 แก้ว", text);
    }
}

public class LineIdTokenValidationTests
{
    private static LineIdTokenVerifier CreateVerifier(string channelId = "1234567890")
    {
        var options = Options.Create(new LineOptions { LoginChannelId = channelId });
        return new LineIdTokenVerifier(new HttpClient(), options,
            NullLogger<LineIdTokenVerifier>.Instance);
    }

    [Fact]
    public void Validate_ReturnsProfileWhenAudienceAndIssuerMatch()
    {
        var profile = CreateVerifier().Validate(new LineVerifyResponse
        {
            Sub = "U1234567890abcdef",
            Name = "สมชาย",
            Picture = "https://profile.line-scdn.net/abc",
            Aud = "1234567890",
            Iss = LineIdTokenVerifier.ExpectedIssuer
        });

        Assert.Equal("U1234567890abcdef", profile.UserId);
        Assert.Equal("สมชาย", profile.DisplayName);
    }

    [Fact]
    public void Validate_RejectsTokenFromAnotherChannel()
    {
        var verifier = CreateVerifier();

        Assert.Throws<DomainException>(() => verifier.Validate(new LineVerifyResponse
        {
            Sub = "U1234567890abcdef",
            Aud = "9999999999",
            Iss = LineIdTokenVerifier.ExpectedIssuer
        }));
    }

    [Fact]
    public void Validate_RejectsWrongIssuer()
    {
        var verifier = CreateVerifier();

        Assert.Throws<DomainException>(() => verifier.Validate(new LineVerifyResponse
        {
            Sub = "U1234567890abcdef",
            Aud = "1234567890",
            Iss = "https://evil.example.com"
        }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RejectsResponseWithoutSubject(string? sub)
    {
        var verifier = CreateVerifier();

        Assert.Throws<DomainException>(() => verifier.Validate(new LineVerifyResponse
        {
            Sub = sub,
            Aud = "1234567890"
        }));
    }

    [Fact]
    public void Validate_RejectsNullResponse()
    {
        Assert.Throws<DomainException>(() => CreateVerifier().Validate(null));
    }

    [Fact]
    public async Task VerifyAsync_RejectsEmptyToken()
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateVerifier().VerifyAsync(""));
    }

    [Fact]
    public async Task VerifyAsync_FailsClearlyWhenChannelNotConfigured()
    {
        var verifier = CreateVerifier(channelId: "");

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            verifier.VerifyAsync("some-id-token"));

        Assert.Contains("LoginChannelId", error.Message);
    }
}

public class StampLedgerMathTests
{
    private static StampTransaction Earn(int quantity, long orderId) => new()
    {
        Type = StampTransactionType.Earn,
        Quantity = quantity,
        OrderId = orderId
    };

    [Fact]
    public void LedgerSum_EqualsBalanceAfterEarnAndVoid()
    {
        // ledger เป็น append-only การยกเลิกคือการเพิ่มรายการ Void ไม่ใช่ลบ Earn
        var ledger = new List<StampTransaction>
        {
            Earn(3, 1),
            Earn(2, 2),
            new() { Type = StampTransactionType.Void, Quantity = -2, OrderId = 2 }
        };

        Assert.Equal(3, ledger.Sum(t => t.Quantity));
        Assert.Equal(2, ledger.Count(t => t.Type == StampTransactionType.Earn));
    }

    [Fact]
    public void RewardMathMatchesCardDisplay()
    {
        const int perReward = 10;

        Assert.Equal(0, 7 / perReward);
        Assert.Equal(3, perReward - 7 % perReward);
        Assert.Equal(2, 21 / perReward);
        Assert.Equal(9, perReward - 21 % perReward);
    }
}
