using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services.RecurringJobs;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Controllers;

[ApiController, Route("api/recurring-jobs"), Authorize]
public sealed class RecurringJobsController(IRecurringJobService jobs) : ControllerBase
{
	public sealed record ConfigureRequest(string Schedule, string TimeZone, bool Enabled);
	[HttpGet]
	public Task<IActionResult> List([FromQuery] bool all = false, CancellationToken ct = default)
		=> Run(async () => await jobs.ListAsync(Actor(), all, ct));
	[HttpPost]
	public Task<IActionResult> Create(RecurringJobRequest request, CancellationToken ct)
		=> Run(async () => await jobs.CreateAsync(Actor(), request, ct));
	[HttpPut("{id}")]
	public Task<IActionResult> Configure(string id, ConfigureRequest request, CancellationToken ct)
		=> Run(async () => await jobs.ConfigureAsync(Actor(), id, request.Schedule, request.TimeZone, request.Enabled, ct));
	[HttpDelete("{id}")]
	public Task<IActionResult> Delete(string id, CancellationToken ct)
		=> Run(async () => { await jobs.DeleteAsync(Actor(), id, ct); return true; });
	private CapabilityActor Actor()
	{
		var account = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (account is null || !DBRef.TryParse(User.FindFirstValue(GameHub.CharacterDbrefClaim), out var active) || active is not { IsObjid: true })
			throw new RecurringJobException("denied", "Select a linked active character before managing jobs.");
		return new(account, active, active);
	}
	private async Task<IActionResult> Run(Func<Task<object>> action)
	{
		try { return Ok(await action()); }
		catch (RecurringJobException ex) { return StatusCode(ex.Code switch { "denied" => 403, "missing" => 404, "limit" => 409, _ => 400 }, new { error = ex.Message, code = ex.Code }); }
	}
}
