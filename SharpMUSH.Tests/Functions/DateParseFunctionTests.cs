using System.Globalization;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Time;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Functions;

public class DateParseFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }
	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	public static IEnumerable<Func<DateExpressionExample>> Cases() => DateExpressionCorpus.Cases();

	[Test]
	[MethodDataSource(nameof(Cases))]
	public async Task SharedCorpus(DateExpressionExample example)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain($"dateparse({example.Expression},{DateExpressionCorpus.Zone},{DateExpressionCorpus.Reference},ms)"));
		await Assert.That(result.ToPlainText()).IsEqualTo(DateTimeOffset.Parse(example.ExpectedUtc, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
	}

	[Test]
	public async Task TomorrowUsesTheExplicitReferenceAndZone()
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain("dateparse(tomorrow 7pm,America/Chicago,1792152000)"));
		await Assert.That(result.ToPlainText()).IsEqualTo("1792281600");
	}

	[Test]
	[Arguments("dateparse(tomorrow 7pm America/Chicago,UTC,1792152000,normalized)", "2026-10-17 19:00:00 -05:00 (America/Chicago)")]
	[Arguments("dateparse(2026-11-01T01:30:00-04:00,UTC,,normalized)", "2026-11-01 01:30:00 -04:00 (-04:00)")]
	[Arguments("dateparse(1792152000.125,UTC,,f)", "1792152000.125")]
	[Arguments("dateparse(1792152000.125,UTC,,ms)", "1792152000125")]
	[Arguments("dateparse(-1.5,UTC)", "-2")]
	[Arguments("dateparse(tomorrow 7pm,UTC,nonsense)", "#-1 INVALID REFERENCE TIME")]
	[Arguments("dateparse(tomorrow 7pm,UTC,1792152000,bad)", "#-1 INVALID PRECISION")]
	[Arguments("dateparse(next Tuesday,UTC,1792152000)", "#-1 INCOMPLETE DATE: CLOCK TIME REQUIRED; ADD A TIME SUCH AS 19:00 OR NOON")]
	public async Task PrecisionReferenceAndClarificationContracts(string expression, string expected)
	{
		var actual = await Parser.EvaluateAsync(MarkupText.Plain(expression));
		await Assert.That(actual.ToPlainText()).IsEqualTo(expected);
	}
}
