using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The Messages page: the stored text of each <see cref="GameMessage"/>, and whether the game reads its messages
/// from that text or from the Messages object.
/// </summary>
[ApiController]
[Route("api/admin/messages")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class GameMessagesController(
	IGameMessageService messages,
	IPackageRegistryService packages,
	IAuditLog audit)
	: ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<GameMessagesResponse>> Get() => Ok(await StateAsync());

	/// <summary>Stores a message's text: ANSI escapes for its colours, as a terminal receives it.</summary>
	[HttpPut("{message}")]
	public async Task<ActionResult<GameMessagesResponse>> PutText(GameMessage message, [FromBody] GameMessageTextRequest request)
	{
		if (!Enum.IsDefined(message)) return NotFound();

		await messages.SetTextAsync(message, request.Text);
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet,
			AuditTargets.Of(AuditTargetKinds.Setting, $"message:{message.ToString().ToLowerInvariant()}"), "edited");
		return Ok(await StateAsync());
	}

	/// <summary>Puts back the text SharpMUSH ships for a message.</summary>
	[HttpDelete("{message}")]
	public async Task<ActionResult<GameMessagesResponse>> ResetText(GameMessage message)
	{
		if (!Enum.IsDefined(message)) return NotFound();

		await messages.SetTextAsync(message, null);
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet,
			AuditTargets.Of(AuditTargetKinds.Setting, $"message:{message.ToString().ToLowerInvariant()}"), "reset");
		return Ok(await StateAsync());
	}

	/// <summary>Chooses where messages are read from; null follows the Messages package again.</summary>
	[HttpPut("source")]
	public async Task<ActionResult<GameMessagesResponse>> PutSource([FromBody] GameMessageSourceRequest request)
	{
		if (request.Source is { } source && !Enum.IsDefined(source)) return BadRequest(new { error = "Unknown source." });

		await messages.SetSourceAsync(request.Source);
		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, "message:source"),
			request.Source?.ToString().ToLowerInvariant() ?? "package");
		return Ok(await StateAsync());
	}

	private async Task<GameMessagesResponse> StateAsync()
	{
		var (source, chosen) = await messages.GetSourceAsync();
		var holder = await messages.MessagesObjectAsync() is AnySharpObject found ? found : null;
		var attributes = holder is null
			? []
			: (await holder.Object().Attributes.Value.Select(a => a.Name).ToListAsync())
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var entries = new List<GameMessageEntry>(GameMessages.All.Count);
		foreach (var message in GameMessages.All)
		{
			var attribute = GameMessages.AttributeName(message);
			// The preview is what a connection at the connect screen would be shown: no viewer, no descriptor.
			var preview = await messages.RenderAsync(message, 0);
			entries.Add(new GameMessageEntry(
				message,
				await messages.GetTextAsync(message),
				await messages.IsDefaultAsync(message),
				attribute,
				attributes.Contains(attribute),
				preview is MString shown ? shown.Render(MarkupFormat.Ansi) : null));
		}

		return new GameMessagesResponse(
			source,
			chosen,
			await packages.GetInstalledPackageAsync(GameMessages.PackageId) is InstalledPackageRecord,
			holder?.Object().DBRef.Number,
			holder?.Object().Name,
			entries);
	}
}
