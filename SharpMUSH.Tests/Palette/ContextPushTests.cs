using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Palette;

namespace SharpMUSH.Tests.Palette;

/// <summary>
/// <c>@context/push</c>'s right side: a JSON object's own commas do not split it, and the info takes the
/// rest of the line, commas and all.
/// </summary>
public class ContextPushTests
{
	[Test]
	public async ValueTask ANameAlone_PushesNoValues()
	{
		var push = ContextPush.Parse("Scene_Here").Expect<ContextPush>();
		await Assert.That(push.Name).IsEqualTo("scene_here");
		await Assert.That(push.Values).IsEmpty();
		await Assert.That(push.Lifetime).IsNull();
		await Assert.That(push.Info).IsNull();
	}

	[Test]
	public async ValueTask JsonValues_KeepTheirCommas_AndTheInfoKeepsItsOwn()
	{
		var push = ContextPush.Parse("""combat,{"encounter":"7","round":3},60s,The ogre swings, then waits.""").Expect<ContextPush>();
		await Assert.That(push.Values["encounter"]).IsEqualTo("7");
		await Assert.That(push.Values["round"]).IsEqualTo("3");
		await Assert.That(push.Lifetime).IsEqualTo(TimeSpan.FromSeconds(60));
		await Assert.That(push.Info).IsEqualTo("The ogre swings, then waits.");
	}

	[Test]
	public async ValueTask EmptyValuesAndLifetime_MayBeLeftOut()
	{
		var push = ContextPush.Parse("my_turn,,,Queue a defence within 60 seconds.").Expect<ContextPush>();
		await Assert.That(push.Values).IsEmpty();
		await Assert.That(push.Lifetime).IsNull();
		await Assert.That(push.Info).IsEqualTo("Queue a defence within 60 seconds.");
	}

	[Test]
	[Arguments("bad name", ContextPush.InvalidName)]
	[Arguments("ok,[1,2]", ContextPush.InvalidValues)]
	[Arguments("""ok,{"a":{"b":1}}""", ContextPush.InvalidValues)]
	[Arguments("""ok,{"a":1""", ContextPush.InvalidValues)]
	[Arguments("ok,,soon", ContextPush.InvalidLifetime)]
	[Arguments("ok,,2d", ContextPush.InvalidLifetime)]
	public async ValueTask Refusals_SayWhy(string text, string error)
	{
		await Assert.That(ContextPush.Parse(text).Expect<Error<string>>().Value).IsEqualTo(error);
	}
}
