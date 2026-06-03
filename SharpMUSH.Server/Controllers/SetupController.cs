using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// First-run setup endpoints. Only functional when no accounts exist.
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(IAccountService accountService) : ControllerBase
{
	public record SetupStatusResponse(bool NeedsSetup);
	public record SetupCompleteRequest(string Username, string Password);

	[HttpGet("status")]
	public async Task<IActionResult> GetStatus()
	{
		var hasAccounts = await accountService.HasAnyAccountAsync();
		return Ok(new SetupStatusResponse(!hasAccounts));
	}

	[HttpPost("complete")]
	public async Task<IActionResult> Complete([FromBody] SetupCompleteRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");

		if (await accountService.HasAnyAccountAsync())
			return Conflict("Setup has already been completed.");

		var result = await accountService.CreateAccountAsync(request.Username, null, request.Password);
		if (result.IsT1)
			return Conflict(result.AsT1.Value);

		var account = result.AsT0;
		await accountService.LinkCharacterAsync(account.Id!, new DBRef(1));

		return Ok(new { Message = "Setup complete. You can now log in.", AccountId = account.Id });
	}
}
