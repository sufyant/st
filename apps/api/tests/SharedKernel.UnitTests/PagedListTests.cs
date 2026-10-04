namespace SharedKernel.UnitTests;

public class PagedListTests
{
    [Fact]
    public void Mapping_a_page_maps_its_items_and_keeps_its_position()
    {
        var page = new PagedList<int>([1, 2], Page: 2, PageSize: 2, TotalCount: 5);

        var mapped = page.Map(item => $"#{item}");

        mapped.Items.ShouldBe(["#1", "#2"]);
        (mapped.Page, mapped.PageSize, mapped.TotalCount).ShouldBe((2, 2, 5L));
    }
}
