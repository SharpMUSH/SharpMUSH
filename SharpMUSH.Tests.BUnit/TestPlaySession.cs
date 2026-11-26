using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Services.Play;

namespace SharpMUSH.Tests.BUnit;

/// <summary>A play session over a terminal that never connects, for pages that inject one and do not play.</summary>
internal static class TestPlaySession
{
	public static PlaySession Create() =>
		new(new TerminalServiceHost(() =>
			{
				var terminal = Substitute.For<ITerminalService>();
				terminal.OobChannels.Returns(new OobChannelStore());
				return terminal;
			}),
			() => Task.CompletedTask, () => false, NullLogger<PlaySession>.Instance);
}
