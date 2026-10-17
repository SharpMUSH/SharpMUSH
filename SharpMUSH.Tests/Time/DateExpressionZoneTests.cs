using System.Globalization;
using SharpMUSH.Library.Time;

namespace SharpMUSH.Tests.Time;

public class DateExpressionZoneTests
{
	[Test]
	[Arguments("2026-10-23T19:00:00Z Nowhere/Zone", DateExpressionFailureKind.Invalid)]
	[Arguments("2026-10-23T19:00:00Z America/Chicago", DateExpressionFailureKind.Ambiguous)]
	[Arguments("CST", DateExpressionFailureKind.Ambiguous)]
	[Arguments("first Saturday of next month 2027 at noon", DateExpressionFailureKind.Ambiguous)]
	[Arguments("in 2 hours ago", DateExpressionFailureKind.Invalid)]
	[Arguments("in ١ hours", DateExpressionFailureKind.Invalid)]
	[Arguments("last Friday of November ٢٠٢٧ at noon", DateExpressionFailureKind.Invalid)]
	[Arguments("Tuesday 18:30-20:30", DateExpressionFailureKind.Unsupported)]
	public async Task EveryWrittenComponentMustBeAccountedFor(string expression, DateExpressionFailureKind expected)
	{
		var result = DateExpressionParser.Parse(expression, "UTC", DateTimeOffset.UnixEpoch);
		await Assert.That(result is DateExpressionFailure).IsTrue();
		if (result is DateExpressionFailure failure) await Assert.That(failure.Kind).IsEqualTo(expected);
	}

	[Test]
	public async Task MatchingOffsetAndNamedZoneKeepTheNamedZone()
	{
		var parsed = DateExpressionParser.Parse("2026-10-23T19:00:00-05:00 America/Chicago", "Invalid/Fallback", DateTimeOffset.UnixEpoch);
		await Assert.That(parsed is ResolvedDateExpression).IsTrue();
		if (parsed is ResolvedDateExpression resolved)
		{
			await Assert.That(resolved.Zone).IsEqualTo("America/Chicago");
			await Assert.That(resolved.Instant).IsEqualTo(DateTimeOffset.Parse("2026-10-24T00:00:00Z", CultureInfo.InvariantCulture));
		}
	}
}
