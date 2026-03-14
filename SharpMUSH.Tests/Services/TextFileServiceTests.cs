using SharpMUSH.Implementation.Services;

namespace SharpMUSH.Tests.Services;

public class TextFileServiceTests
{
	[Test]
	public async Task StripConsecutiveHeaders_SingleHeader_ReturnsUnchanged()
	{
		var content = "# FUNCTION LIST\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}

	[Test]
	public async Task StripConsecutiveHeaders_TwoConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# FUNCTION LIST\n# FUNCTION TYPES\n  Several major variants of functions are available.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# FUNCTION LIST\n  Several major variants of functions are available.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_ThreeConsecutiveHeaders_KeepsOnlyFirst()
	{
		var content = "# TOPIC1\n# TOPIC2\n# TOPIC3\n  Body text here.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo("# TOPIC1\n  Body text here.");
	}

	[Test]
	public async Task StripConsecutiveHeaders_NoHeaders_ReturnsUnchanged()
	{
		var content = "Just some plain text\nwith multiple lines.";
		var result = TextFileService.StripConsecutiveHeaders(content);
		await Assert.That(result).IsEqualTo(content);
	}
}
