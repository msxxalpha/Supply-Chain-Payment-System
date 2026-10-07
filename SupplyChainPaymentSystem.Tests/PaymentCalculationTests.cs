using Indamin.Payment.Services;
using ClosedXML.Excel;
using Xunit;

namespace Indamin.Payment.Tests;

public class PaymentCalculationTests
{
    [Theory]
    [InlineData(10,100,1)]
    [InlineData(25,100,2)]
    [InlineData(50,100,2)]
    [InlineData(51,100,1)]
    [InlineData(75,100,1)]
    [InlineData(76,100,4)]
    [InlineData(100,100,4)]
    [InlineData(130,100,4)]
    [InlineData(131,100,5)]
    public void AgeScore_FollowsBoundaries(int a,int c,int e)
        => Assert.Equal(e, PaymentCalculationService.AgeScore(a,c));

    [Theory]
    [InlineData(2.99,100,1)]
    [InlineData(3,100,2)]
    [InlineData(10,100,2)]
    [InlineData(10.01,100,3)]
    [InlineData(20,100,3)]
    [InlineData(20.01,100,4)]
    [InlineData(35,100,4)]
    [InlineData(35.01,100,5)]
    public void DebtAmount_FollowsBoundaries(decimal d,decimal t,int e)
        => Assert.Equal(e, PaymentCalculationService.DebtAmountScore(d,t));

    [Fact]
    public void CompositeKey_NormalizesArabicLetters()
        => Assert.Equal(
            PaymentCalculationService.Key("R","انبار","قطعه ك","تامین‌کننده"),
            PaymentCalculationService.Key("r","انبار","قطعه ک","تامین‌کننده"));

    [Fact]
    public void KeyHash_NormalizesBusinessKey()
        => Assert.Equal(
            PaymentCalculationService.KeyHash("R","انبار","کالا ك","تامین کننده"),
            PaymentCalculationService.KeyHash("r","انبار","کالا ک","تامین کننده"));

    [Fact]
    public void AllocateBudget_NeverExceedsDebtOrBudget()
    {
        var rows = new List<PaymentCalculationRow>
        {
            new(){RemainingDebt=40,WeightedScore=5},
            new(){RemainingDebt=80,WeightedScore=3}
        };
        PaymentCalculationService.AllocateBudget(200,rows);
        Assert.True(rows.Sum(x=>x.AllocatedAmount)<=120);
        Assert.All(rows,x=>Assert.InRange(x.AllocatedAmount,0,x.RemainingDebt));
    }

    [Fact]
    public void AllocateBudget_DistributesAvailableBudgetByWeightedScore()
    {
        var rows = new List<PaymentCalculationRow>
        {
            new(){RemainingDebt=1000,WeightedScore=3},
            new(){RemainingDebt=1000,WeightedScore=1}
        };
        PaymentCalculationService.AllocateBudget(100,rows);
        Assert.Equal(100m,rows.Sum(x=>x.AllocatedAmount));
        Assert.Equal(75m,rows[0].AllocatedAmount);
        Assert.Equal(25m,rows[1].AllocatedAmount);
    }

    [Fact]
    public void SupplierPartExcel_ParsesCodes()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ارتباط");
        ws.Cell(1,1).Value="کد تامین کننده";
        ws.Cell(1,2).Value="کد قطعه";
        ws.Cell(1,3).Value="ظرفیت تامین";
        ws.Cell(1,4).Value="مهلت تسویه (روز)";
        ws.Cell(1,5).Value="فعال یا غیرفعال بودن";
        ws.Cell(2,1).Value="SUP-001";
        ws.Cell(2,2).Value="P-001";
        ws.Cell(2,3).Value=125.5m;
        ws.Cell(2,4).Value=30;
        ws.Cell(2,5).Value="فعال";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position=0;
        var rows = new ExcelService().ReadSupplierParts(
            ms,
            new Dictionary<string,int>{{"sup-001",7}},
            new Dictionary<string,int>{{"p-001",11}},
            out var errors);
        Assert.Empty(errors);
        var x=Assert.Single(rows);
        Assert.Equal(7,x.SupplierId);
        Assert.Equal(11,x.PartId);
        Assert.Equal(125.5m,x.SupplyCapacity);
        Assert.Equal(30,x.ContractSettlementDays);
        Assert.True(x.IsActive);
    }

    [Fact]
    public void SupplierDebtAmountScore_UsesSupplierAggregate()
        => Assert.Equal(4, PaymentCalculationService.SupplierDebtAmountScore(40,100));

    [Fact]
    public void SplitByShares_MovesUnavailableInitialShareToCurrent()
    {
        var x=PaymentCalculationService.SplitByShares(100,0,1000,50,50);
        Assert.Equal(0,x.InitialClaim);
        Assert.Equal(100,x.Current);
        Assert.Equal(100,x.Total);
    }

    [Fact]
    public void SplitByShares_MovesUnavailableCurrentShareToInitial()
    {
        var x=PaymentCalculationService.SplitByShares(100,1000,0,50,50);
        Assert.Equal(100,x.InitialClaim);
        Assert.Equal(0,x.Current);
        Assert.Equal(100,x.Total);
    }

    [Fact]
    public void SplitByShares_EachComponentIsMultipleOfConfiguredCoefficient()
    {
        var x=PaymentCalculationService.SplitByShares(1_000_000,5_000_000,5_000_000,50,50,100_000);
        Assert.Equal(500_000,x.InitialClaim);
        Assert.Equal(500_000,x.Current);
        Assert.Equal(0,x.InitialClaim%100_000);
        Assert.Equal(0,x.Current%100_000);
    }

    [Fact]
    public void SplitByShares_TransfersUnavailableInitialUnitsToCurrent()
    {
        var x=PaymentCalculationService.SplitByShares(1_000_000,200_000,5_000_000,50,50,100_000);
        Assert.Equal(200_000,x.InitialClaim);
        Assert.Equal(800_000,x.Current);
        Assert.Equal(1_000_000,x.Total);
    }

    [Fact]
    public void CalculatedAllocation_RespectsMinimumAgeRoundingAndMinimumAmount()
    {
        var rows=new List<PaymentCalculationRow>
        {
            new(){SupplierId=1,SupplierInitialClaimAmount=0,RemainingDebt=100_000_000,DebtAgeDays=10,WeightedScore=5},
            new(){SupplierId=2,SupplierInitialClaimAmount=0,RemainingDebt=100_000_000,DebtAgeDays=3,WeightedScore=5}
        };
        PaymentCalculationService.AllocateCalculatedBudget(10_000_000,rows,50,50,5,6_000_000,1_000_000);
        Assert.Equal(10_000_000,rows[0].AllocatedAmount);
        Assert.Equal(0,rows[1].AllocatedAmount);
        Assert.Equal(10_000_000,rows[0].AllocatedCurrentAmount);
        Assert.Equal(0,rows[0].AllocatedInitialClaimAmount);
        Assert.True(rows.All(x=>x.AllocatedAmount%1_000_000==0));
        Assert.True(rows[0].AllocatedAmount>6_000_000);
    }

    [Fact]
    public void CalculatedAllocation_SkipsCandidateBelowMinimumWithoutConsumingBudget()
    {
        var rows=new List<PaymentCalculationRow>
        {
            new(){SupplierId=1,SupplierInitialClaimAmount=0,RemainingDebt=100_000_000,DebtAgeDays=10,WeightedScore=1},
            new(){SupplierId=2,SupplierInitialClaimAmount=0,RemainingDebt=100_000_000,DebtAgeDays=10,WeightedScore=3}
        };
        PaymentCalculationService.AllocateCalculatedBudget(6_000_000,rows,50,50,0,4_000_000,1_000_000);
        Assert.Equal(6_000_000,rows.Sum(x=>x.AllocatedAmount));
        Assert.True(rows.Any(x=>x.AllocatedAmount>4_000_000));
    }

    [Fact]
    public void CalculatedAllocation_UsesInitialClaimAndFallsBackToCurrent()
    {
        var rows=new List<PaymentCalculationRow>
        {
            new(){SupplierId=1,SupplierInitialClaimAmount=300_000,RemainingDebt=1_000_000,DebtAgeDays=10,WeightedScore=1}
        };
        PaymentCalculationService.AllocateCalculatedBudget(1_000_000,rows,50,50,0,1,100_000);
        Assert.Equal(300_000,rows[0].AllocatedInitialClaimAmount);
        Assert.Equal(700_000,rows[0].AllocatedCurrentAmount);
        Assert.Equal(1_000_000,rows[0].AllocatedAmount);
        Assert.Equal(0,rows[0].AllocatedInitialClaimAmount%100_000);
        Assert.Equal(0,rows[0].AllocatedCurrentAmount%100_000);
    }

    [Fact]
    public void CalculatedAllocation_SkipsReceiptAtOrBelowMinimumAge()
    {
        var rows=new List<PaymentCalculationRow>
        {
            new(){SupplierId=1,SupplierInitialClaimAmount=1_000_000,RemainingDebt=1_000_000,DebtAgeDays=5,WeightedScore=5},
            new(){SupplierId=1,SupplierInitialClaimAmount=1_000_000,RemainingDebt=1_000_000,DebtAgeDays=6,WeightedScore=1}
        };
        PaymentCalculationService.AllocateCalculatedBudget(1_000_000,rows,50,50,5,1,100_000);
        Assert.Equal(0,rows[0].AllocatedAmount);
        Assert.Equal(1_000_000,rows[1].AllocatedAmount);
    }

    [Fact]
    public void DistributeSupplierAllocation_PreservesStage2Snapshot()
    {
        var rows=new List<PaymentCalculationRow>
        {
            new(){SupplierId=1,SupplierInitialClaimAmount=0,RemainingDebt=10_000_000,DebtAgeDays=10,CalculatedAllocatedAmount=3_000_000,CalculatedCurrentAllocatedAmount=3_000_000,CalculatedInitialClaimAllocatedAmount=0},
            new(){SupplierId=1,SupplierInitialClaimAmount=0,RemainingDebt=20_000_000,DebtAgeDays=10,CalculatedAllocatedAmount=7_000_000,CalculatedCurrentAllocatedAmount=7_000_000,CalculatedInitialClaimAllocatedAmount=0}
        };
        PaymentCalculationService.DistributeSupplierAllocation(5_000_000,rows,50,50,100_000,0,1);
        Assert.Equal(3_000_000,rows[0].CalculatedAllocatedAmount);
        Assert.Equal(7_000_000,rows[1].CalculatedAllocatedAmount);
        Assert.Equal(5_000_000,rows.Sum(x=>x.AllocatedAmount));
        Assert.True(rows.All(x=>x.AllocatedAmount%100_000==0));
    }

    [Theory]
    [InlineData("SELECT 1", true)]
    [InlineData("WITH x AS (SELECT 1 AS A) SELECT A FROM x", true)]
    [InlineData("UPDATE Parts SET Title='x'", false)]
    [InlineData("SELECT 1; DELETE FROM Parts", false)]
    public void InputQuery_AllowsOnlySingleReadOnlyStatement(string sql, bool expected)
        => Assert.Equal(expected, InputQueryService.IsReadOnlyQuery(sql));

    [Fact]
    public void PaymentReceiptExcel_ParsesReceiptQuantity()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("رسیدها");
        ws.Cell(1,1).Value="شماره رسید";
        ws.Cell(1,2).Value="انبار";
        ws.Cell(1,3).Value="نام کالا";
        ws.Cell(1,4).Value="نام تامین کننده";
        ws.Cell(1,5).Value="مقدار رسید";
        ws.Cell(1,6).Value="مبلغ بدهی";
        ws.Cell(1,7).Value="تاریخ رسید";
        ws.Cell(2,1).Value="R-100";
        ws.Cell(2,2).Value="انبار مرکزی";
        ws.Cell(2,3).Value="کالا ۱";
        ws.Cell(2,4).Value="تامین‌کننده ۱";
        ws.Cell(2,5).Value=12.75m;
        ws.Cell(2,6).Value=1000000m;
        ws.Cell(2,7).Value="1405/01/15";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position=0;

        var rows = new ExcelService().ReadPaymentInvoices(ms, PersianDateService.Parse, out var errors);

        Assert.Empty(errors);
        var row = Assert.Single(rows);
        Assert.Equal(12.75m, row.ReceiptQuantity);
        Assert.Equal(1000000m, row.DebtAmount);
    }

    [Fact]
    public void EfModel_DoesNotContainShadowForeignKeys()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Indamin.Payment.Data.AppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelValidationOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var db = new Indamin.Payment.Data.AppDbContext(options);
        var shadowForeignKeys = db.Model.GetEntityTypes()
            .SelectMany(x => x.GetForeignKeys())
            .SelectMany(x => x.Properties)
            .Where(x => x.IsShadowProperty())
            .Select(x => x.DeclaringType.ClrType.Name + "." + x.Name)
            .Distinct()
            .ToList();

        Assert.Empty(shadowForeignKeys);
    }

}
