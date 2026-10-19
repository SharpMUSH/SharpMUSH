namespace SharpMUSH.Library.Models.Wiki;

/// <summary>Begin a new operation-local scan.</summary>
public readonly record struct WikiPageScanStart;

/// <summary>A copied provider position, never an authorization grant or public HTTP cursor.</summary>
public sealed record WikiPageScanContinuation(int Offset, string Token);

/// <summary>A scan starts explicitly or resumes an earlier batch under explicitly supplied visibility.</summary>
public union WikiPageScanPosition(WikiPageScanStart, WikiPageScanContinuation);

/// <summary>The provider exhausted its iterator.</summary>
public readonly record struct WikiPageScanEnd;

/// <summary>Another position, or proven end of the scan.</summary>
public union WikiPageScanNext(WikiPageScanContinuation, WikiPageScanEnd);

/// <summary>Materialized admitted pages and operation-local progress. Separate batches are not one snapshot.</summary>
public sealed record WikiPageScanBatch(IReadOnlyList<WikiPage> Pages, WikiPageScanNext Next);

/// <summary>Validation and compatibility behavior shared by the public scan interfaces.</summary>
public static class WikiPageScan
{
	// Token decoding bounds allocations; this is not a candidate/search-work budget.
	public const int MaxPositionBytes = 65536;
	private const string OffsetToken = "offset:v1";

	public static int Validate(WikiPageScanPosition position, int take)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
		return position switch
		{
			WikiPageScanStart => 0,
			WikiPageScanContinuation { Offset: >= 0, Token: { Length: > 0 } token } continuation
				when token.Length <= 4 * ((MaxPositionBytes + 2) / 3) + 64 => continuation.Offset,
			_ => throw new ArgumentException("Invalid wiki scan position.", nameof(position))
		};
	}

	public static byte[] Decode(string token, string prefix)
	{
		if (!token.StartsWith(prefix, StringComparison.Ordinal)
			|| token.Length - prefix.Length > 4 * ((MaxPositionBytes + 2) / 3))
			throw new ArgumentException("Invalid wiki scan token version or length.", nameof(token));
		var payload = token[prefix.Length..];
		if (payload.Length == 0) throw new ArgumentException("Empty wiki scan position.", nameof(token));
		var bytes = Convert.FromBase64String(payload);
		if (bytes.Length > MaxPositionBytes) throw new ArgumentException("Wiki scan position is too large.", nameof(token));
		return bytes;
	}

	public static string Encode(byte[] bytes, string prefix)
	{
		if (bytes.Length > MaxPositionBytes) throw new ArgumentException("Wiki scan position is too large.", nameof(bytes));
		return prefix + Convert.ToBase64String(bytes);
	}

	/// <summary>Old implementers retain their existing listing semantics and offset work.</summary>
	public static async Task<WikiPageScanBatch> FallbackAsync(WikiPageScanPosition position, int take,
		Func<int, int, Task<IReadOnlyList<WikiPage>>> read)
	{
		var offset = Validate(position, take);
		if (position is WikiPageScanContinuation continuation && continuation.Token != OffsetToken)
			throw new ArgumentException("A provider scan position cannot resume an offset implementation.", nameof(position));
		var next = checked(offset + take);
		var pages = await read(offset, take);
		return new WikiPageScanBatch(pages, pages.Count < take
			? new WikiPageScanEnd() : new WikiPageScanContinuation(next, OffsetToken));
	}
}
