using SharpMUSH.Library.API.Pagination;

namespace SharpMUSH.Tests.API.Pagination;

/// <summary>
/// Unit tests for <see cref="OffsetPaginationHelper"/> and <see cref="PagedResult{T}"/>.
/// </summary>
public class OffsetPaginationTests
{
    private static IEnumerable<int> Seq(int count) => Enumerable.Range(1, count);

    // ── Basic slicing ────────────────────────────────────────────────────────

    [Test]
    public async Task Paginate_Page0_ReturnsFirstN()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(100), page: 0, pageSize: 10);

        await Assert.That(result.Items.Count).IsEqualTo(10);
        await Assert.That(result.Items[0]).IsEqualTo(1);
        await Assert.That(result.Items[9]).IsEqualTo(10);
    }

    [Test]
    public async Task Paginate_Page1_ReturnsSecondSlice()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(100), page: 1, pageSize: 10);

        await Assert.That(result.Items[0]).IsEqualTo(11);
        await Assert.That(result.Items[9]).IsEqualTo(20);
    }

    [Test]
    public async Task Paginate_LastPage_ReturnsRemainder()
    {
        // 25 items, pageSize 10: page 2 = items 21..25
        var result = OffsetPaginationHelper.Paginate(Seq(25), page: 2, pageSize: 10);

        await Assert.That(result.Items.Count).IsEqualTo(5);
        await Assert.That(result.Items[0]).IsEqualTo(21);
    }

    [Test]
    public async Task Paginate_BeyondEnd_ReturnsEmptyPage()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(5), page: 10, pageSize: 10);
        await Assert.That(result.Items.Count).IsEqualTo(0);
    }

    // ── TotalCount / TotalPages ───────────────────────────────────────────────

    [Test]
    public async Task Paginate_TotalCountMatchesSourceLength()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(37), page: 0, pageSize: 10);
        await Assert.That(result.TotalCount).IsEqualTo(37);
    }

    [Test]
    public async Task Paginate_TotalPagesRoundsUp()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(37), page: 0, pageSize: 10);
        await Assert.That(result.TotalPages).IsEqualTo(4); // ceil(37/10)
    }

    [Test]
    public async Task Paginate_ExactMultiple_NoExtraPage()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(30), page: 0, pageSize: 10);
        await Assert.That(result.TotalPages).IsEqualTo(3);
    }

    // ── HasNextPage / HasPreviousPage ─────────────────────────────────────────

    [Test]
    public async Task Paginate_Page0_HasNextPage_WhenMoreExists()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(20), page: 0, pageSize: 10);
        await Assert.That(result.HasNextPage).IsTrue();
        await Assert.That(result.HasPreviousPage).IsFalse();
    }

    [Test]
    public async Task Paginate_LastPage_NoNextPage()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(10), page: 0, pageSize: 10);
        await Assert.That(result.HasNextPage).IsFalse();
    }

    [Test]
    public async Task Paginate_Page1_HasPreviousPage()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(30), page: 1, pageSize: 10);
        await Assert.That(result.HasPreviousPage).IsTrue();
    }

    // ── Parameter clamping ───────────────────────────────────────────────────

    [Test]
    public async Task Paginate_NegativePage_ClampedToZero()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(20), page: -5, pageSize: 10);
        await Assert.That(result.Page).IsEqualTo(0);
        await Assert.That(result.Items[0]).IsEqualTo(1);
    }

    [Test]
    public async Task Paginate_PageSizeZero_ClampedTo1()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(20), page: 0, pageSize: 0);
        await Assert.That(result.PageSize).IsEqualTo(1);
        await Assert.That(result.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Paginate_PageSizeOverMax_ClampedTo200()
    {
        var result = OffsetPaginationHelper.Paginate(Seq(500), page: 0, pageSize: 9999);
        await Assert.That(result.PageSize).IsEqualTo(200);
        await Assert.That(result.Items.Count).IsEqualTo(200);
    }

    // ── FromSlice overload ───────────────────────────────────────────────────

    [Test]
    public async Task FromSlice_HonoursTotalCountFromCaller()
    {
        var slice = new[] { 21, 22, 23, 24, 25 };
        var result = OffsetPaginationHelper.FromSlice(slice, totalCount: 100, page: 2, pageSize: 10);

        await Assert.That(result.TotalCount).IsEqualTo(100);
        await Assert.That(result.Page).IsEqualTo(2);
        await Assert.That(result.PageSize).IsEqualTo(10);
        await Assert.That(result.Items.Count).IsEqualTo(5);
        await Assert.That(result.TotalPages).IsEqualTo(10);
    }

    [Test]
    public async Task FromSlice_HasNextPage_WhenNotLastPage()
    {
        var result = OffsetPaginationHelper.FromSlice<int>([], totalCount: 100, page: 0, pageSize: 10);
        await Assert.That(result.HasNextPage).IsTrue();
    }

    // ── Empty source ─────────────────────────────────────────────────────────

    [Test]
    public async Task Paginate_EmptySource_ReturnsEmptyResult()
    {
        var result = OffsetPaginationHelper.Paginate(Enumerable.Empty<string>(), page: 0, pageSize: 10);

        await Assert.That(result.Items.Count).IsEqualTo(0);
        await Assert.That(result.TotalCount).IsEqualTo(0);
        await Assert.That(result.TotalPages).IsEqualTo(0);
        await Assert.That(result.HasNextPage).IsFalse();
        await Assert.That(result.HasPreviousPage).IsFalse();
    }
}
