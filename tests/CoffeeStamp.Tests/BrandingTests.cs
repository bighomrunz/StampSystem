using CoffeeStamp.Web.Api;
using CoffeeStamp.Web.Domain;

namespace CoffeeStamp.Tests;

/// <summary>
/// ชื่อร้านต้องมีแหล่งเดียวคือ ShopSettings.ShopName แล้วไหลไปทุกหน้า
/// ที่นี่เทสเฉพาะส่วนที่เป็น logic ล้วน ส่วนการไหลไปหน้าต่าง ๆ ทดสอบด้วยการรันจริง
/// </summary>
public class ShopNameTests
{
    [Fact]
    public void DefaultNameIsUsedWhenNothingConfigured()
    {
        Assert.Equal("MInDCoffee", ShopSettings.DefaultShopName);
        Assert.Equal(ShopSettings.DefaultShopName, new ShopSettings().ShopName);
    }

    [Theory]
    [InlineData("MInDCoffee", "MInDCoffee")]
    [InlineData("  MInDCoffee  ", "MInDCoffee")]
    [InlineData("Coffee", "Coffee")]
    public void ShortNameKeepsNamesThatAlreadyFit(string input, string expected)
    {
        Assert.Equal(expected, BrandingEndpoints.ShortName(input));
    }

    [Fact]
    public void ShortNameTrimsLongLatinNames()
    {
        var shortName = BrandingEndpoints.ShortName("MInDCoffee Specialty Bangkok");
        Assert.Equal(12, shortName.Length);
        Assert.StartsWith("MInDCoffee", shortName);
    }

    [Fact]
    public void ShortNameNeverSplitsThaiCharacterClusters()
    {
        // "ร้านกาแฟมายด์คอฟฟี่" — ตัวอักษรไทยหลายตัวมีสระและวรรณยุกต์ต่อท้าย
        // ถ้าตัดด้วย string[..12] ดิบ ๆ จะได้สระลอยท้ายชื่อ
        const string thai = "ร้านกาแฟมายด์คอฟฟี่";
        var shortName = BrandingEndpoints.ShortName(thai);

        Assert.True(shortName.Length <= thai.Length);
        // ตัวสุดท้ายต้องไม่ใช่สระหรือวรรณยุกต์ที่ลอยอยู่เดี่ยว ๆ
        var last = shortName[^1];
        Assert.False(char.GetUnicodeCategory(last)
            is System.Globalization.UnicodeCategory.NonSpacingMark,
            $"ชื่อย่อ '{shortName}' ลงท้ายด้วยเครื่องหมายที่ต้องเกาะตัวอักษรอื่น");

        // และต้องนับเป็นหน่วยที่คนอ่านได้ไม่เกิน 12
        var elements = new System.Globalization.StringInfo(shortName);
        Assert.True(elements.LengthInTextElements <= 12);
    }

    [Fact]
    public void ShortNameHandlesEmojiAndCombiningMarks()
    {
        var shortName = BrandingEndpoints.ShortName("คาเฟ่ ☕ สบาย ๆ ริมน้ำ เชียงใหม่");
        var elements = new System.Globalization.StringInfo(shortName);
        Assert.True(elements.LengthInTextElements <= 12);
        Assert.False(string.IsNullOrWhiteSpace(shortName));
    }
}
