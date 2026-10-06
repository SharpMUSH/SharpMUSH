using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The Messages page: the stored text of each <see cref="GameMessage"/>, and whether the game reads its messages
/// from that text or from an object (the <c>messages_object</c> option).
/// </summary>
[ApiController]
[Route("api/admin/messages")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public class GameMessagesController(
	IGameMessageService messages,
	IPackageRegistryService packages,
	IConfigOptionWriter config,
	IMediator mediator,
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

	/// <summary>Sets <c>messages_object</c>: the object to read messages from, or none for the stored texts.</summary>
	[HttpPut("source")]
	public async Task<ActionResult<GameMessagesResponse>> PutSource([FromBody] GameMessageSourceRequest request)
	{
		if (request.ObjectDbref is { } number
			&& (number < 0 || await mediator.Send(new GetObjectNodeQuery(new DBRef(number))) is not AnySharpObject))
		{
			return BadRequest(new { error = $"#{number} is not an object." });
		}

		const string property = nameof(DatabaseOptions.MessagesObject);
		if (await config.SetAsync(property, (uint?)request.ObjectDbref) is Error<string> refused)
		{
			return BadRequest(new { error = refused.Value });
		}

		await audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, config.NameOf(property)),
			request.ObjectDbref is { } set ? $"#{set}" : "none");
		return Ok(await StateAsync());
	}

	private async Task<GameMessagesResponse> StateAsync()
	{
		var configured = (await config.CurrentAsync()).Database.MessagesObject;
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

		var packageObject = (await packages.GetPackageObjectsAsync(GameMessages.PackageId))
			.FirstOrDefault(o => o.Ref == GameMessages.ObjectRef) is { } record
			&& HelperFunctions.ParseDbRef(record.Objid) is DBRef dbref
				? dbref.Number
				: (int?)null;

		return new GameMessagesResponse(
			configured is null ? GameMessageSource.Stored : GameMessageSource.Object,
			await packages.GetInstalledPackageAsync(GameMessages.PackageId) is InstalledPackageRecord,
			(int?)configured,
			holder?.Object().Name,
			packageObject,
			entries);
	}
}
