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
/// Credentials sent to <c>/http/</c> stay out of the arguments and registers every route's softcode
/// reads; the verified caller arrives as <c>%&lt;viewer&gt;</c>. The route itself is covered end to end by
/// <c>SharpMUSH.Tests.Integration.Http.HttpHandlerCredentialTests</c>.
/// </summary>
public class HttpHandlerCredentialTests
{
	/// <summary>Dispatches one request and returns the q-registers and arguments the handler ran with.</summary>
	private static async Task<(Dictionary<string, MString> Registers, Dictionary<string, CallState> Arguments)> DispatchAsync(
		string path, IEnumerable<(string, string)> headers, DBRef? viewer, bool qRegisters = false)
	{
		var mediator = Substitute.For<IMediator>();
		AnySharpObject handler = new TestObjectFactory().CreateThing(8, "HTTP handler");
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(handler));

		var attributes = Substitute.For<IAttributeService>();
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				IAttributeService.AttributeMode.Execute, Arg.Any<bool>())
			.Returns(ValueTask.FromResult<OptionalSharpAttributeOrError>(
				new[] { new SharpAttribute("", "", "GET", [], null, "GET", null!, null!, null!) { Value = MarkupText.Plain("think") } }));

		var parser = Substitute.For<IMUSHCodeParser>();
		parser.State.Returns(ImmutableStack<ParserState>.Empty);
		ParserState? pushed = null;
		parser.Push(Arg.Any<ParserState>()).Returns(call => { pushed = call.Arg<ParserState>(); return parser; });
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(ValueTask.FromResult<CallState?>(CallState.Empty));

		var baseline = TestSharpMushOptions.Create();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Database = baseline.Database with { HttpHandler = 8, EventHandler = null, HttpRequestsPerSecond = 30 },
			Limit = baseline.Limit with { QueueEntryCpuTime = 0 },
			Compatibility = baseline.Compatibility with { HttpQRegisters = qRegisters }
		});

		var service = new HttpHandlerCommandService(mediator, attributes, parser, new HttpOutputCapture(),
			Substitute.For<IEventService>(), InlineTaskScheduler.Create(), options,
			NullLogger<HttpHandlerCommandService>.Instance);

		(await service.DispatchAsync("GET", path, "", headers, "198.51.100.4", viewer)).Expect<HttpHandlerResult>();

		await Assert.That(pushed).IsNotNull();
		pushed!.Registers.TryPeek(out var registers);
		return (registers!, pushed.EnvironmentRegisters);
	}

	[Test]
	[Arguments("Authorization")]
	[Arguments("authorization")]
	[Arguments("Proxy-Authorization")]
	[Arguments("Cookie")]
	public async Task CredentialHeaderIsNeitherAnArgumentNorARegister(string name)
	{
		var (registers, arguments) = await DispatchAsync("/x", [(name, "secret-value"), ("X-Kept", "kept")], null,
			qRegisters: true);

		await Assert.That(arguments.Keys.Any(key => key.StartsWith("HDR.") && key != "HDR.X-KEPT")).IsFalse();
		await Assert.That(arguments["headers"].Message.ToPlainText()).IsEqualTo("X-KEPT");
		await Assert.That(arguments.Values.Any(value => value.Message.ToPlainText().Contains("secret-value"))).IsFalse();
		await Assert.That(registers.Keys.Any(key => key.StartsWith("HDR.") && key != "HDR.X-KEPT")).IsFalse();
		await Assert.That(registers["HEADERS"].ToPlainText()).IsEqualTo("X-KEPT");
		await Assert.That(registers.Values.Any(value => value.ToPlainText().Contains("secret-value"))).IsFalse();
	}

	[Test]
	public async Task HeadersAreNamedArgumentsAndRepeatsJoinWithANewline()
	{
		var (registers, arguments) = await DispatchAsync("/x", [("Accept", "a"), ("X-Kept", "kept"), ("accept", "b")], null);

		await Assert.That(arguments["hdr.accept"].Message.ToPlainText()).IsEqualTo("a\nb");
		await Assert.That(arguments["headers"].Message.ToPlainText()).IsEqualTo("ACCEPT X-KEPT");
		await Assert.That(registers).IsEmpty();
	}

	[Test]
	public async Task HttpQRegistersAlsoSetsTheHeaderRegisters()
	{
		var (registers, _) = await DispatchAsync("/x", [("Accept", "a")], null, qRegisters: true);

		await Assert.That(registers["HDR.ACCEPT"].ToPlainText()).IsEqualTo("a");
		await Assert.That(registers["HEADERS"].ToPlainText()).IsEqualTo("ACCEPT");
	}

	[Test]
	public async Task ViewerArgumentHoldsTheVerifiedObjid()
	{
		var (_, arguments) = await DispatchAsync("/x", [], new DBRef(42, 1700000000000));

		await Assert.That(arguments["viewer"].Message.ToPlainText()).IsEqualTo("#42:1700000000000");
	}

	[Test]
	public async Task AnonymousRequestHasAnEmptyViewer()
	{
		var (_, arguments) = await DispatchAsync("/x", [], null);

		await Assert.That(arguments["viewer"].Message.ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AccessTokenIsRemovedFromThePath()
	{
		var (_, arguments) = await DispatchAsync("/x?a=1&access_token=secret&b=2", [], null);

		await Assert.That(arguments["0"].Message.ToPlainText()).IsEqualTo("/x?a=1&b=2");
		await Assert.That(arguments["path"].Message.ToPlainText()).IsEqualTo("/x?a=1&b=2");
	}

	[Test]
	[Arguments("/x", "/x")]
	[Arguments("/x?a=1", "/x?a=1")]
	[Arguments("/x?access_token=t", "/x")]
	[Arguments("/x?ACCESS_TOKEN=t&a=1", "/x?a=1")]
	[Arguments("/x?access%5Ftoken=t&a=1", "/x?a=1")]
	[Arguments("/x?a=1&access_token=t&access_token=u", "/x?a=1")]
	[Arguments("/x?access_token_hint=keep&q=access_token", "/x?access_token_hint=keep&q=access_token")]
	[Arguments("/x?name=Joe+Smith&access_token", "/x?name=Joe+Smith")]
	public async Task WithoutCredentialParametersKeepsTheRestVerbatim(string path, string expected)
		=> await Assert.That(HttpHandlerCommandService.WithoutCredentialParameters(path)).IsEqualTo(expected);
}
