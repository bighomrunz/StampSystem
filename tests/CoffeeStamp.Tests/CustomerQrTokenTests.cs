using System.Security.Cryptography;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

public class CustomerQrTokenTests
{
    private static readonly byte[] Secret = Convert.FromBase64String(
        "c2VjcmV0LWZvci10ZXN0cy1vbmx5LTMyLWJ5dGVzLWxvbmch");

    private static readonly DateTime Now = new(2026, 10, 16, 7, 30, 0, DateTimeKind.Utc);
    private static readonly Guid PublicId = Guid.Parse("8f14e45f-ea6a-4c0f-9f4b-6f1c2d3e4a5b");

    [Fact]
    public void Issue_ThenVerify_ReturnsSameCustomerAndStep()
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var payload = CustomerQrToken.Verify(Secret, token, Now);

        Assert.Equal(PublicId, payload.PublicId);
        Assert.Equal(CustomerQrToken.StepOf(Now), payload.Step);
    }

    [Fact]
    public void Issue_TokenHasExpectedShape()
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var parts = token.Split('.');

        Assert.Equal(4, parts.Length);
        Assert.Equal("CS1", parts[0]);
        // ต้องไม่มีอักขระที่ทำให้ QR ยาวขึ้นหรือ encode เพี้ยน
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void StepOf_ChangesEveryThirtySeconds()
    {
        var step = CustomerQrToken.StepOf(Now);

        Assert.Equal(step, CustomerQrToken.StepOf(Now.AddSeconds(29)));
        Assert.Equal(step + 1, CustomerQrToken.StepOf(Now.AddSeconds(30)));
        Assert.Equal(step + 2, CustomerQrToken.StepOf(Now.AddSeconds(75)));
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(45)]
    public void Verify_AcceptsOneStepOfDriftEitherWay(int secondsLater)
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var payload = CustomerQrToken.Verify(Secret, token, Now.AddSeconds(secondsLater));
        Assert.Equal(PublicId, payload.PublicId);
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(-61)]
    [InlineData(61)]
    [InlineData(600)]
    public void Verify_RejectsTokenOutsideWindow(int secondsLater)
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);

        var error = Assert.Throws<DomainException>(() =>
            CustomerQrToken.Verify(Secret, token, Now.AddSeconds(secondsLater)));

        Assert.Contains("หมดอายุ", error.Message);
    }

    [Fact]
    public void Verify_RejectsTokenSignedWithAnotherSecret()
    {
        var otherSecret = RandomNumberGenerator.GetBytes(32);
        var token = CustomerQrToken.Issue(otherSecret, PublicId, Now);

        Assert.Throws<DomainException>(() => CustomerQrToken.Verify(Secret, token, Now));
    }

    [Fact]
    public void Verify_RejectsSwappedCustomerId()
    {
        // ผู้โจมตีรู้ publicId ของเหยื่อ แต่ไม่มี secret จึงแก้ส่วน id แล้วใช้ MAC เดิมไม่ได้
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var parts = token.Split('.');
        var victim = CustomerQrToken.Issue(Secret, Guid.NewGuid(), Now).Split('.')[1];
        var forged = string.Join('.', parts[0], victim, parts[2], parts[3]);

        Assert.Throws<DomainException>(() => CustomerQrToken.Verify(Secret, forged, Now));
    }

    [Fact]
    public void Verify_RejectsStepChangedToExtendLifetime()
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var parts = token.Split('.');
        var futureStep = (long.Parse(parts[2]) + 10).ToString();
        var forged = string.Join('.', parts[0], parts[1], futureStep, parts[3]);

        Assert.Throws<DomainException>(() => CustomerQrToken.Verify(Secret, forged, Now));
    }

    [Fact]
    public void Verify_RejectsTamperedMac()
    {
        var token = CustomerQrToken.Issue(Secret, PublicId, Now);
        var parts = token.Split('.');
        var flipped = parts[3][0] == 'A' ? 'B' + parts[3][1..] : 'A' + parts[3][1..];
        var forged = string.Join('.', parts[0], parts[1], parts[2], flipped);

        Assert.Throws<DomainException>(() => CustomerQrToken.Verify(Secret, forged, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("CS1.only.three")]
    [InlineData("CS2.aaaa.1.bbbb")]
    [InlineData("CS1.!!!!.1.bbbb")]
    [InlineData("CS1.aaaa.notanumber.bbbb")]
    public void Verify_RejectsMalformedTokens(string? token)
    {
        Assert.Throws<DomainException>(() => CustomerQrToken.Verify(Secret, token, Now));
    }

    [Fact]
    public void Issue_DifferentStepsProduceDifferentTokens()
    {
        var first = CustomerQrToken.Issue(Secret, PublicId, Now);
        var second = CustomerQrToken.Issue(Secret, PublicId, Now.AddSeconds(30));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Issue_SameStepIsStableSoScreenDoesNotFlicker()
    {
        var first = CustomerQrToken.Issue(Secret, PublicId, Now);
        var second = CustomerQrToken.Issue(Secret, PublicId, Now.AddSeconds(5));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(10, 20)]
    [InlineData(29, 1)]
    public void SecondsUntilNextStep_CountsDownWithinWindow(int offsetSeconds, int expected)
    {
        // Now ตรงขอบ step พอดี (07:30:00 = unix ลงตัวด้วย 30)
        var at = Now.AddSeconds(offsetSeconds);
        Assert.Equal(expected, CustomerQrToken.SecondsUntilNextStep(at));
    }
}
