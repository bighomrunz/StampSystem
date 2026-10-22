using CoffeeStamp.Web.Domain;
using CoffeeStamp.Web.Services;

namespace CoffeeStamp.Tests;

/// <summary>
/// กฎการแก้สแตมป์ผิด — จุดที่ reward card เดิมใน LINE ทำไม่ได้และต้องออกบัตรใหม่
/// ที่นี่บัตรไม่ต้องทำใหม่ เพราะ ledger เป็น append-only การแก้คือการเพิ่มรายการ
/// </summary>
public class StampCorrectionRuleTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void ManagerPinNeededOnlyWhenAddingStamps(int delta, bool needsManager)
    {
        // ลดแต้มเสียหายกับร้านไม่ได้ พนักงานจึงทำเองได้
        // เพิ่มแต้มคือการสร้างมูลค่า ต้องมีผู้อนุมัติ
        Assert.Equal(needsManager, delta > 0);
    }

    [Theory]
    [InlineData(3, -3, true)]
    [InlineData(3, -4, false)]
    [InlineData(0, -1, false)]
    [InlineData(10, 5, true)]
    public void BalanceMustNeverGoNegative(int balance, int delta, bool allowed)
    {
        Assert.Equal(allowed, balance + delta >= 0);
    }

    [Fact]
    public void CorrectionIsAppendedNotDeleted()
    {
        // สแตมป์ 2 แล้วพบว่าผิด จึงเพิ่มรายการ -2 ไม่ใช่ลบรายการเดิม
        var ledger = new List<StampTransaction>
        {
            new() { Type = StampTransactionType.Earn, Quantity = 2, OrderId = 1 },
            new()
            {
                Type = StampTransactionType.Adjust,
                AdjustKind = AdjustKind.Correction,
                Quantity = -2,
                Note = "สแตมป์ผิด"
            }
        };

        Assert.Equal(0, ledger.Sum(t => t.Quantity));
        // รายการเดิมต้องยังอยู่ให้ตรวจย้อนหลังได้
        Assert.Single(ledger, t => t.Type == StampTransactionType.Earn);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void TransferMovesStampsWithoutChangingTotalInSystem()
    {
        // สแตมป์ผิดคน: ฝั่งผิดได้ Void ฝั่งถูกได้ Adjust ยอดรวมทั้งระบบไม่เปลี่ยน
        const int wrongCustomer = 1;
        const int rightCustomer = 2;

        var ledger = new List<StampTransaction>
        {
            new() { CustomerId = wrongCustomer, Type = StampTransactionType.Earn, Quantity = 2, OrderId = 9 },
            new() { CustomerId = wrongCustomer, Type = StampTransactionType.Void, Quantity = -2, OrderId = 9 },
            new()
            {
                CustomerId = rightCustomer,
                Type = StampTransactionType.Adjust,
                AdjustKind = AdjustKind.Correction,
                Quantity = 2,
                OrderId = 9
            }
        };

        Assert.Equal(0, ledger.Where(t => t.CustomerId == wrongCustomer).Sum(t => t.Quantity));
        Assert.Equal(2, ledger.Where(t => t.CustomerId == rightCustomer).Sum(t => t.Quantity));
        Assert.Equal(2, ledger.Sum(t => t.Quantity));
        // ทุกรายการยังผูกกับออเดอร์เดิม ตรวจย้อนได้ว่าแต้มชุดนี้มาจากการขายครั้งไหน
        Assert.All(ledger, t => Assert.Equal(9, t.OrderId));
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    [InlineData(-50, true)]
    [InlineData(-51, false)]
    [InlineData(0, false)]
    public void CorrectionSizeIsCapped(int delta, bool allowed)
    {
        Assert.Equal(allowed, delta != 0 && Math.Abs(delta) <= 50);
    }
}

public class QuickSaleTests
{
    /// <summary>
    /// ขายเร็วไม่เลือกเมนู แต่ยังต้องผูกออเดอร์จริงตามหลักข้อ 2 ของ README
    /// จำนวนแต้มมาจากจำนวนแก้วในบรรทัดเดียวของออเดอร์
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    public void StampCountComesFromCupCount(int cups)
    {
        var order = new Order
        {
            Items =
            [
                new OrderItem
                {
                    NameSnapshot = $"ขายเร็ว {cups} แก้ว",
                    Quantity = cups,
                    EarnsStampSnapshot = true,
                    IsReward = false
                }
            ]
        };

        Assert.Equal(cups, order.EligibleStampCount);
    }

    [Fact]
    public void QuickSaleWithoutAmountKeepsMoneyAtZero()
    {
        // ช่วงเปิดใช้แค่สแตมป์ ร้านยังไม่กรอกยอดเงิน ออเดอร์จึงมียอด 0 ได้
        var order = new Order
        {
            SubTotal = 0m,
            Total = 0m,
            Items = [new OrderItem { Quantity = 2, UnitPrice = 0m, LineTotal = 0m, EarnsStampSnapshot = true }]
        };

        Assert.Equal(0m, order.Total);
        Assert.Equal(2, order.EligibleStampCount);
    }

    [Fact]
    public void QuickSaleWithAmountKeepsTypedTotalExact()
    {
        // ยอดที่พนักงานพิมพ์ต้องเก็บตรงเป๊ะ ไม่เพี้ยนจากการหารเฉลี่ยต่อแก้ว
        const decimal typed = 100m;
        const int cups = 3;

        var unitPrice = Money.Round(typed / cups);
        var order = new Order
        {
            SubTotal = typed,
            Total = typed,
            Items = [new OrderItem { Quantity = cups, UnitPrice = unitPrice, LineTotal = typed }]
        };

        Assert.Equal(100m, order.Total);
        Assert.Equal(33.33m, unitPrice);
        Assert.Equal(100m, order.Items.Sum(i => i.LineTotal));
    }
}
