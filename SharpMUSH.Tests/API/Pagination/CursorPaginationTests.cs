using SharpMUSH.Library.API.Pagination;

namespace SharpMUSH.Tests.API.Pagination;

/// <summary>
/// Unit tests for <see cref="CursorPaginationHelper"/> and <see cref="CursorPage{T}"/>.
/// </summary>
public class CursorPaginationTests
{
    private static IEnumerable<int> Range(int from, int to) =>
        Enumerable.Range(from, to - from + 1);

    // ── Basic slicing ────────────────────────────────────────────────────────

    [Test]
    public async Task Paginate_FirstPage_ReturnsFirstN()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10);

        await Assert.That(page.Items.Count).IsEqualTo(10);
        await Assert.That(page.Items[0]).IsEqualTo(1);
        await Assert.That(page.Items[9]).IsEqualTo(10);
    }

    [Test]
    public async Task Paginate_FirstPage_HasNextCursor()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10);
        await Assert.That(page.HasNextPage).IsTrue();
        await Assert.That(page.NextCursor).IsNotNull();
    }

    [Test]
    public async Task Paginate_FirstPage_NoPreviousCursor()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10);
        await Assert.That(page.HasPreviousPage).IsFalse();
        await Assert.That(page.PreviousCursor).IsNull();
    }

    [Test]
    public async Task Paginate_SecondPage_FollowsNextCursor()
    {
        var page1 = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10);
        var page2 = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10, after: page1.NextCursor);

        await Assert.That(page2.Items.Count).IsEqualTo(10);
        await Assert.That(page2.Items[0]).IsEqualTo(11);
        await Assert.That(page2.Items[9]).IsEqualTo(20);
    }

    [Test]
    public async Task Paginate_SecondPage_HasPreviousCursor()
    {
        var page1 = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10);
        var page2 = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 10, after: page1.NextCursor);

        await Assert.That(page2.HasPreviousPage).IsTrue();
    }

    [Test]
    public async Task Paginate_LastPage_NoNextCursor()
    {
        // 5 items with pageSize=10 — fits all on one page
        var page = CursorPaginationHelper.Paginate(Range(1, 5), x => x, pageSize: 10);

        await Assert.That(page.HasNextPage).IsFalse();
        await Assert.That(page.NextCursor).IsNull();
    }

    [Test]
    public async Task Paginate_ExactlyPageSize_NoNextCursor()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 10), x => x, pageSize: 10);

        await Assert.That(page.Items.Count).IsEqualTo(10);
        await Assert.That(page.HasNextPage).IsFalse();
    }

    // ── Page size clamping ───────────────────────────────────────────────────

    [Test]
    public async Task Paginate_PageSizeZero_ClampedTo1()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 100), x => x, pageSize: 0);
        await Assert.That(page.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Paginate_PageSizeOverMax_ClampedTo200()
    {
        var page = CursorPaginationHelper.Paginate(Range(1, 500), x => x, pageSize: 9999);
        await Assert.That(page.Items.Count).IsEqualTo(200);
    }

    // ── Cursor encode / decode round-trip ────────────────────────────────────

    [Test]
    public async Task EncodeDecode_RoundTrip_Int()
    {
        var encoded = CursorPaginationHelper.EncodeCursor(42);
        var decoded  = CursorPaginationHelper.DecodeCursor<int>(encoded);

        await Assert.That(decoded).IsEqualTo(42);
    }

    [Test]
    public async Task EncodeDecode_RoundTrip_String()
    {
        var encoded = CursorPaginationHelper.EncodeCursor("hello-world");
        var decoded  = CursorPaginationHelper.DecodeCursor<string>(encoded);

        await Assert.That(decoded).IsEqualTo("hello-world");
    }

    [Test]
    public async Task DecodeCursor_InvalidBase64_ReturnsDefault()
    {
        var decoded = CursorPaginationHelper.DecodeCursor<int>("!!!invalid!!!");
        await Assert.That(decoded).IsEqualTo(0);
    }

    // ── Empty source ─────────────────────────────────────────────────────────

    [Test]
    public async Task Paginate_EmptySource_ReturnsEmptyPage()
    {
        var page = CursorPaginationHelper.Paginate(Enumerable.Empty<int>(), x => x, pageSize: 10);

        await Assert.That(page.Items.Count).IsEqualTo(0);
        await Assert.That(page.HasNextPage).IsFalse();
        await Assert.That(page.HasPreviousPage).IsFalse();
    }
}
