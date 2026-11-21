using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>A bucket write refused because the account already holds <see cref="AccountBuckets.MaxPerAccount"/> buckets.</summary>
public readonly record struct AccountBucketsFull;

/// <summary>
/// What a bucket write did, decided inside the write: <see cref="Success"/>, <see cref="NotFound"/> when there is
/// no such account, or <see cref="AccountBucketsFull"/>, in which case nothing was written.
/// </summary>
public union AccountBucketWrite(Success, NotFound, AccountBucketsFull);

/// <summary>
/// The rules a bucket's name and value follow, checked before anything reaches the store.
/// </summary>
public static class AccountBuckets
{
	/// <summary>The most buckets one account holds.</summary>
	public const int MaxPerAccount = 1000;

	/// <summary>The longest bucket name, in characters.</summary>
	public const int MaxNameLength = 64;

	/// <summary>
	/// The bucket name <paramref name="given"/> stands for: 1 to <see cref="MaxNameLength"/> printable ASCII
	/// characters other than space, folded to lower case so <c>XP</c> and <c>xp</c> are one bucket.
	/// </summary>
	public static Result<string> Name(string given)
		=> given.Length is > 0 and <= MaxNameLength && given.All(c => c is > ' ' and <= '~')
			? given.ToLowerInvariant()
			: new Error<string>($"BUCKET NAME MUST BE 1 TO {MaxNameLength} PRINTABLE ASCII CHARACTERS WITHOUT SPACES");

	/// <summary>
	/// <paramref name="given"/> as a bucket's value: one JSON value (an object, array, string, number, true,
	/// false or null) of at most <paramref name="maxBytes"/> bytes of UTF-8, with the whitespace around it trimmed.
	/// </summary>
	public static Result<string> Json(string given, int maxBytes)
	{
		var json = given.Trim();
		if (System.Text.Encoding.UTF8.GetByteCount(json) > maxBytes)
			return new Error<string>("BUCKET VALUE TOO LONG");

		try
		{
			using var _ = JsonDocument.Parse(json);
			return json;
		}
		catch (JsonException)
		{
			return new Error<string>("INVALID JSON");
		}
	}
}
