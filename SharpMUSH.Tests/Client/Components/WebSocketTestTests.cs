using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the WebSocketTest component to verify WebSocket client functionality.
/// </summary>
public class WebSocketTestTests
{
	/// <summary>
	/// Concrete test context for WebSocket tests that sets up MudBlazor services.
	/// </summary>
	private class WebSocketTestContext : BunitContext
	{
		public WebSocketTestContext()
		{
			JSInterop.Mode = JSRuntimeMode.Loose;
			Services.AddMudServices();
			Services.AddLocalization();
		}
	}

	private IWebSocketClientService CreateMockWebSocketClient(bool isConnected = false)
	{
		var mock = Substitute.For<IWebSocketClientService>();
		mock.IsConnected.Returns(isConnected);
		return mock;
	}

	[Test]
	public async Task WebSocketTest_InitialState_ShowsDisconnected()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var chip = cut.Find("div.mud-chip");
		await Assert.That(chip.TextContent.Trim()).IsEqualTo("Disconnected");
	}

	[Test]
	public async Task WebSocketTest_RendersPageTitle()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var heading = cut.Find("h4");
		await Assert.That(heading.TextContent).Contains("WebSocket Connection Test");
	}

	[Test]
	public async Task WebSocketTest_HasServerUriField()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var serverUriField = cut.Find("input[placeholder='ws://localhost:4202/ws']");
		await Assert.That(serverUriField).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasConnectButton()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var buttons = cut.FindAll("button");
		var connectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Connect"));
		await Assert.That(connectButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasDisconnectButton()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var buttons = cut.FindAll("button");
		var disconnectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Disconnect"));
		await Assert.That(disconnectButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_DisconnectButton_DisabledWhenDisconnected()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var buttons = cut.FindAll("button");
		var disconnectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Disconnect"));
		await Assert.That(disconnectButton?.HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task WebSocketTest_HasMessageInputField()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var inputs = cut.FindAll("input");
		var messageInput = inputs.Skip(1).FirstOrDefault();
		await Assert.That(messageInput).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasSendMessageButton()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var buttons = cut.FindAll("button");
		var sendButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Send Message"));
		await Assert.That(sendButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_SendButton_DisabledWhenDisconnected()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var buttons = cut.FindAll("button");
		var sendButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Send Message"));
		await Assert.That(sendButton?.HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task WebSocketTest_HasMessagesSection()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var messagesHeading = cut.FindAll("h6").FirstOrDefault(h => h.TextContent.Contains("Messages"));
		await Assert.That(messagesHeading).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_DefaultServerUri_IsCorrect()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();

		var serverUriInput = cut.Find("input[placeholder='ws://localhost:4202/ws']");
		await Assert.That(serverUriInput.GetAttribute("value")).IsEqualTo("ws://localhost:4202/ws");
	}

	[Test]
	public async Task WebSocketTest_ConnectButton_CallsConnectAsync()
	{
		await using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.Render<WebSocketTest>();
		var buttons = cut.FindAll("button");
		var connectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Connect") && !b.TextContent.Contains("Disconnect"));

		await connectButton!.ClickAsync(new());

		await mockWebSocketClient.Received(1).ConnectAsync(Arg.Any<string>());
	}

}
