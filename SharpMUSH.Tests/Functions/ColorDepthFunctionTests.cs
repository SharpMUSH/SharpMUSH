using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// TinyMUX's colordepth(): the colour depth a connection is sent, on TinyMUX's 0, 4, 8, 24 scale, read the way
/// terminfo() reads the colour style.
/// </summary>
public class ColorDepthFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService Connections => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private Task<DBRef> PlayerAsync(string prefix) => TestIsolationHelpers.CreateTestPlayerAsync(
		WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<IMediator>(), prefix);

	private Task<long> ConnectAsync(DBRef player, string colorStyle) => TestIsolationHelpers.ConnectTestHandleAsync(
		Connections, player, "telnet", new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "telnet",
			[TerminalCapabilityReader.ColorStyleKey] = colorStyle
		}));

	private async Task<string> EvalAsync(DBRef executor, string code)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	[Test]
	[Arguments(ColorStyles.Plain, "0")]
	[Arguments(ColorStyles.Hilite, "0")]
	[Arguments(ColorStyles.SixteenColor, "4")]
	[Arguments(ColorStyles.Xterm256, "8")]
	[Arguments(ColorStyles.Truecolor, "24")]
	public async Task ReportsTheDepthSent(string colorStyle, string expected)
	{
		var player = await PlayerAsync("ColorDepth");
		var handle = await ConnectAsync(player, colorStyle);
		try
		{
			await Assert.That(await EvalAsync(player, "colordepth(me)")).IsEqualTo(expected);
			await Assert.That(await EvalAsync(player, $"colordepth({handle})")).IsEqualTo(expected);
			await Assert.That(await EvalAsync(new DBRef(1), $"colordepth(#{player.Number})")).IsEqualTo(expected);
		}
		finally
		{
			await Connections.Disconnect(handle);
		}
	}

	[Test]
	public async Task OnlyThePlayerAndSeeAllMayAsk()
	{
		var player = await PlayerAsync("ColorDepthOwner");
		var other = await PlayerAsync("ColorDepthOther");
		var handle = await ConnectAsync(player, ColorStyles.Xterm256);
		try
		{
			await Assert.That(await EvalAsync(other, $"colordepth(#{player.Number})")).StartsWith("#-1");
		}
		finally
		{
			await Connections.Disconnect(handle);
		}
	}

	[Test]
	public async Task NotConnected()
	{
		var player = await PlayerAsync("ColorDepthAway");
		await Assert.That(await EvalAsync(new DBRef(1), $"colordepth(#{player.Number})")).IsEqualTo("#-1 NOT CONNECTED");
	}
}
