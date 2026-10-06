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
/// Credentials sent to <c>/http/</c> stay out of the registers every route's softcode reads; the
/// verified caller arrives as <c>%q&lt;viewer&gt;</c>. The route itself is covered end to end by
/// <c>SharpMUSH.Tests.Integration.Http.HttpHandlerCredentialTests</c>.
/// </summary>
public class HttpHandlerCredentialTests
{
	/// <summary>Dispatches one request and returns the registers and <c>%0</c> the handler ran with.</summary>
	private static async Task<(Dictionary<string, MString> Registers, string Path)> DispatchAsync(
		string path, IEnumerable<(string, string)> headers, DBRef? viewer)
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
			Limit = baseline.Limit with { QueueEntryCpuTime = 0 }
		});

		var service = new HttpHandlerCommandService(mediator, attributes, parser, new HttpOutputCapture(),
			Substitute.For<IEventService>(), InlineTaskScheduler.Create(), options,
			NullLogger<HttpHandlerCommandService>.Instance);

		(await service.DispatchAsync("GET", path, "", headers, "198.51.100.4", viewer)).Expect<HttpHandlerResult>();

		await Assert.That(pushed).IsNotNull();
		pushed!.Registers.TryPeek(out var registers);
		return (registers!, pushed.EnvironmentRegisters["0"].Message!.ToPlainText());
	}

	[Test]
	[Arguments("Authorization")]
	[Arguments("authorization")]
	[Arguments("Proxy-Authorization")]
	[Arguments("Cookie")]
	public async Task CredentialHeaderIsNotARegister(string name)
	{
		var (registers, _) = await DispatchAsync("/x", [(name, "secret-value"), ("X-Kept", "kept")], null);

		await Assert.That(registers.Keys.Any(key => key.StartsWith("HDR.") && key != "HDR.X-KEPT")).IsFalse();
		await Assert.That(registers["HEADERS"].ToPlainText()).IsEqualTo("X-KEPT");
		await Assert.That(registers.Values.Any(value => value.ToPlainText().Contains("secret-value"))).IsFalse();
	}

	[Test]
	public async Task ViewerRegisterHoldsTheVerifiedObjid()
	{
		var (registers, _) = await DispatchAsync("/x", [], new DBRef(42, 1700000000000));

		await Assert.That(registers["VIEWER"].ToPlainText()).IsEqualTo("#42:1700000000000");
	}

	[Test]
	public async Task AnonymousRequestHasAnEmptyViewer()
	{
		var (registers, _) = await DispatchAsync("/x", [], null);

		await Assert.That(registers["VIEWER"].ToPlainText()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task AccessTokenIsRemovedFromPathRegister()
	{
		var (_, path) = await DispatchAsync("/x?a=1&access_token=secret&b=2", [], null);

		await Assert.That(path).IsEqualTo("/x?a=1&b=2");
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
