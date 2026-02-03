using System.Collections.Concurrent;
using System.Text;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for NotifyService batching behavior to ensure no extra newlines are added.
/// </summary>
public class NotifyServiceBatchingTests
{
	private static (NotifyService, List<TelnetOutputMessage>) CreateNotifyServiceWithCapture()
	{
		var publishedMessages = new List<TelnetOutputMessage>();
		var mockPublisher = Substitute.For<IMessageBus>();
		mockPublisher
			.When(x => x.Publish(Arg.Any<TelnetOutputMessage>(), Arg.Any<CancellationToken>()))
			.Do(callInfo => publishedMessages.Add(callInfo.Arg<TelnetOutputMessage>()));

		// Mock connection service - not needed for Notify(long handle, ...) tests
		var mockConnectionService = Substitute.For<IConnectionService>();

		var notifyService = new NotifyService(mockPublisher, mockConnectionService);
		return (notifyService, publishedMessages);
	}

	[Test]
	public async Task Notify_BatchesMultipleMessages_WithoutExtraNewlines()
	{
		// Arrange
		var (notifyService, publishedMessages) = CreateNotifyServiceWithCapture();

		// Act - Send multiple messages in quick succession (within 1ms batching window)
		await notifyService.Notify(1L, "Message 1", null);
		await notifyService.Notify(1L, "Message 2", null);
		await notifyService.Notify(1L, "Message 3", null);

		// Wait for the batching timer to flush (1ms + margin for timer overhead)
		await Task.Delay(50);

		// Assert
		await Assert.That(publishedMessages.Count).IsEqualTo(1);
		
		var message = publishedMessages[0];
		var output = Encoding.UTF8.GetString(message.Data);
		
		// Each message should have exactly one \r\n, not double newlines
		// Expected output: "Message 1\r\nMessage 2\r\nMessage 3\r\n"
		var expectedOutput = "Message 1\r\nMessage 2\r\nMessage 3\r\n";
		await Assert.That(output).IsEqualTo(expectedOutput);

		// Verify no double newlines exist
		await Assert.That(output.Contains("\r\n\r\n")).IsFalse();
	}

	[Test]
	public async Task Notify_SingleMessage_EndsWithSingleNewline()
	{
		// Arrange
		var (notifyService, publishedMessages) = CreateNotifyServiceWithCapture();

		// Act
		await notifyService.Notify(1L, "Single message", null);
		
		// Wait for the batching timer to flush
		await Task.Delay(50);

		// Assert
		await Assert.That(publishedMessages.Count).IsEqualTo(1);
		
		var message = publishedMessages[0];
		var output = Encoding.UTF8.GetString(message.Data);
		
		// Should end with exactly one \r\n
		await Assert.That(output).IsEqualTo("Single message\r\n");
		await Assert.That(output.Contains("\r\n\r\n")).IsFalse();
	}

	[Test]
	public async Task Notify_MessageAlreadyHasNewline_DoesNotDouble()
	{
		// Arrange
		var (notifyService, publishedMessages) = CreateNotifyServiceWithCapture();

		// Act - Send a message that already has a newline
		await notifyService.Notify(1L, "Message with newline\n", null);
		
		// Wait for the batching timer to flush
		await Task.Delay(50);

		// Assert
		await Assert.That(publishedMessages.Count).IsEqualTo(1);
		
		var message = publishedMessages[0];
		var output = Encoding.UTF8.GetString(message.Data);
		
		// NormalizeLineEnding should convert \n to \r\n and ensure exactly one at the end
		await Assert.That(output).IsEqualTo("Message with newline\r\n");
	}

	[Test]
	public async Task Notify_EmptyMessage_IsIgnored()
	{
		// Arrange
		var (notifyService, publishedMessages) = CreateNotifyServiceWithCapture();

		// Act
		await notifyService.Notify(1L, "", null);
		
		// Wait to ensure no flush happens
		await Task.Delay(50);

		// Assert - Empty messages should not be published
		await Assert.That(publishedMessages.Count).IsEqualTo(0);
	}
}
