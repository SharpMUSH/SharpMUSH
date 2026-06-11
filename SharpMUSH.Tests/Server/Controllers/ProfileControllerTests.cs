using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="ProfileController"/>'s handler-output guard: a misconfigured
/// in-game http_handler that returns empty or non-JSON output must yield a 502 envelope
/// (never an unparseable 200 that crashes the portal), while a configured-but-absent route
/// stays a 404 and valid JSON passes through.
/// </summary>
public class ProfileControllerTests
{
	private static ProfileController MakeController(OneOf<string, NotFound> dispatchResult)
	{
		var dispatcher = Substitute.For<IHttpHandlerDispatcher>();
		dispatcher.DispatchAsync(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<OneOf<string, NotFound>>(dispatchResult));

		return new ProfileController(dispatcher, NullLogger<ProfileController>.Instance)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
	}

	[Test]
	public async Task GetSchema_ValidJson_Returns200Json()
	{
		const string json = """{"sections":[]}""";
		var result = await MakeController(json).GetSchema(CancellationToken.None);

		var content = result as ContentResult;
		await Assert.That(content).IsNotNull();
		await Assert.That(content!.ContentType).Contains("application/json");
		await Assert.That(content.Content).IsEqualTo(json);
	}

	[Test]
	public async Task GetSchema_EmptyBody_Returns502()
	{
		var result = await MakeController(string.Empty).GetSchema(CancellationToken.None);

		var obj = result as ObjectResult;
		await Assert.That(obj).IsNotNull();
		await Assert.That(obj!.StatusCode).IsEqualTo(502);
	}

	[Test]
	public async Task GetSchema_MushcodeError_Returns502()
	{
		// A MUSHcode error like "#-1 ..." is not valid JSON ('#' is not a JSON start token).
		var result = await MakeController("#-1 FUNCTION (JSON) EXPECTS AN OBJECT").GetSchema(CancellationToken.None);

		var obj = result as ObjectResult;
		await Assert.That(obj).IsNotNull();
		await Assert.That(obj!.StatusCode).IsEqualTo(502);
	}

	[Test]
	public async Task GetSchema_HandlerUnavailable_Returns404()
	{
		var result = await MakeController(new NotFound()).GetSchema(CancellationToken.None);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}
}
