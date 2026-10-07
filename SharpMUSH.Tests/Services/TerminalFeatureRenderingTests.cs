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

	private const string KittyStart = "\u001b_G";
	private const string KittyPlaceholder = "\U0010EEEE";

	private sealed class PictureHandler(HttpStatusCode status) : HttpMessageHandler
	{
		public int Requests;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref Requests);
			return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Png) });
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
		var context = Context(new ProtocolCapabilities(SupportsTruecolor: true, GraphicsPin: TerminalGraphics.Kitty));
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
	public async Task APictureThatCannotBeFetched_IsItsTextArt()
	{
		using var store = new TerminalPictureStore(null, 1 << 20, NullLogger<TerminalPictureStore>.Instance,
			new PictureHandler(HttpStatusCode.NotFound));
		var renderer = new MarkupOutputRenderer(store, new ConnectionPictures());
		var context = Context(new ProtocolCapabilities(GraphicsPin: TerminalGraphics.Kitty));

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
			Context(new ProtocolCapabilities(HyperlinksPin: true, CommandLinksPin: true))).Data);

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
}
