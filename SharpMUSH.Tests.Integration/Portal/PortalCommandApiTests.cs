using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// <c>POST api/commands</c>, the portal's route for a game command it issues itself (#1485).
///
/// <para>Before it, the portal's only route was the game hub's <c>SendCommand</c>, which published a
/// message nothing in the repository consumed: a command sent that way never reached the engine, and
/// there was nothing to answer with. These drive a command from the HTTP request to the engine's queue
/// and back, and read the answer the request got — not a notification recorded on the side.</para>
///
/// <para>Every test here is the same account session, and an account may have only
/// <c>PortalCommands:MaxPendingPerAccount</c> (4) commands pending, so they run one at a time: run
/// together, the eleven of them were answered 429.</para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(nameof(PortalCommandApiTests))]
public class PortalCommandApiTests(ServerWebAppFactory factory)
{
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// Pinned to https: the server uses UseHttpsRedirection, and following the 307 from http→https
	/// makes HttpClient drop headers.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static async Task<PortalCommandResponse> RunAsync(HttpClient http, string command, string? result = null)
	{
		var response = await http.PostAsJsonAsync("api/commands", new PortalCommandRequest(command, result));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because(await response.Content.ReadAsStringAsync());
		return (await response.Content.ReadFromJsonAsync<PortalCommandResponse>())!;
	}

	/// <summary>A command sent over HTTP reaches the engine, and what it said comes back as the answer.</summary>
	[Test]
	public async Task ACommandReachesTheEngine_AndItsOutputIsTheAnswer()
	{
		var http = CreateClient();
		var marker = TestIsolationHelpers.GenerateUniqueName("PortalThink");

		var answer = await RunAsync(http, $"think {marker} [add(2,3)]");

		await Assert.That(answer.Output).Contains($"{marker} 5");
		await Assert.That(answer.Result).IsNull();
		await Assert.That(answer.Truncated).IsFalse();
	}

	/// <summary>
	/// A softcode <c>$</c>-command — the thing <c>IEngineCommandInvoker</c> cannot run — runs as a typed
	/// line does: in place, inside the request's own queue entry. Its <c>@pemit</c> is in the answer,
	/// and the result expression, evaluated straight after in the same entry, already sees what the
	/// <c>$</c>-command wrote. Queued, neither would be there yet.
	/// </summary>
	[Test]
	public async Task ASoftcodeDollarCommand_RunsInPlace_AndTheResultSeesWhatItDid()
	{
		var http = CreateClient();
		var gadget = TestIsolationHelpers.GenerateUniqueName("PortalGadget");
		var verb = $"+ping{gadget.ToLowerInvariant()}";

		await RunAsync(http, $"@create {gadget}");
		// A new thing is created NO_COMMAND, as in PennMUSH's default configuration.
		await RunAsync(http, $"@set {gadget}=!NO_COMMAND");
		await RunAsync(http, $"&CMD`PING {gadget}=${verb} *:@pemit %#=pong {gadget} %0; @set me=PINGED:%0");

		var answer = await RunAsync(http, $"{verb} hello", result: $"get({gadget}/PINGED)");

		await Assert.That(answer.Output).Contains($"pong {gadget} hello");
		await Assert.That(answer.Result).IsEqualTo("hello");
	}

	/// <summary>
	/// The command acts as the account session's character, which here has no connection at all — the
	/// route needs no terminal, and does not borrow one.
	/// </summary>
	[Test]
	public async Task TheCommandRunsAsTheSessionsCharacter_WhoNeedsNoConnection()
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "PortalCmdChar");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var controller = await CommandsControllerAs(character);
		var marker = TestIsolationHelpers.GenerateUniqueName("PortalSelf");

		var result = await controller.Run(new PortalCommandRequest($"think {marker} [num(me)]", "name(me)"), CancellationToken.None);

		var answer = result.Value!;
		await Assert.That(answer.Output).Contains($"{marker} #{character.Number}");
		await Assert.That(answer.Result).StartsWith("PortalCmdChar");
	}

	/// <summary>
	/// The Scenes page's start form, end to end: <c>+scene/create</c> — a softcode <c>$</c>-command in the
	/// scene package — run as the acting character, who has no connection, answers with the scene it made.
	/// <c>scenefocus(me)</c> evaluated in the same entry names it, where the page used to diff the roster
	/// and check the owner to guess which new scene was its own.
	/// </summary>
	[Test]
	public async Task SceneCreate_AnswersWithTheSceneItMade()
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "PortalStarter");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		await RunAsync(CreateClient(), $"@set #{character.Number}=APPROVED");
		var controller = await CommandsControllerAs(character);
		var title = TestIsolationHelpers.GenerateUniqueName("PortalScene");

		var before = (await controller.Run(new PortalCommandRequest("think", "scenefocus(me)"), CancellationToken.None)).Value!;
		var created = (await controller.Run(new PortalCommandRequest($"+scene/create {title}", "scenefocus(me)"), CancellationToken.None)).Value!;

		await Assert.That(created.Result).IsNotNull().And.DoesNotStartWith("#-1");
		await Assert.That(created.Result).IsNotEqualTo(before.Result);
		await Assert.That(created.Output).Contains(line => line.Contains($"Scene {created.Result} created", StringComparison.Ordinal));
		var owner = (await RunAsync(CreateClient(), "think", $"scene({created.Result},owner)")).Result;
		await Assert.That(owner?.Split(':')[0]).IsEqualTo($"#{character.Number}");
	}

	/// <summary>
	/// A request that names the character it means to act as is refused, unrun, once the session acts as
	/// someone else — so a flow of several commands cannot finish as the character the tab switched to.
	/// </summary>
	[Test]
	public async Task ARequestPinnedToAnotherCharacter_IsRefused_AndNothingRuns()
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "PortalPinned");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>().Object.DBRef;
		var controller = await CommandsControllerAs(character);
		var marker = TestIsolationHelpers.GenerateUniqueName("PortalPin");

		var refused = await controller.Run(new PortalCommandRequest($"&PINMARK me={marker}", Character: god.ToString()), CancellationToken.None);
		var ran = await controller.Run(new PortalCommandRequest("think", $"get(me/PINMARK)", character.ToString()), CancellationToken.None);

		await Assert.That((refused.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
		await Assert.That(ran.Value!.Result).IsEqualTo(string.Empty);
	}

	/// <summary>
	/// The result expression is the character's code as much as the command is, and runs under the same
	/// output ceiling: a guest's is <c>guest_output_limit</c>, not the 5 MB everyone else gets.
	/// </summary>
	[Test]
	public async Task AGuestsResultExpression_KeepsTheGuestOutputLimit()
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "PortalGuest");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var guestLimit = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.GuestOutputLimit;
		await RunAsync(CreateClient(), $"@power #{character.Number}=Guest");
		try
		{
			var controller = await CommandsControllerAs(character);

			var answer = (await controller.Run(new PortalCommandRequest("think", $"strlen(repeat(x,{guestLimit + 1}))"),
				CancellationToken.None)).Value!;

			await Assert.That(answer.Result).StartsWith(ErrorMessages.Returns.OutputTooLarge);
		}
		finally
		{
			await RunAsync(CreateClient(), $"@power #{character.Number}=!Guest");
		}
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("think one\nthink two")]
	public async Task AnEmptyOrMultiLineCommandIsRefused(string command)
	{
		var http = CreateClient();

		var response = await http.PostAsJsonAsync("api/commands", new PortalCommandRequest(command));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	private static async Task<HttpResponseMessage> EvaluateAsync(HttpClient http, PortalEvalRequest request) =>
		await http.PostAsJsonAsync("api/commands/eval", request);

	private static async Task<PortalCommandResponse> EvaluatedAsync(HttpClient http, PortalEvalRequest request)
	{
		var response = await EvaluateAsync(http, request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
			.Because(await response.Content.ReadAsStringAsync());
		return (await response.Content.ReadFromJsonAsync<PortalCommandResponse>())!;
	}

	/// <summary>
	/// The Softcode Editor's console (#1578): an expression runs as the character, with its arguments as
	/// <c>%0</c> onward, each evaluated first as <c>u()</c> evaluates its arguments, and its value is the answer.
	/// </summary>
	[Test]
	public async Task AnExpression_RunsAsTheCharacter_WithItsArguments()
	{
		var http = CreateClient();
		var me = (await RunAsync(http, "think", "num(me)")).Result;

		var answer = await EvaluatedAsync(http,
			new PortalEvalRequest("[add(%0,%1)] [num(me)] [num(%#)]", ["2", "[add(1,2)]"]));

		await Assert.That(answer.Result).IsEqualTo($"5 {me} {me}");
	}

	/// <summary>
	/// Given an object, the expression — the editor's unsaved buffer — runs as <c>u()</c> would run it from
	/// one of that object's attributes: <c>%!</c> and <c>me</c> are the object, <c>%#</c> the character, and
	/// what the code tells the character comes back with the value.
	/// </summary>
	[Test]
	public async Task AnExpressionRunsAsAnObjectTheCharacterControls()
	{
		var http = CreateClient();
		var gadget = TestIsolationHelpers.GenerateUniqueName("EvalGadget");
		await RunAsync(http, $"@create {gadget}");
		var number = int.Parse((await RunAsync(http, "think", $"num({gadget})")).Result!.TrimStart('#'));
		var marker = TestIsolationHelpers.GenerateUniqueName("EvalTold");
		var me = (await RunAsync(http, "think", "num(me)")).Result;

		var answer = await EvaluatedAsync(http, new PortalEvalRequest(
			$"[pemit(%#,{marker} %0)][num(%!)] [name(me)] [num(%#)]", ["hi"], number));

		await Assert.That(answer.Result).IsEqualTo($"#{number} {gadget} {me}");
		await Assert.That(answer.Output).Contains($"{marker} hi");
	}

	/// <summary>Running code as an object takes what writing code onto it takes: control of it.</summary>
	[Test]
	public async Task AnObjectTheCharacterDoesNotControl_IsRefused()
	{
		var number = await TestIsolationHelpers.CreateTestPlayerAsync(factory.Services, Mediator, "EvalMortal");
		var character = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<SharpPlayer>().Object.DBRef;
		var controller = await CommandsControllerAs(character);

		var refused = await controller.Evaluate(new PortalEvalRequest("num(%!)", Object: 1), CancellationToken.None);
		var own = await controller.Evaluate(new PortalEvalRequest("num(%!)", Object: character.Number), CancellationToken.None);

		await Assert.That((refused.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(own.Value!.Result).IsEqualTo($"#{character.Number}");
	}

	[Test]
	public async Task AnEmptyExpression_OrMoreThanTenArguments_IsRefused()
	{
		var http = CreateClient();

		var empty = await EvaluateAsync(http, new PortalEvalRequest(" "));
		var eleven = await EvaluateAsync(http, new PortalEvalRequest("%0", Enumerable.Range(0, 11).Select(i => $"{i}").ToList()));

		await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(eleven.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	private async Task<CommandsController> CommandsControllerAs(DBRef character)
	{
		var identity = await PortalControllers.IdentityFor(factory, character);
		return new CommandsController(
			factory.Services.GetRequiredService<IPortalCommandService>(),
			factory.Services.GetRequiredService<IVisibleWorldProjection>(),
			Mediator,
			factory.Services.GetRequiredService<IPermissionService>(),
			factory.Services.GetRequiredService<IRealityPolicy>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
			}
		};
	}
}
