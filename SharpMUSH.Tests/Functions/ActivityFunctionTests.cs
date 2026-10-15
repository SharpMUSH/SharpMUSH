using System.Text.Json;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// activity(): each staff report as JSON, refused to anyone without players.view. The numbers
/// themselves are tested against a fixed clock in <c>ActivityReportsTests</c>; these pin the
/// function's own surface as the parser dispatches it.
/// </summary>
public class ActivityFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("summary")]
	[Arguments("drifting")]
	[Arguments("cohorts")]
	[Arguments("rooms")]
	[Arguments("hours")]
	public async Task EachReportIsJson(string report)
	{
		var result = await Eval($"activity({report},lit({{\"days\":7}}))");

		await Assert.That(result).DoesNotStartWith("#-1");
		using var document = JsonDocument.Parse(result);
		await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
	}

	[Test]
	public async Task AnUnknownReportIsAnError()
		=> await Assert.That(await Eval("activity(weather)")).IsEqualTo("#-1 NO SUCH REPORT");

	[Test]
	public async Task OptionsThatAreNotAnObjectAreAnError()
		=> await Assert.That(await Eval("activity(summary,7)")).IsEqualTo("#-1 OPTIONS MUST BE A JSON OBJECT");

	[Test]
	public async Task AMortalIsRefused()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ActivityMortal");
		try
		{
			await Assert.That(await EvalAs(mortal.DbRef, "activity(summary)")).StartsWith("#-1");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}
}
