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
}
