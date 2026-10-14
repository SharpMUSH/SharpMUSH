using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services.Webhooks;

/// <summary>
/// The event service as the engine uses it: every event is offered to the webhook subscriptions, then
/// queued for the event handler as before. Webhooks hear an event whether or not the handler has an
/// attribute for it.
/// </summary>
public sealed class WebhookPublishingEventService(
	IEventService inner,
	IWebhookService webhooks,
	ILogger<WebhookPublishingEventService> logger) : IEventService
{
	/// <inheritdoc />
	public async ValueTask TriggerEventAsync(string eventName, DBRef? enactor, params string[] args)
	{
		await PublishAsync(eventName, enactor, args);
		await inner.TriggerEventAsync(eventName, enactor, args);
	}

	/// <inheritdoc />
	/// <remarks>A webhook's body is JSON for programs, so markup arguments go to it as plain text.</remarks>
	public async ValueTask TriggerEventAsync(string eventName, DBRef? enactor, IReadOnlyList<MString> args)
	{
		await PublishAsync(eventName, enactor, args.Select(arg => arg.ToPlainText()).ToArray());
		await inner.TriggerEventAsync(eventName, enactor, args);
	}

	private async ValueTask PublishAsync(string eventName, DBRef? enactor, IReadOnlyList<string> args)
	{
		try
		{
			await webhooks.PublishAsync(eventName, enactor, args);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// A webhook failure must not break the code that raised the event, nor keep it from the handler.
			logger.LogError(ex, "Error offering event {EventName} to webhooks", eventName);
		}
	}
}
