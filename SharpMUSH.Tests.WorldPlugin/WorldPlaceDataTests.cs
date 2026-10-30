using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Tests.WorldPlugin;

/// <summary>A place's JSON data: merging, what a reader who may not examine sees, and the pieces the map draws.</summary>
public class WorldPlaceDataTests
{
	[Test]
	public async Task ATextDrawingShowsCharThenAShortIconThenTheLabelsFirstLetter()
	{
		await Assert.That(new WorldTerrain("forest", "Old forest", null, "forest", null, "♣").Glyph).IsEqualTo("♣");
		await Assert.That(new WorldTerrain("water", "Deep water", null, "~", null).Glyph).IsEqualTo("~");
		await Assert.That(new WorldTerrain("forest", "old forest", null, "forest", null).Glyph).IsEqualTo("O")
			.Because("an icon the portal draws by name is not a character");
	}

	[Test]
	public async Task AMergeRemovesNullsAndMergesObjects()
	{
		var merged = WorldPlaceData.Merge(WorldPlaceData.Parse("""{"a":1,"render":{"icon":"x","tone":"red"},"gone":true}"""),
			WorldPlaceData.Parse("""{"gone":null,"render":{"tone":null,"label":"Ford"},"b":[1]}""")!);
		await Assert.That(merged.ToJsonString()).IsEqualTo("""{"a":1,"render":{"icon":"x","label":"Ford"},"b":[1]}""");
	}

	[Test]
	public async Task OnlyRenderAndInfoShowToAReaderWhoMayNotExamine()
	{
		const string data = """{"secret":"gold","render":{"icon":"*"},"info":{"Danger":"Low"}}""";
		await Assert.That(WorldPlaceData.Visible(data, examine: true)).IsEqualTo(data);
		await Assert.That(WorldPlaceData.Visible(data, examine: false)).IsEqualTo("""{"render":{"icon":"*"},"info":{"Danger":"Low"}}""");
		await Assert.That(WorldPlaceData.Visible("""{"secret":1}""", examine: false)).IsNull();
	}

	[Test]
	public async Task RenderInfoAndHiddenReadTheirKeys()
	{
		var data = WorldPlaceData.Parse("""{"render":{"icon":" ! ","hidden":true},"info":[{"label":"Depth","value":3}]}""");
		await Assert.That(WorldPlaceData.Render(data, "icon")).IsEqualTo("!");
		await Assert.That(WorldPlaceData.Render(data, "tone")).IsNull();
		await Assert.That(WorldPlaceData.Hidden(data)).IsTrue();
		var info = WorldPlaceData.Info(data);
		await Assert.That(info.Count).IsEqualTo(1);
		await Assert.That((info[0].Label, info[0].Value)).IsEqualTo(("Depth", "3"));
	}

	[Test]
	[Arguments("1,2", 1, 2, 0)]
	[Arguments(" -5 , 7 , 3 ", -5, 7, 3)]
	public async Task APositionIsTwoOrThreeWholeNumbers(string text, int x, int y, int z) =>
		await Assert.That(WorldPosition.Parse(text)).IsEqualTo(new WorldPosition(x, y, z));

	[Test]
	[Arguments("1")]
	[Arguments("1,2,3,4")]
	[Arguments("1.5,2")]
	[Arguments("1000001,0")]
	public async Task AnythingElseIsNoPosition(string text) =>
		await Assert.That(WorldPosition.Parse(text)).IsNull();

	[Test]
	[Arguments(-1, -1, 31)]
	[Arguments(31, 0, 31)]
	[Arguments(32, 1, 0)]
	[Arguments(-33, -2, 31)]
	public async Task APlaceSplitsIntoItsTileAndColumn(int x, int tile, int offset) =>
		await Assert.That(WorldGrid.Split(x)).IsEqualTo((tile, offset));
}
