using System.Globalization;
using SharpMUSH.Library.Time;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Time;

public class DateExpressionParserTests
{
	public static IEnumerable<Func<DateExpressionExample>> Cases() => DateExpressionCorpus.Cases();

	[Test]
	[MethodDataSource(nameof(Cases))]
	public async Task SharedCorpus(DateExpressionExample example)
	{
		var result = DateExpressionParser.Parse(example.Expression, DateExpressionCorpus.Zone, DateTimeOffset.Parse("2026-10-16T12:00:00Z", CultureInfo.InvariantCulture));
		await Assert.That(result is ResolvedDateExpression).IsTrue();
		if (result is ResolvedDateExpression resolved)
		{
			await Assert.That(resolved.Instant).IsEqualTo(DateTimeOffset.Parse(example.ExpectedUtc, CultureInfo.InvariantCulture));
			await Assert.That(resolved.Normalized).Contains(resolved.Zone);
		}
	}

	[Test]
	[Arguments("next Tuesday", DateExpressionFailureKind.Incomplete)]
	[Arguments("tonight", DateExpressionFailureKind.Incomplete)]
	[Arguments("tomorrow evening", DateExpressionFailureKind.Incomplete)]
	[Arguments("next weekend", DateExpressionFailureKind.Incomplete)]
	[Arguments("03/04", DateExpressionFailureKind.Ambiguous)]
	[Arguments("tomorrow 7pm CST", DateExpressionFailureKind.Ambiguous)]
	[Arguments("Tuesday 6-8pm", DateExpressionFailureKind.Unsupported)]
	[Arguments("every other Tuesday at 7pm", DateExpressionFailureKind.Unsupported)]
	[Arguments("tomorrow 7pm trailing junk", DateExpressionFailureKind.Invalid)]
	[Arguments("tomorrow 7pm America/Chicagoo", DateExpressionFailureKind.Invalid)]
	[Arguments("2027-03-14 02:30 America/New_York", DateExpressionFailureKind.Invalid)]
	[Arguments("2026-11-01 01:30 America/New_York", DateExpressionFailureKind.Ambiguous)]
	[Arguments("2026-02-29 19:00", DateExpressionFailureKind.Invalid)]
	[Arguments("fifth Monday of February 2026 at noon", DateExpressionFailureKind.Invalid)]
	[Arguments("+1h-30m", DateExpressionFailureKind.Invalid)]
	[Arguments("in 2 hours at noon", DateExpressionFailureKind.Invalid)]
	public async Task RefusesToGuessOrIgnoreInput(string expression, DateExpressionFailureKind kind)
	{
		var result = DateExpressionParser.Parse(expression, "UTC", DateTimeOffset.Parse("2026-10-16T12:00:00Z", CultureInfo.InvariantCulture));
		await Assert.That(result is DateExpressionFailure).IsTrue();
		if (result is DateExpressionFailure failure) await Assert.That(failure.Kind).IsEqualTo(kind);
	}

	[Test]
	[Arguments("in 1 day", "2027-03-13T17:00:00Z", "2027-03-14T16:00:00Z")]
	[Arguments("in 24 hours", "2027-03-13T17:00:00Z", "2027-03-14T17:00:00Z")]
	[Arguments("in 1 day", "2026-10-31T16:00:00Z", "2026-11-01T17:00:00Z")]
	[Arguments("in 24 hours", "2026-10-31T16:00:00Z", "2026-11-01T16:00:00Z")]
	[Arguments("2026-11-01T01:30:00-04:00", "2026-10-31T16:00:00Z", "2026-11-01T05:30:00Z")]
	[Arguments("2026-11-01T01:30:00-05:00", "2026-10-31T16:00:00Z", "2026-11-01T06:30:00Z")]
	[Arguments("February 29 2028 noon", "2026-10-31T16:00:00Z", "2028-02-29T17:00:00Z")]
	[Arguments("this Tuesday 7pm", "2026-10-20T23:00:00Z", "2026-10-20T23:00:00Z")]
	[Arguments("next Tuesday 7pm", "2026-10-20T23:00:00Z", "2026-10-27T23:00:00Z")]
	[Arguments("next week Tuesday 7pm", "2026-10-20T23:00:00Z", "2026-10-27T23:00:00Z")]
	public async Task CalendarAndElapsedPolicies(string expression, string reference, string expected)
	{
		var result = DateExpressionParser.Parse(expression, "America/New_York", DateTimeOffset.Parse(reference, CultureInfo.InvariantCulture));
		await Assert.That(result is ResolvedDateExpression).IsTrue();
		if (result is ResolvedDateExpression resolved) await Assert.That(resolved.Instant).IsEqualTo(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task BoundsInputAndPreservesExplicitComponents()
	{
		await Assert.That(DateExpressionParser.Parse(new string('x', 513), "UTC", DateTimeOffset.UnixEpoch) is DateExpressionFailure).IsTrue();
		var parsed = DateExpressionParser.Parse("2026-10-23T19:00:00Z", "Invalid/Fallback", DateTimeOffset.UnixEpoch);
		await Assert.That(parsed is ResolvedDateExpression).IsTrue();
		if (parsed is ResolvedDateExpression resolved)
			await Assert.That(resolved.Explicit.HasFlag(DateExpressionComponents.Clock | DateExpressionComponents.Year | DateExpressionComponents.Zone)).IsTrue();
	}

	[Test]
	public async Task OrdinalDateRetainsTheWrittenYear()
	{
		var parsed = DateExpressionParser.Parse("last Friday of November 2027 at noon", "UTC", DateTimeOffset.UnixEpoch);
		await Assert.That(parsed is ResolvedDateExpression).IsTrue();
		if (parsed is ResolvedDateExpression value)
			await Assert.That(value.Explicit.HasFlag(DateExpressionComponents.Year)).IsTrue();
	}
}
