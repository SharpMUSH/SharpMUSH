using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

internal sealed class TestableController : ApiControllerBase
{
    public IActionResult GetOk<T>(T value, string? message = null) => Ok(value, message);
    public IActionResult GetCreated<T>(string location, T value) => Created(location, value);
    public IActionResult GetNoContent() => NoContent();
    public IActionResult GetProblem400(string detail) => Problem400(detail);
    public IActionResult GetProblem401(string detail = "Authentication required.") => Problem401(detail);
    public IActionResult GetProblem403(string detail = "You do not have permission to perform this action.") => Problem403(detail);
    public IActionResult GetProblem404(string detail) => Problem404(detail);
    public IActionResult GetProblem409(string detail) => Problem409(detail);
    public IActionResult GetProblem422(string detail) => Problem422(detail);
    public IActionResult GetProblem500(string detail = "An unexpected error occurred.") => Problem500(detail);

    public string? GetAccountId() => CurrentAccountId;
    public string? GetUsername() => CurrentUsername;
    public int? GetCharacterKey() => CurrentCharacterKey;
}

/// <summary>
/// Unit tests for <see cref="ApiControllerBase"/>: envelope shape, identity helpers, and RFC 7807 error helpers.
/// </summary>
public class ApiControllerBaseTests
{
    private static TestableController BuildController(
        string? sub = null,
        string? uniqueName = null,
        string? characterKey = null)
    {
        var controller = new TestableController();

        var claims = new List<Claim>();
        if (sub is not null)         claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
        if (uniqueName is not null)  claims.Add(new Claim(ClaimTypes.Name, uniqueName));
        if (characterKey is not null) claims.Add(new Claim("character_key", characterKey));

        var identity  = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };

        return controller;
    }

    [Test]
    public async Task Ok_ReturnsSuccessEnvelopeWithData()
    {
        var controller = BuildController();
        var result = controller.GetOk(42) as OkObjectResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(200);

        var envelope = result.Value as ApiResponse<int>;
        await Assert.That(envelope).IsNotNull();
        await Assert.That(envelope!.Succeeded).IsTrue();
        await Assert.That(envelope.Data).IsEqualTo(42);
    }

    [Test]
    public async Task Ok_WithMessage_SetsMessageOnEnvelope()
    {
        var controller = BuildController();
        var result = (controller.GetOk("hello", "created") as OkObjectResult)!;
        var envelope = (result.Value as ApiResponse<string>)!;

        await Assert.That(envelope.Message).IsEqualTo("created");
    }

    [Test]
    public async Task Created_ReturnsCreatedResultWithLocationAndEnvelope()
    {
        var controller = BuildController();
        var result = controller.GetCreated("/api/items/1", "item") as CreatedResult;

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(201);
        await Assert.That(result.Location).IsEqualTo("/api/items/1");

        var envelope = result.Value as ApiResponse<string>;
        await Assert.That(envelope!.Succeeded).IsTrue();
        await Assert.That(envelope.Data).IsEqualTo("item");
    }

    [Test]
    public async Task NoContent_Returns204()
    {
        var controller = BuildController();
        var result = controller.GetNoContent() as NoContentResult;
        await Assert.That(result!.StatusCode).IsEqualTo(204);
    }

    [Test]
    public async Task Problem400_Returns400WithProblemDetails()
    {
        var controller = BuildController();
        var result = controller.GetProblem400("bad input") as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(400);

        var problem = result.Value as ProblemDetails;
        await Assert.That(problem!.Status).IsEqualTo(400);
        await Assert.That(problem.Detail).IsEqualTo("bad input");
    }

    [Test]
    public async Task Problem401_Returns401()
    {
        var result = BuildController().GetProblem401() as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(401);
    }

    [Test]
    public async Task Problem403_Returns403()
    {
        var result = BuildController().GetProblem403() as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Problem404_Returns404WithDetail()
    {
        var result = BuildController().GetProblem404("not found") as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(404);
        await Assert.That((result.Value as ProblemDetails)!.Detail).IsEqualTo("not found");
    }

    [Test]
    public async Task Problem409_Returns409()
    {
        var result = BuildController().GetProblem409("conflict") as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(409);
    }

    [Test]
    public async Task Problem422_Returns422()
    {
        var result = BuildController().GetProblem422("semantic error") as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(422);
    }

    [Test]
    public async Task Problem500_Returns500()
    {
        var result = BuildController().GetProblem500() as ObjectResult;
        await Assert.That(result!.StatusCode).IsEqualTo(500);
    }

    [Test]
    public async Task CurrentAccountId_WhenClaimPresent_ReturnsValue()
    {
        var controller = BuildController(sub: "acct-guid-123");
        await Assert.That(controller.GetAccountId()).IsEqualTo("acct-guid-123");
    }

    [Test]
    public async Task CurrentAccountId_WhenClaimAbsent_ReturnsNull()
    {
        var controller = BuildController();
        await Assert.That(controller.GetAccountId()).IsNull();
    }

    [Test]
    public async Task CurrentUsername_WhenClaimPresent_ReturnsValue()
    {
        var controller = BuildController(uniqueName: "Gandalf");
        await Assert.That(controller.GetUsername()).IsEqualTo("Gandalf");
    }

    [Test]
    public async Task CurrentCharacterKey_WhenNumericClaimPresent_ReturnsInt()
    {
        var controller = BuildController(characterKey: "42");
        await Assert.That(controller.GetCharacterKey()).IsEqualTo(42);
    }

    [Test]
    public async Task CurrentCharacterKey_WhenClaimAbsent_ReturnsNull()
    {
        var controller = BuildController();
        await Assert.That(controller.GetCharacterKey()).IsNull();
    }

    [Test]
    public async Task CurrentCharacterKey_WhenClaimNonNumeric_ReturnsNull()
    {
        var controller = BuildController(characterKey: "not-a-number");
        await Assert.That(controller.GetCharacterKey()).IsNull();
    }
}
