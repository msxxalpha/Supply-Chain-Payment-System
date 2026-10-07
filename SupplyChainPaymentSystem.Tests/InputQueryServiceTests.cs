using Indamin.Payment.Services;
using Indamin.Payment.Controllers;
using Xunit;

namespace Indamin.Payment.Tests;

public class InputQueryServiceTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("SELECT * FROM dbo.Items WHERE Id = @Id;")]
    [InlineData("-- comment\nSELECT 1")]
    [InlineData("/* block comment */ SELECT 1")]
    [InlineData("WITH cte AS (SELECT 1 AS Id) SELECT Id FROM cte")]
    public void IsReadOnlyQuery_AcceptsSelectAndCte(string sql)
        => Assert.True(InputQueryService.IsReadOnlyQuery(sql));

    [Theory]
    [InlineData("")]
    [InlineData("UPDATE dbo.Items SET Name='x'")]
    [InlineData("SELECT 1; DELETE FROM dbo.Items")]
    [InlineData("EXEC dbo.SomeProcedure")]
    [InlineData("DROP TABLE dbo.Items")]
    [InlineData("INSERT INTO dbo.Items(Id) VALUES(1)")]
    public void IsReadOnlyQuery_RejectsNonReadOnlyOrMultipleStatements(string sql)
        => Assert.False(InputQueryService.IsReadOnlyQuery(sql));

    [Fact]
    public void IsReadOnlyQuery_RemovesCommentsWithoutRegexException()
    {
        var sql = "/* SELECT; UPDATE */ -- trailing comment\nSELECT * FROM dbo.Items;";
        Assert.True(InputQueryService.IsReadOnlyQuery(sql));
    }
    [Theory]
    [InlineData("1405/07/15", "2026-10-07")]
    [InlineData("۱۴۰۵/۰۷/۱۵", "2026-10-07")]
    [InlineData("1405-07-15 00:00:00", "2026-10-07")]
    public void ParseQueryResultDate_ParsesJalaliReceiptDates(string value, string expectedGregorian)
    {
        var actual = InputQueryService.ParseQueryResultDate(value);
        Assert.Equal(DateTime.Parse(expectedGregorian).Date, actual);
    }

    [Fact]
    public void ParseQueryResultDate_StillParsesGregorianSqlDates()
    {
        var actual = InputQueryService.ParseQueryResultDate("2026-10-07 14:30:00");
        Assert.Equal(new DateTime(2026, 10, 7), actual);
    }

    [Fact]
    public void ParseQueryResultDate_RejectsInvalidJalaliDate()
        => Assert.Throws<FormatException>(() => InputQueryService.ParseQueryResultDate("1405/12/30"));
    [Fact]
    public void ExtractImportErrorRowNumbers_ParsesSingleAndGroupedRows()
    {
        var errors = new[]
        {
            "سطر 2: مقدار رسید نامعتبر است.",
            "کالا «X» در اطلاعات پایه تعریف نشده یا فعال نیست؛ سطرهای Excel: 5، 8.",
            "برای کالا «Y» و تامین‌کننده «Z» قیمت معتبر وجود ندارد؛ رکوردهای ورودی: 11، 14."
        };

        Assert.Equal(new[] { 2, 5, 8, 11, 14 }, PaymentWizardController.ExtractImportErrorRowNumbers(errors));
    }


}
