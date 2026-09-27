using System.Net;
using System.Text;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The editor's help index over <c>mush-defs.json</c>, where a function and a command can share a
/// name: <c>idle()</c> and <c>IDLE</c>, <c>version()</c> and <c>VERSION</c>.
/// </summary>
public class HelpServiceTests
{
	private const string Definitions = """
		{
		  "functions": { "idle": { "helpFull": "function help", "helpPreview": "", "minArgs": 1, "maxArgs": 2, "parameterNames": [] } },
		  "commands": { "IDLE": { "helpFull": "command help", "helpPreview": "", "minArgs": 0, "maxArgs": 0, "parameterNames": [], "switches": [] } }
		}
		""";

	private sealed class DefinitionsHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(Definitions, Encoding.UTF8, "application/json")
			});
	}

	private static async Task<HelpService> Loaded()
	{
		var help = new HelpService(new HttpClient(new DefinitionsHandler()) { BaseAddress = new Uri("http://localhost/") });
		await help.EnsureLoadedAsync();
		return help;
	}

	[Test]
	public async Task AFunctionIsReachableByItsKeyWhenACommandSharesItsName()
	{
		var help = await Loaded();

		var function = help.Get("IDLE()");

		await Assert.That(function).IsNotNull();
		await Assert.That(function!.IsCommand).IsFalse();
		await Assert.That(function.HelpFull).IsEqualTo("function help");
		await Assert.That(help.Get(function.Key)).IsSameReferenceAs(function);
	}

	[Test]
	public async Task TheBareNameIsTheCommand()
		=> await Assert.That((await Loaded()).Get("IDLE")?.HelpFull).IsEqualTo("command help");

	[Test]
	public async Task EachEntryIsListedOnce()
		=> await Assert.That((await Loaded()).AllEntries.Select(e => e.Key)).IsEquivalentTo(["IDLE()", "IDLE"]);
}
