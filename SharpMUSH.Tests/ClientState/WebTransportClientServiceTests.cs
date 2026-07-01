using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.ClientState;

public class WebTransportClientServiceTests
{
	private static IJSRuntime JsWithSupport(bool supported)
	{
		var js = Substitute.For<IJSRuntime>();
		js.InvokeAsync<bool>(
				Arg.Is<string>(s => s == "sharpWebTransport.isSupported"),
				Arg.Any<object?[]?>())
			.Returns(new ValueTask<bool>(supported));
		return js;
	}

	[Test]
	public async Task ConnectAsync_throws_when_browser_lacks_WebTransport()
	{
		var svc = new WebTransportClientService(JsWithSupport(false), NullLogger<WebTransportClientService>.Instance);

		await Assert.That(async () => await svc.ConnectAsync("https://h:4203/wt"))
			.Throws<NotSupportedException>();
		await Assert.That(svc.IsConnected).IsFalse();
	}

	[Test]
	public async Task OnFrame_raises_MessageReceived()
	{
		var svc = new WebTransportClientService(JsWithSupport(true), NullLogger<WebTransportClientService>.Instance);
		string? received = null;
		svc.MessageReceived += (_, text) => received = text;

		svc.OnFrame("hello world");

		await Assert.That(received).IsEqualTo("hello world");
	}

	[Test]
	public async Task Kind_is_webtransport()
	{
		var svc = new WebTransportClientService(JsWithSupport(true), NullLogger<WebTransportClientService>.Instance);
		await Assert.That(svc.Kind).IsEqualTo("webtransport");
	}
}
