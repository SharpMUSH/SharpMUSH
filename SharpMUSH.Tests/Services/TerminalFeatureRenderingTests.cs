using System.Net;
using System.Text;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Utilities;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What the rendering worker sends a terminal beyond colour: links where the terminal reads them, and
/// pictures drawn in a figure's cells where it draws them, sent to each connection once.
/// </summary>
public class TerminalFeatureRenderingTests
{
	/// <summary>A 1x1 opaque PNG.</summary>
	private static readonly byte[] Png = Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

	/// <summary>A 4x2 GIF of two frames: red for 100ms, then blue for 250ms.</summary>
	private static readonly byte[] MovingGif = Convert.FromBase64String(
		"R0lGODlhBAACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAABAACAAAIBwABCBwoMCAAIfkEARkAAQAsAAAAAAQAAgCBAAD/AAAAAAAAAAAACAcAAQgcKDAgADs=");

	private const string KittyStart = "\u001b_G";
	private const string KittyPlaceholder = "\U0010EEEE";

	private sealed class PictureHandler(HttpStatusCode status, byte[]? picture = null) : HttpMessageHandler
	{
		public int Requests;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref Requests);
			return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(picture ?? Png) });
		}
	}

	private static RenderContext Context(ProtocolCapabilities capabilities, string session = "s") =>
		new("telnet", capabilities, null, Handle: 7, SessionId: session);

	private static string FigureMarkup(string source) => MarkupTextSerializer.Serialize(ServerLayout.Build(
		new Figure(new ImageMarkup(source, "a cat"), MarkupText.Plain("(=^.^=)")), 40));

	private static async Task<string> RenderAsync(MarkupOutputRenderer renderer, string markup, RenderContext context) =>
		Encoding.UTF8.GetString((await renderer.RenderAsync(markup, context)).Data);

	[Test]
	public async Task Kitty_SendsThePictureOnceThenOnlyItsPlaceholders()
	{
		var handler = new PictureHandler(HttpStatusCode.OK);
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance, handler);
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());
		var context = Context(new ProtocolCapabilities(SupportsTruecolor: true, Pins: new TerminalPins(Graphics: TerminalGraphics.Kitty)));
		var markup = FigureMarkup("https://pictures.example/cat.png");

		var first = await RenderAsync(renderer, markup, context);
		var second = await RenderAsync(renderer, markup, context);
		var otherConnection = await RenderAsync(renderer, markup, Context(context.Capabilities, session: "other"));

		await Assert.That(first).Contains(KittyStart).And.Contains(KittyPlaceholder);
		await Assert.That(second).DoesNotContain(KittyStart).And.Contains(KittyPlaceholder);
		await Assert.That(otherConnection).Contains(KittyStart).Because("each terminal holds its own images");
		await Assert.That(handler.Requests).IsEqualTo(1).Because("the picture is fetched once for everyone");
	}

	[Test]
	[Arguments(TerminalGraphics.Kitty, false, "a=f", false)]
	[Arguments(TerminalGraphics.Kitty, true, "a=f", true)]
	[Arguments(TerminalGraphics.Iterm2, false, "R0lGOD", false)]
	[Arguments(TerminalGraphics.Iterm2, true, "R0lGOD", true)]
	public async Task AMovingPicture_PlaysOnlyWithAnimationOn(string graphics, bool animation, string moving, bool plays)
	{
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance,
			new PictureHandler(HttpStatusCode.OK, MovingGif));
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());
		var context = Context(new ProtocolCapabilities(SupportsTruecolor: true,
			Pins: new TerminalPins(Graphics: graphics, Animation: animation)));

		var text = await RenderAsync(renderer, FigureMarkup("https://pictures.example/cat.gif"), context);

		await Assert.That(text.Contains(moving)).IsEqualTo(plays);
		await Assert.That(text).DoesNotContain("(=^.^=)").Because("the first frame is drawn either way");
	}

	[Test]
	public async Task TurningAnimationOnOrOff_SendsThePictureAgain()
	{
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance,
			new PictureHandler(HttpStatusCode.OK, MovingGif));
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());
		var markup = FigureMarkup("https://pictures.example/cat.gif");
		RenderContext With(bool animation) => Context(new ProtocolCapabilities(SupportsTruecolor: true,
			Pins: new TerminalPins(Graphics: TerminalGraphics.Kitty, Animation: animation)));

		var still = await RenderAsync(renderer, markup, With(false));
		var moving = await RenderAsync(renderer, markup, With(true));
		var stillAgain = await RenderAsync(renderer, markup, With(false));

		await Assert.That(still).Contains(KittyStart).And.DoesNotContain("a=f");
		await Assert.That(moving).Contains("a=f").Because("the terminal holds the still picture, not its frames");
		await Assert.That(stillAgain).Contains(KittyStart).And.DoesNotContain("a=f")
			.Because("the still picture replaces the moving one, which would otherwise keep playing");
	}

	[Test]
	public async Task AMovingPicture_CountsEveryFrameAgainstTheCache()
	{
		var picture = TerminalPictureStore.Decode(MovingGif);

		await Assert.That(TerminalPictureStore.SizeOf(picture)).IsEqualTo(2L * 4 * 2 * 4);
	}

	[Test]
	public async Task APictureThatCannotBeFetched_IsItsTextArt()
	{
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance,
			new PictureHandler(HttpStatusCode.NotFound));
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());
		var context = Context(new ProtocolCapabilities(Pins: new TerminalPins(Graphics: TerminalGraphics.Kitty)));

		var text = await RenderAsync(renderer, FigureMarkup("https://pictures.example/missing.png"), context);

		await Assert.That(text).Contains("(=^.^=)").And.DoesNotContain(KittyPlaceholder);
	}

	[Test]
	public async Task ARelativeAddress_IsNotFetchedWithoutAnImageBaseAddress()
	{
		var handler = new PictureHandler(HttpStatusCode.OK);
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance, handler);

		await Assert.That(store.TryGet("/pictures/cat.png", out _, out var pending)).IsFalse();
		await Assert.That(pending is null).IsTrue();
		await Assert.That(handler.Requests).IsEqualTo(0);
	}

	[Test]
	public async Task OnlyTheGamesExactOrigin_IsFetchedWithoutTheGuard()
	{
		var guarded = new PictureHandler(HttpStatusCode.OK);
		var local = new PictureHandler(HttpStatusCode.OK);
		using var store = new TerminalPictureStore("http://game.example:8080/", 1 << 20,
			NullLogger<TerminalPictureStore>.Instance, guarded, local);

		foreach (var source in new[]
			{
				"/pictures/cat.png", "https://game.example:8080/cat.png", "http://game.example:9000/cat.png",
				"http://game.example/cat.png"
			})
		{
			store.TryGet(source, out _, out var pending);
			if (pending is not null) await pending;
		}

		await Assert.That(local.Requests).IsEqualTo(1).Because("only the relative address is on the game's origin");
		await Assert.That(guarded.Requests).IsEqualTo(3).Because("another scheme or port is someone else's server");
	}

	[Test]
	public async Task FailedAddresses_AreForgottenPastTheLimit()
	{
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance,
			new PictureHandler(HttpStatusCode.NotFound));

		for (var i = 0; i < TerminalPictureStore.MaxFailures + 50; i++)
		{
			store.TryGet($"https://pictures.example/{i}.png", out _, out var pending);
			if (pending is not null) await pending;
		}

		await Assert.That(store.AddressCount).IsLessThanOrEqualTo(TerminalPictureStore.MaxFailures + 1);
	}

	/// <summary>An 80x40 opaque PNG: eight cells by two at the default cell size.</summary>
	private static readonly byte[] WidePng = Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAFAAAAAoCAIAAADmAupWAAAAQ0lEQVR42u3PMQ0AAAgDsMmZfxXIwgUPTWqgmfaVCAsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsL31p8N6zEucp5cQAAAABJRU5ErkJggg==");

	private static string ArtlessFigureMarkup(string source) => MarkupTextSerializer.Serialize(ServerLayout.Build(
		new Figure(new ImageMarkup(source, "a cat"), MarkupText.Empty), 40));

	/// <summary>
	/// An MXP client draws the picture itself, but in the cells the figure keeps for it: those are the picture's
	/// shape once the server knows its size, rather than one line of its description.
	/// </summary>
	[Test]
	public async Task Mxp_AFigureKeepsCellsOfThePicturesShape()
	{
		var handler = new PictureHandler(HttpStatusCode.OK, WidePng);
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance, handler);
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());

		var text = await RenderAsync(renderer, ArtlessFigureMarkup("https://pictures.example/cat.png"),
			Context(new ProtocolCapabilities(Format: OutputFormat.Mxp)));

		var lines = text.Split("\r\n");
		await Assert.That(lines.Length).IsEqualTo(2).Because("the picture is two cells tall");
		await Assert.That(lines[0]).Contains("<IMAGE cat.png");
		await Assert.That(lines.Count(line => line.Contains("<IMAGE"))).IsEqualTo(1);
		await Assert.That(handler.Requests).IsEqualTo(1);
	}

	[Test]
	public async Task Mxp_AClientThatDoesNotDrawImages_FetchesNothing()
	{
		var handler = new PictureHandler(HttpStatusCode.OK, WidePng);
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance, handler);
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());

		var text = await RenderAsync(renderer, ArtlessFigureMarkup("https://pictures.example/cat.png"),
			Context(new ProtocolCapabilities(Format: OutputFormat.Mxp, MxpSupported: "SEND")));

		await Assert.That(text).Contains("[a cat]");
		await Assert.That(handler.Requests).IsEqualTo(0);
	}

	[Test]
	public async Task NoPictureFeature_LeavesTheArtAndFetchesNothing()
	{
		var handler = new PictureHandler(HttpStatusCode.OK);
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance, handler);
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());

		var text = await RenderAsync(renderer, FigureMarkup("https://pictures.example/cat.png"),
			Context(new ProtocolCapabilities()));

		await Assert.That(text).Contains("(=^.^=)");
		await Assert.That(handler.Requests).IsEqualTo(0);
	}

	[Test]
	public async Task Links_FollowTheConnectionsFeatures()
	{
		var line = MarkupText.Concat([
			MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "https://example.com/"), "site"),
			MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "look", linkKind: LinkKind.Command), "look")]);
		var markup = MarkupTextSerializer.Serialize(line);
		var renderer = new MarkupOutputRenderer();

		var plain = Encoding.UTF8.GetString(renderer.Render(markup, Context(new ProtocolCapabilities())).Data);
		var linked = Encoding.UTF8.GetString(renderer.Render(markup,
			Context(new ProtocolCapabilities(Pins: new TerminalPins(Hyperlinks: true, CommandLinks: true)))).Data);

		await Assert.That(plain).DoesNotContain("\u001b]8;").And.DoesNotContain("\u001b]68;");
		await Assert.That(linked).Contains("\u001b]8;;https://example.com/").And.Contains("\u001b]68;1;SEND;look");
	}

	[Test]
	[Arguments("8.8.8.8", true)]
	[Arguments("2606:4700:4700::1111", true)]
	[Arguments("127.0.0.1", false)]
	[Arguments("10.1.2.3", false)]
	[Arguments("172.20.0.5", false)]
	[Arguments("192.168.1.1", false)]
	[Arguments("169.254.169.254", false)]
	[Arguments("100.64.0.1", false)]
	[Arguments("::1", false)]
	[Arguments("fd00::1", false)]
	[Arguments("fe80::1", false)]
	[Arguments("::ffff:127.0.0.1", false)]
	public async Task OnlyPublicAddressesAreFetched(string address, bool allowed) =>
		await Assert.That(TerminalPictureStore.IsPublic(IPAddress.Parse(address))).IsEqualTo(allowed);

	[Test]
	public async Task Decode_ShrinksALargePictureKeepingItsShape()
	{
		var rgba = new byte[1024 * 256 * 4];
		Array.Fill<byte>(rgba, 255);

		var (width, height, pixels) = TerminalPictureStore.Shrink(1024, 256, rgba, TerminalPictureStore.MaxStoredSide);

		await Assert.That(width).IsEqualTo(512);
		await Assert.That(height).IsEqualTo(128);
		await Assert.That(pixels.Length).IsEqualTo(512 * 128 * 4);
		await Assert.That(pixels.All(b => b == 255)).IsTrue();
		await Assert.That(TerminalPictureStore.Decode(Png).Width).IsEqualTo(1);
	}

	[Test]
	public async Task AGifOfSeveralFrames_IsAMovingPicture()
	{
		var picture = TerminalPictureStore.Decode(MovingGif);

		await Assert.That(picture.Frames.Count).IsEqualTo(2);
		await Assert.That(picture.Frames.Select(frame => frame.Duration.TotalMilliseconds)).IsEquivalentTo(new[] { 100.0, 250.0 });
		await Assert.That(picture.Frames[0].Rgba.Span[..4].ToArray()).IsEquivalentTo(new byte[] { 255, 0, 0, 255 });
		await Assert.That(picture.Frames[1].Rgba.Span[..4].ToArray()).IsEquivalentTo(new byte[] { 0, 0, 255, 255 });
		await Assert.That(TerminalPictureStore.Decode(Png).Frames).IsEmpty();
	}
}
