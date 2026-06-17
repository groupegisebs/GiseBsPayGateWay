using GiseBsPayGateway.Pages.Admin;

namespace GiseBsPayGateway.Tests.Pages.Admin;

public class AdminListPaginationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(2, 2)]
    public void Parse_NormalizesPage(int input, int expected)
    {
        var (page, _) = AdminListPagination.Parse(input, null);
        Assert.Equal(expected, page);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("  abc  ", "abc")]
    public void Parse_NormalizesSearch(string? input, string? expected)
    {
        var (_, search) = AdminListPagination.Parse(1, input);
        Assert.Equal(expected, search);
    }

    [Fact]
    public void Create_ClampsPageToTotalPages()
    {
        var info = AdminListPagination.Create(99, null, 20);
        Assert.Equal(2, info.Page);
        Assert.Equal(2, info.TotalPages);
    }

    [Fact]
    public void Create_ComputesSkipAndRange()
    {
        var info = AdminListPagination.Create(2, "test", 20);
        Assert.Equal(15, info.Skip);
        Assert.Equal(16, info.StartItem);
        Assert.Equal(20, info.EndItem);
        Assert.Equal("?page=2&search=test", info.PageUrl(2));
    }

    [Fact]
    public void PageUrl_OmitsSearchWhenEmpty()
    {
        var info = AdminListPagination.Create(1, null, 0);
        Assert.Equal("?page=3", info.PageUrl(3));
    }
}
