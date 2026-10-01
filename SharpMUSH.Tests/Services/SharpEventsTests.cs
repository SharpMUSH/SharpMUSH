using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

public class SharpEventsTests
{
	[Test]
	public async Task RoomContentsEventNameMatchesPennMushAttributeFormat()
	{
		var roomContents = SharpEvents.RoomContents;
		await Assert.That(roomContents).IsEqualTo("ROOM`CONTENTS");
	}

	/// <summary>
	/// The comm-feed package's handler attributes are named after these, and help event channel/page/player
	/// documents them by these names; a rename here would silently disconnect both.
	/// </summary>
	[Test]
	public async Task CommEventNamesMatchTheirHandlerAttributes()
	{
		await Assert.That(SharpEvents.ChannelMessage).IsEqualTo("CHANNEL`MESSAGE");
		await Assert.That(SharpEvents.PageMessage).IsEqualTo("PAGE`MESSAGE");
		await Assert.That(SharpEvents.PlayerChannels).IsEqualTo("PLAYER`CHANNELS");
	}
}
