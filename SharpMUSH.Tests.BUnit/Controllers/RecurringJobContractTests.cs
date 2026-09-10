using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services.RecurringJobs;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.BUnit.Controllers;

public class RecurringJobContractTests
{
	[Test]
	public async Task CreateUsesAuthenticatedActiveIdentity()
	{
		var service = Substitute.For<IRecurringJobService>();
		var request = new RecurringJobRequest("#10:123", "RUN", "0 9 * * *", "UTC");
		await Controller(service, true).Create(request, default);
		await service.Received(1).CreateAsync(new("account", new DBRef(7, 1), new DBRef(7, 1)), request, default);
	}
	[Test]
	public async Task MissingActiveCharacterFailsBeforeServiceInvocation()
	{
		var service = Substitute.For<IRecurringJobService>();
		var result = await Controller(service, false).Delete("job-id", default);
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(403);
		await service.DidNotReceive().DeleteAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
	private static RecurringJobsController Controller(IRecurringJobService service, bool active)
	{
		var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "account") };
		if (active) claims.Add(new(GameHub.CharacterDbrefClaim, "#7:1"));
		return new(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, "test")) } } };
	}
}
