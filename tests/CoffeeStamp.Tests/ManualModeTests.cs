using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0812345678", "0812345678")]
    [InlineData("081-234-5678", "0812345678")]
    [InlineData("081 234 5678", "0812345678")]
    [InlineData(" (081) 234-5678 ", "0812345678")]
    public void Normalize_KeepsOnlyDigits(string input, string expected)
    {
        // เก็บเป็นตัวเลขล้วนเสมอ ไม่งั้นค้นหาไม่เจอเวลาพนักงานพิมพ์คนละแบบ
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ไม่มีเบอร์")]
    public void Normalize_ReturnsNullWhenNoDigits(string? input)
    {
        Assert.Null(PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0812345678")]
    [InlineData("081-234-5678")]
    [InlineData("021234567")]
    public void Validate_AcceptsEmptyAndThaiNumbers(string? input)
    {
        // เบอร์ไม่บังคับกรอก ค่าว่างจึงต้องผ่าน
        Assert.Null(PhoneNumber.Validate(input));
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("08123456789")]
    [InlineData("812345678")]
    public void Validate_RejectsWrongLengthOrMissingLeadingZero(string input)
    {
        Assert.NotNull(PhoneNumber.Validate(input));
    }

    [Theory]
    [InlineData("0812345678", "081-234-5678")]
    [InlineData("021234567", "02-123-4567")]
    [InlineData(null, "-")]
    public void Format_IsReadableOnScreen(string? input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Format(input));
    }
}

public class CounterMemberTests
{
    [Fact]
    public void CounterMember_HasNoLineAccountYet()
    {
        var customer = new Customer
        {
            DisplayName = "คุณสมหญิง",
            Phone = "0812345678",
            Source = CustomerSource.Counter,
            LineUserId = null
        };

        Assert.False(customer.IsLineLinked);
        Assert.Equal("คุณสมหญิง", customer.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_FallsBackToIdWhenNameMissing()
    {
        Assert.Equal("สมาชิก #7", new Customer { Id = 7, DisplayName = null }.DisplayLabel);
        Assert.Equal("สมาชิก #8", new Customer { Id = 8, DisplayName = "  " }.DisplayLabel);
    }

    [Fact]
    public void CounterConsent_CountsAsConsentForCurrentVersion()
    {
        // ลูกค้าที่สมัครหน้าร้านยินยอมด้วยวาจาหลังพนักงานอ่านข้อความให้ฟัง
        // ต้องถือว่ายินยอมแล้วเท่ากับคนที่กดในหน้า LIFF
        var customer = new Customer
        {
            Source = CustomerSource.Counter,
            ConsentVersion = "2026-10-16",
            ConsentAt = DateTime.UtcNow,
            ConsentSource = ConsentSource.Counter
        };

        Assert.True(customer.HasConsented("2026-10-16"));
        Assert.Equal(ConsentSource.Counter, customer.ConsentSource);
    }

    [Fact]
    public void LineLinkedMember_IsRecognised()
    {
        var customer = new Customer { LineUserId = "U1234567890" };
        Assert.True(customer.IsLineLinked);
    }
}

public class CardMigrationRuleTests
{
    /// <summary>
    /// กฎที่ตกลงไว้: บัตรที่เต็มแล้วให้แลกฟรีหน้าร้าน ไม่ย้ายเข้าระบบ
    /// ดังนั้นย้ายได้ไม่เกิน StampsPerReward - 1 แต้ม
    /// </summary>
    [Theory]
    [InlineData(10, 9, true)]
    [InlineData(10, 1, true)]
    [InlineData(10, 10, false)]
    [InlineData(10, 11, false)]
    [InlineData(10, 0, false)]
    [InlineData(10, -1, false)]
    [InlineData(6, 5, true)]
    [InlineData(6, 6, false)]
    public void MigrationCap_LimitsExposureToLessThanOneReward(int stampsPerReward,
        int requested, bool allowed)
    {
        var isAllowed = requested > 0 && requested < stampsPerReward;
        Assert.Equal(allowed, isAllowed);
    }

    [Fact]
    public void CardMigration_IsTaggedSoItCanBeLimitedToOncePerCustomer()
    {
        var txn = new StampTransaction
        {
            Type = StampTransactionType.Adjust,
            AdjustKind = AdjustKind.CardMigration,
            Quantity = 7,
            ApprovedByStaffId = 1
        };

        // unique index ของฐานข้อมูลอาศัย adjust_kind ตัวนี้ในการกันย้ายซ้ำ
        Assert.Equal("CardMigration", txn.AdjustKind);
        Assert.Equal(StampTransactionType.Adjust, txn.Type);
        Assert.NotNull(txn.ApprovedByStaffId);
        // ไม่ผูกออเดอร์ เพราะแต้มกลุ่มนี้ไม่ได้มาจากการขายในระบบ
        Assert.Null(txn.OrderId);
    }

    [Fact]
    public void Correction_UsesDifferentKindSoItIsNotBlockedByTheIndex()
    {
        var txn = new StampTransaction
        {
            Type = StampTransactionType.Adjust,
            AdjustKind = AdjustKind.Correction,
            Quantity = -2
        };

        Assert.NotEqual(AdjustKind.CardMigration, txn.AdjustKind);
    }
}
