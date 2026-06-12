using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Unit tests for the real <see cref="NotifyService"/>'s HTTP output capture: while an inbound
/// HTTP request runs the http_handler's softcode, output emitted to the handler must be appended
/// to <see cref="HttpResponseContext.Body"/> instead of being published to connections —
/// PennMUSH's CONN_HTTP_BUFFER hijack. (Integration paths use a substituted INotifyService, so
/// this is the only place the real service's capture line is exercised.)
/// </summary>
public class NotifyServiceHttpCaptureTests
{
	private static (NotifyService Service, IMessageBus Bus) BuildService()
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>())
			.Returns(AsyncEnumerable.Empty<IConnectionService.ConnectionData>());

		var service = new NotifyService(
			bus, connections, Substitute.For<ILocalizationService>(),
			listenerRoutingService: null, mediator: null, httpOutputCapture: new HttpOutputCapture());
		return (service, bus);
	}

	[Test]
	public async Task Notify_DuringCapture_AppendsToBodyAndSkipsPublish()
	{
		var (service, bus) = BuildService();
		var capture = new HttpOutputCapture(); // AsyncLocal state is shared across instances.
		var context = new HttpResponseContext();

		using (capture.BeginCapture(42, context))
		{
			await service.Notify(new DBRef(42, null), "hello from the handler", sender: null);
		}

		await Assert.That(context.Body.ToString()).IsEqualTo("hello from the handler\n");
		await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task Notify_DifferentDbref_IsNotCaptured()
	{
		var (service, _) = BuildService();
		var capture = new HttpOutputCapture();
		var context = new HttpResponseContext();

		using (capture.BeginCapture(42, context))
		{
			// Output to a different object (e.g. @pemit to a player) must not leak into the response.
			await service.Notify(new DBRef(7, null), "private message", sender: null);
		}

		await Assert.That(context.Body.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Notify_AfterCaptureScopeEnds_IsNotCaptured()
	{
		var (service, _) = BuildService();
		var capture = new HttpOutputCapture();
		var context = new HttpResponseContext();

		using (capture.BeginCapture(42, context))
		{
		}

		await service.Notify(new DBRef(42, null), "late output", sender: null);

		await Assert.That(context.Body.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Capture_IsCappedAtMaxBodyLength()
	{
		var capture = new HttpOutputCapture();
		var context = new HttpResponseContext();

		using (capture.BeginCapture(42, context))
		{
			var chunk = new string('x', 3000);
			for (var i = 0; i < 5; i++)
			{
				capture.TryCapture(42, chunk);
			}
		}

		await Assert.That(context.Body.Length).IsEqualTo(HttpOutputCapture.MaxBodyLength);
	}

	[Test]
	public async Task MultipleNotifies_AppendInOrder()
	{
		var (service, _) = BuildService();
		var capture = new HttpOutputCapture();
		var context = new HttpResponseContext();

		using (capture.BeginCapture(42, context))
		{
			await service.Notify(new DBRef(42, null), "first", sender: null);
			await service.Notify(new DBRef(42, null), "second", sender: null);
		}

		await Assert.That(context.Body.ToString()).IsEqualTo("first\nsecond\n");
	}
}
