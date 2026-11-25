using System.Text;
using System.Text.Json;
using SharpMUSH.Client.Services.Play;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Tests.Client.Play;

/// <summary>What a hosted application's frame receives in a <c>play-event</c>, and what its ui.json may declare.</summary>
public class HostedPlayEventsTests
{
	private static readonly PlayState Playing = new(new PlayCharacter("#12:1", "Tamsin", false), PlayConnection.Connected, "#40:1", 2);

	[Test]
	public async Task APackageCarriesItsPayloadAndWhereItCameFrom()
	{
		using var data = JsonDocument.Parse("""{"v":2,"n":1}""");
		var json = HostedPlayEvents.ToJson(new PlayPackage(2, "myapp.event", 2, data.RootElement.Clone(), PlayOrigin.CatchUp,
			PlaySender.Self, "#12:1"))!.ToJsonString();

		await Assert.That(json).IsEqualTo(
			"""{"kind":"package","generation":2,"package":"myapp.event","version":2,"data":{"v":2,"n":1},"origin":"catchUp","sender":"self","senderObjId":"#12:1"}""");
	}

	[Test]
	public async Task AResetNamesItsReason()
	{
		await Assert.That(HostedPlayEvents.ToJson(new PlayReset(3, PlayResetReason.CharacterChanged))!.ToJsonString())
			.IsEqualTo("""{"kind":"reset","generation":3,"reason":"characterChanged"}""");
	}

	[Test]
	public async Task AStateChangeCarriesBothStates()
	{
		var json = HostedPlayEvents.ToJson(new PlayStateChanged(2, PlayState.NotPlaying with { Generation = 2 }, Playing))!.ToJsonString();

		await Assert.That(json).IsEqualTo(
			"""{"kind":"state","generation":2,"previous":{"generation":2,"connection":"notPlaying","room":null,"character":null},"current":{"generation":2,"connection":"connected","room":"#40:1","character":{"objId":"#12:1","name":"Tamsin","guest":false}}}""");
	}

	private static byte[] Ui(string subscribes) => Encoding.UTF8.GetBytes(
		$$"""{"version":1,"id":"myapp","entry":"index.html","configVersion":1,"configSchema":{"type":"object"},"subscribes":{{subscribes}}}""");

	[Test]
	public async Task AUiDefinitionDeclaresThePackagesItReads()
	{
		await Assert.That(UiDefinition.Parse(Ui("""["myapp.*","other.exact"]""")).Subscribes).IsEquivalentTo(["myapp.*", "other.exact"]);
	}

	[Test]
	[Arguments("""["sharp.play"]""")]
	[Arguments("""["*"]""")]
	[Arguments("""["query.*"]""")]
	public async Task AUiDefinitionMayNotDeclareReservedOrWildPackages(string subscribes)
	{
		await Assert.That(() => UiDefinition.Parse(Ui(subscribes))).Throws<FormatException>();
	}
}
