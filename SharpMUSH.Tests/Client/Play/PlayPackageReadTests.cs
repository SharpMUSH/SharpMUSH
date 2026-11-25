using System.Text.Json;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Tests.Client.Play;

/// <summary>
/// A payload with fields added, fields missing, a wrong type or a new version comes back from <c>Read</c> as a value to
/// test, never as an exception.
/// </summary>
public class PlayPackageReadTests
{
	public sealed record Counter(int? Count, string? Label);

	private static PlayPackage Package(string json)
	{
		using var document = JsonDocument.Parse(json);
		var data = document.RootElement.Clone();
		return new PlayPackage(1, "myapp.counter", PlayPackage.VersionOf(data), data, PlayOrigin.Live, PlaySender.Other, "#7:1");
	}

#pragma warning disable IL2026, IL3050 // The reflection overload is the one most apps call; the test runs on the JIT.
	private static PlayRead<Counter> Read(string json, int min = 1, int max = int.MaxValue) =>
		Package(json).Read<Counter>(min, max);
#pragma warning restore IL2026, IL3050

	[Test]
	public async Task UnknownFieldsAreSkippedAndMissingOnesKeepTheirDefault()
	{
		var read = Read("""{"count":3,"added":"later","nested":{"x":1}}""");

		await Assert.That(read is Counter { Count: 3, Label: null }).IsTrue();
	}

	[Test]
	public async Task NamesInAnyCaseAndNumbersInStringsAreRead()
	{
		await Assert.That(Read("""{"COUNT":"4","Label":"four"}""") is Counter { Count: 4, Label: "four" }).IsTrue();
	}

	[Test]
	public async Task AWrongTypeNamesItsPath()
	{
		var read = Read("""{"count":{"not":"a number"}}""");

		await Assert.That(read is PlayUnreadable { Path: "$.count" }).IsTrue();
	}

	[Test]
	public async Task ANullPayloadIsUnreadable()
	{
		await Assert.That(Read("null") is PlayUnreadable { Path: "$" }).IsTrue();
	}

	[Test]
	public async Task AVersionOutsideTheRangeIsReportedWithoutReading()
	{
		var read = Read("""{"v":3,"count":"not read"}""", 1, 2);

		await Assert.That(read is PlayWrongVersion { Version: 3 }).IsTrue();
	}

	[Test]
	public async Task AVersionInsideTheRangeIsRead()
	{
		await Assert.That(Read("""{"v":2,"count":5}""", 1, 2) is Counter { Count: 5 }).IsTrue();
	}

	[Test]
	[Arguments("""{"count":1}""", 1)]
	[Arguments("""{"v":4}""", 4)]
	[Arguments("""{"v":"2"}""", 0)]
	[Arguments("""{"v":-1}""", 0)]
	[Arguments("""{"v":1.5}""", 0)]
	[Arguments("""[1,2]""", 1)]
	[Arguments("\"text\"", 1)]
	public async Task TheVersionIsTheTopLevelV(string json, int expected)
	{
		using var document = JsonDocument.Parse(json);

		await Assert.That(PlayPackage.VersionOf(document.RootElement)).IsEqualTo(expected);
	}

	[Test]
	public async Task NoPayloadAtAllIsUnreadable()
	{
		var package = new PlayPackage(1, "myapp.empty", 1, default, PlayOrigin.Live, PlaySender.Unknown, null);

#pragma warning disable IL2026, IL3050
		await Assert.That(package.Read<Counter>() is PlayUnreadable).IsTrue();
#pragma warning restore IL2026, IL3050
	}

	[Test]
	[Arguments("myapp.private", true)]
	[Arguments("myapp.*", true)]
	[Arguments("Char.Vitals", true)]
	[Arguments("my-app_2.x", true)]
	[Arguments("*", false)]
	[Arguments(".*", false)]
	[Arguments("myapp.*.x", false)]
	[Arguments("my*", false)]
	[Arguments("query.*", false)]
	[Arguments("sharp.play", false)]
	[Arguments("SHARP.*", false)]
	[Arguments("sharp", false)]
	[Arguments("sharpie.x", true)]
	[Arguments(".hidden", false)]
	[Arguments("trailing.", false)]
	[Arguments("has space", false)]
	[Arguments("", false)]
	public async Task OnlyExactNamesAndDotStarPrefixesArePatterns(string pattern, bool expected)
	{
		await Assert.That(PlayPackageNames.IsPattern(pattern)).IsEqualTo(expected);
	}
}
