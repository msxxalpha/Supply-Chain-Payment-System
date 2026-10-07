using Indamin.Payment.Services;
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
}
