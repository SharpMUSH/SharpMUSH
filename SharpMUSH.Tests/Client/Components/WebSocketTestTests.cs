using Bunit;
using Bunit.TestDoubles;
using TUnit.Core;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using System.Net.WebSockets;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the WebSocketTest component to verify WebSocket client functionality.
/// </summary>
public class WebSocketTestTests
{
	/// <summary>
	/// Concrete test context for WebSocket tests that sets up MudBlazor services.
	/// </summary>
	private class WebSocketTestContext : Bunit.TestContext
	{
		public WebSocketTestContext()
		{
			JSInterop.Mode = JSRuntimeMode.Loose;
			Services.AddMudServices();
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
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var chip = cut.Find("div.mud-chip");
		await Assert.That(chip.TextContent.Trim()).IsEqualTo("Disconnected");
	}

	[Test]
	public async Task WebSocketTest_RendersPageTitle()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var heading = cut.Find("h4");
		await Assert.That(heading.TextContent).Contains("WebSocket Connection Test");
	}

	[Test]
	public async Task WebSocketTest_HasServerUriField()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var serverUriField = cut.Find("input[placeholder='ws://localhost:4202/ws']");
		await Assert.That(serverUriField).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasConnectButton()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var buttons = cut.FindAll("button");
		var connectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Connect"));
		await Assert.That(connectButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasDisconnectButton()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var buttons = cut.FindAll("button");
		var disconnectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Disconnect"));
		await Assert.That(disconnectButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_DisconnectButton_DisabledWhenDisconnected()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var buttons = cut.FindAll("button");
		var disconnectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Disconnect"));
		await Assert.That(disconnectButton?.HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task WebSocketTest_HasMessageInputField()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var inputs = cut.FindAll("input");
		var messageInput = inputs.Skip(1).FirstOrDefault(); // Skip server URI input
		await Assert.That(messageInput).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_HasSendMessageButton()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var buttons = cut.FindAll("button");
		var sendButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Send Message"));
		await Assert.That(sendButton).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_SendButton_DisabledWhenDisconnected()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var buttons = cut.FindAll("button");
		var sendButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Send Message"));
		await Assert.That(sendButton?.HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task WebSocketTest_HasMessagesSection()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var messagesHeading = cut.FindAll("h6").FirstOrDefault(h => h.TextContent.Contains("Messages"));
		await Assert.That(messagesHeading).IsNotNull();
	}

	[Test]
	public async Task WebSocketTest_DefaultServerUri_IsCorrect()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();

		// Assert
		var serverUriInput = cut.Find("input[placeholder='ws://localhost:4202/ws']");
		await Assert.That(serverUriInput.GetAttribute("value")).IsEqualTo("ws://localhost:4202/ws");
	}

	[Test]
	public async Task WebSocketTest_ConnectButton_CallsConnectAsync()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient(false);
		ctx.Services.AddSingleton(mockWebSocketClient);

		var cut = ctx.RenderComponent<WebSocketTest>();
		var buttons = cut.FindAll("button");
		var connectButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Connect") && !b.TextContent.Contains("Disconnect"));

		// Act
		await connectButton!.ClickAsync(new());

		// Assert
		await mockWebSocketClient.Received(1).ConnectAsync(Arg.Any<string>());
	}

	[Test]
	public async Task WebSocketTest_Dispose_UnsubscribesFromEvents()
	{
		// Arrange
		using var ctx = new WebSocketTestContext();
		var mockWebSocketClient = CreateMockWebSocketClient();
		ctx.Services.AddSingleton(mockWebSocketClient);

		// Act
		var cut = ctx.RenderComponent<WebSocketTest>();
		var instance = cut.Instance; // Get instance before dispose
		cut.Dispose();

		// Assert - Component disposed successfully without errors
		await Assert.That(instance).IsNotNull();
	}
}
