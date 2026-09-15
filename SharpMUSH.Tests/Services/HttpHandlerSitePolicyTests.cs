using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Issue #1122: the softcode HTTP dispatcher knows the caller's address, applies PennMUSH's
/// HTTP site policy with it before running any softcode, and reports it on ``HTTP`COMMAND``.
///
/// Penn checks the client IP against <c>@sitelock</c> for the http_handler player, then checks the
/// composite "<c>&lt;IP&gt;`&lt;METHOD&gt;`&lt;PATH&gt;</c>" as though it were a hostname
/// (src/bsd.c:3814-3839, and help sharphttp "HTTP SITELOCK"), and reports the address as the first
/// field of the completion event (bsd.c:4076).
/// </summary>
public class HttpHandlerSitePolicyTests
{
	private sealed class Fixture
	{
		public IMediator Mediator { get; } = Substitute.For<IMediator>();
		public IAttributeService Attributes { get; } = Substitute.For<IAttributeService>();
		public IMUSHCodeParser Parser { get; } = Substitute.For<IMUSHCodeParser>();
		public HttpHandlerCommandService Service { get; }

		/// <summary>Command lists the parser was actually asked to run, in order.</summary>
		public List<string> Executed { get; } = [];

		/// <summary>The parser state the completion event ran under, if it ran at all.</summary>
		public ParserState? EventState { get; private set; }

		public Fixture(Dictionary<string, string[]> sitelockRules)
		{
			var objects = new TestObjectFactory();
			AnySharpObject handler = objects.CreateThing(8, "HTTP handler");
			AnySharpObject events = objects.CreateThing(9, "Events");
			var reads = 0;
			Mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
				.Returns(ValueTask<AnyOptionalSharpObject> (_) =>
					ValueTask.FromResult<AnyOptionalSharpObject>(Interlocked.Increment(ref reads) == 2 ? events : handler));

			Attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
					IAttributeService.AttributeMode.Execute, false)
				.Returns(ValueTask<OptionalSharpAttributeOrError> (call) =>
				{
					var name = call.Arg<string>();
					return ValueTask.FromResult<OptionalSharpAttributeOrError>(
						new[] { new SharpAttribute("", "", name, [], null, name, null!, null!, null!) { Value = MarkupText.Plain(name) } });
				});

			Parser.State.Returns(ImmutableStack<ParserState>.Empty);
			ParserState? current = null;
			Parser.Push(Arg.Any<ParserState>()).Returns(call => { current = call.Arg<ParserState>(); return Parser; });
			Parser.CommandListParse(Arg.Any<MarkupText>()).Returns(ValueTask<CallState?> (call) =>
			{
				var code = call.Arg<MarkupText>().ToPlainText();
				Executed.Add(code);
				if (code == "GET")
				{
					current!.HttpResponse!.Body.Append("handler ran");
				}
				else
				{
					EventState = current;
				}

				return ValueTask.FromResult<CallState?>(CallState.Empty);
			});

			var baseline = TestSharpMushOptions.Create();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(baseline with
			{
				Database = baseline.Database with { HttpHandler = 8, EventHandler = 9, HttpRequestsPerSecond = 30 },
				Limit = baseline.Limit with { QueueEntryCpuTime = 0 },
				SitelockRules = new SitelockRulesOptions(sitelockRules)
			});

			Service = new(Mediator, Attributes, Parser, new HttpOutputCapture(),
				new EventService(Mediator, Attributes, options, NullLogger<EventService>.Instance),
				options, NullLogger<HttpHandlerCommandService>.Instance);
		}
	}

	[Test]
	public async Task AnAllowedCallerReachesTheHandlerAndIsNamedOnTheCompletionEvent()
	{
		var fixture = new Fixture(new Dictionary<string, string[]> { ["203.0.113.9"] = ["!connect"] });

		var result = (await fixture.Service.DispatchAsync("GET", "/open", "", [], "198.51.100.4")).Expect<HttpHandlerResult>();

		await Assert.That(result.Status).IsEqualTo(200);
		await Assert.That(fixture.Executed).Contains("GET");
		// bsd.c:4076 — the address is the first argument of HTTP`COMMAND, not an empty slot.
		await Assert.That(fixture.EventState!.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("198.51.100.4");
	}

	[Test]
	public async Task AnIpSitelockedCallerIsRefusedBeforeAnySoftcodeRuns()
	{
		var fixture = new Fixture(new Dictionary<string, string[]> { ["198.51.100.*"] = ["!connect"] });

		var result = (await fixture.Service.DispatchAsync("GET", "/open", "", [], "198.51.100.4")).Expect<HttpHandlerResult>();

		await Assert.That(result.Status).IsEqualTo(403);
		await Assert.That(fixture.Executed).IsEmpty();
	}

	[Test]
	public async Task APathSitelockRuleMatchesTheIpMethodPathComposite()
	{
		// help sharphttp: path restrictions are written as "<IP>`<METHOD>`<PATH>".
		var fixture = new Fixture(new Dictionary<string, string[]> { ["*`GET`/admin/*"] = ["!connect"] });

		var blocked = (await fixture.Service.DispatchAsync("GET", "/admin/secrets", "", [], "198.51.100.4")).Expect<HttpHandlerResult>();
		await Assert.That(blocked.Status).IsEqualTo(403);
		await Assert.That(fixture.Executed).IsEmpty();

		var allowed = (await fixture.Service.DispatchAsync("GET", "/public", "", [], "198.51.100.4")).Expect<HttpHandlerResult>();
		await Assert.That(allowed.Status).IsEqualTo(200);
		await Assert.That(fixture.Executed).Contains("GET");
	}

	[Test]
	public async Task ARuleAgainstOneCallerDoesNotRefuseAnother()
	{
		var fixture = new Fixture(new Dictionary<string, string[]> { ["198.51.100.4"] = ["!connect"] });

		var refused = (await fixture.Service.DispatchAsync("GET", "/open", "", [], "198.51.100.4")).Expect<HttpHandlerResult>();
		var served = (await fixture.Service.DispatchAsync("GET", "/open", "", [], "198.51.100.5")).Expect<HttpHandlerResult>();

		await Assert.That(refused.Status).IsEqualTo(403);
		await Assert.That(served.Status).IsEqualTo(200);
	}
}
