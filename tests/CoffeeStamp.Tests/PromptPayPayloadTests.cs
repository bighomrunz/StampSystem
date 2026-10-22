using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

public class Crc16CcittTests
{
    [Fact]
    public void Compute_KnownCheckValue_Matches()
    {
        // ค่าตรวจมาตรฐานของ CRC-16/CCITT-FALSE คือ 0x29B1 เมื่อป้อน "123456789"
        Assert.Equal(0x29B1, Crc16Ccitt.Compute("123456789"));
    }

    [Fact]
    public void ComputeHex_PadsToFourUppercaseHexDigits()
    {
        Assert.Equal("29B1", Crc16Ccitt.ComputeHex("123456789"));
        Assert.Matches("^[0-9A-F]{4}$", Crc16Ccitt.ComputeHex("0002010102"));
    }

    [Fact]
    public void Compute_EmptyInput_ReturnsInitialValue()
    {
        Assert.Equal(0xFFFF, Crc16Ccitt.Compute(""));
    }
}

public class PromptPayPayloadTests
{
    // payload ชุดนี้ cross-check กับ implementation อีกตัวที่เขียนแยกกัน (ภาษา Python)
    // ถ้าแก้ตัวสร้าง payload แล้วค่าเหล่านี้เปลี่ยน ต้องตรวจกับสเปก EMVCo ก่อนแก้ค่าในเทส
    private const string MobileWithAmount =
        "00020101021229370016A0000006770101110113006681234567853037645406100.005802TH6304F142";

    private const string MobileNoAmount =
        "00020101021129370016A0000006770101110113006681234567853037645802TH6304823E";

    private const string OtherMobileWithDecimals =
        "00020101021229370016A000000677010111011300668987654325303764540545.505802TH63046854";

    [Fact]
    public void Build_MobileWithAmount_MatchesKnownPayload()
    {
        var payload = PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, 100m);
        Assert.Equal(MobileWithAmount, payload);
    }

    [Fact]
    public void Build_MobileWithoutAmount_MatchesKnownPayload()
    {
        var payload = PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, null);
        Assert.Equal(MobileNoAmount, payload);
    }

    [Fact]
    public void Build_AmountWithSatang_KeepsTwoDecimals()
    {
        var payload = PromptPayPayload.Build("089-876-5432", PromptPayIdType.Mobile, 45.5m);
        Assert.Equal(OtherMobileWithDecimals, payload);
        Assert.Contains("540545.50", payload);
    }

    [Fact]
    public void Build_WithAmount_UsesDynamicPointOfInitiation()
    {
        var dynamicQr = PromptPayPayload.ReadTags(
            PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, 60m));
        var staticQr = PromptPayPayload.ReadTags(
            PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, null));

        Assert.Equal("12", dynamicQr["01"]);
        Assert.Equal("11", staticQr["01"]);
    }

    [Fact]
    public void Build_SetsThaiCurrencyAndCountry()
    {
        var tags = PromptPayPayload.ReadTags(
            PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, 60m));

        Assert.Equal("01", tags["00"]);
        Assert.Equal("764", tags["53"]);
        Assert.Equal("TH", tags["58"]);
        Assert.Equal("60.00", tags["54"]);
    }

    [Fact]
    public void Build_NationalId_UsesSubTag02()
    {
        var payload = PromptPayPayload.Build("1-2345-67890-12-3", PromptPayIdType.NationalId, 25m);
        var merchantAccount = PromptPayPayload.ReadTags(payload)["29"];

        Assert.Contains(PromptPayPayload.AnyIdApplicationId, merchantAccount);
        Assert.Contains("0213" + "1234567890123", merchantAccount);
        Assert.True(PromptPayPayload.IsValid(payload));
    }

    [Fact]
    public void Build_EWallet_UsesSubTag03()
    {
        var payload = PromptPayPayload.Build("123456789012345", PromptPayIdType.EWalletId, 25m);
        Assert.Contains("0315" + "123456789012345", PromptPayPayload.ReadTags(payload)["29"]);
    }

    [Theory]
    [InlineData("0812345678", "0066812345678")]
    [InlineData("081-234-5678", "0066812345678")]
    [InlineData("66812345678", "0066812345678")]
    [InlineData("0066812345678", "0066812345678")]
    [InlineData("812345678", "0066812345678")]
    public void NormalizeMobile_AcceptsCommonFormats(string input, string expected)
    {
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        Assert.Equal(expected, PromptPayPayload.NormalizeMobile(digits));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("08123456789012")]
    public void NormalizeMobile_RejectsWrongLength(string digits)
    {
        Assert.Throws<ArgumentException>(() => PromptPayPayload.NormalizeMobile(digits));
    }

    [Fact]
    public void NormalizeTarget_NationalIdMustBe13Digits()
    {
        Assert.Throws<ArgumentException>(() =>
            PromptPayPayload.NormalizeTarget("123456", PromptPayIdType.NationalId));
    }

    [Fact]
    public void Build_RejectsMissingPromptPayId()
    {
        Assert.Throws<ArgumentException>(() =>
            PromptPayPayload.Build("", PromptPayIdType.Mobile, 50m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10_000_000)]
    public void Build_RejectsAmountOutOfRange(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, amount));
    }

    [Fact]
    public void IsValid_DetectsTamperedPayload()
    {
        var payload = PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, 100m);
        Assert.True(PromptPayPayload.IsValid(payload));

        // เปลี่ยนยอดจาก 100.00 เป็น 900.00 แต่ไม่แก้ CRC ต้องตรวจจับได้
        var tampered = payload.Replace("5406100.00", "5406900.00");
        Assert.NotEqual(payload, tampered);
        Assert.False(PromptPayPayload.IsValid(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0002")]
    [InlineData("00020101021158")]
    public void IsValid_RejectsGarbage(string? payload)
    {
        Assert.False(PromptPayPayload.IsValid(payload));
    }

    [Fact]
    public void Build_MerchantName_OnlyAsciiIsIncluded()
    {
        var ascii = PromptPayPayload.ReadTags(PromptPayPayload.Build(
            "0812345678", PromptPayIdType.Mobile, 50m, "Coffee Corner"));

        var thai = PromptPayPayload.ReadTags(PromptPayPayload.Build(
            "0812345678", PromptPayIdType.Mobile, 50m, "ร้านกาแฟมุมถนน"));

        Assert.Equal("Coffee Corner", ascii["59"]);
        Assert.False(thai.ContainsKey("59"));
    }

    [Fact]
    public void Build_MerchantName_TruncatedTo25Chars()
    {
        var tags = PromptPayPayload.ReadTags(PromptPayPayload.Build(
            "0812345678", PromptPayIdType.Mobile, 50m,
            "Coffee Corner Bangkok Main Branch Sukhumvit"));

        Assert.True(tags["59"].Length <= 25);
    }

    [Fact]
    public void ReadTags_LengthPrefixesAreConsistent()
    {
        var payload = PromptPayPayload.Build("0812345678", PromptPayIdType.Mobile, 100m);
        var tags = PromptPayPayload.ReadTags(payload);

        // ความยาวที่ประกาศของทุก tag รวมกับหัว 4 ตัวอักษร ต้องเท่ากับความยาว payload ทั้งหมด
        var expectedLength = tags.Sum(t => 4 + t.Value.Length);
        Assert.Equal(payload.Length, expectedLength);
    }
}
