using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The signed-in player's own preferences: the groups installed packages declare (<c>preferences:</c>), each kept on
/// the account, so every computer and character sees it, or on the character the session plays. The values are
/// attributes softcode reads with <c>get()</c>; nothing here reaches anyone else's.
/// </summary>
/// <remarks>
/// Routes:
///   GET    /api/preferences               — every group, with the caller's values
///   GET    /api/preferences/{group}       — one group
///   PUT    /api/preferences/{group}       — set some of its values { values: { key: value } }, each checked before any is set
///   DELETE /api/preferences/{group}       — every value back to its default
///   DELETE /api/preferences/{group}/{key} — one value back to its default
/// A character group needs the session to play a character (409 otherwise); a value the declaration refuses is 400.
/// </remarks>
[ApiController]
[Route("api/preferences")]
[Authorize]
public class PreferencesController(
	IPreferenceService preferences,
	IVisibleWorldProjection projection,
	BearerAccountResolver bearer) : ControllerBase
{
	private async Task<IActionResult> AsAccountAsync(Func<string, Task<IActionResult>> action)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount account => await action(account.Id),
			ActionResult refused => refused
		};

	/// <summary>Every group installed packages declare, with the caller's values where there is somewhere to keep them.</summary>
	[HttpGet]
	public Task<IActionResult> List(CancellationToken ct)
		=> AsAccountAsync(async accountId =>
		{
			var character = await User.ResolvePlayerAsync(projection, ct);
			var groups = new List<PreferenceGroupResponse>();
			foreach (var group in await preferences.GetGroupsAsync(ct))
			{
				groups.Add(await DescribeAsync(group, accountId, character, ct));
			}

			return Ok(groups);
		});

	/// <summary>One group, with the caller's values.</summary>
	[HttpGet("{group}")]
	public Task<IActionResult> Get(string group, CancellationToken ct)
		=> WithGroupAsync(group, ct, async (found, holder) =>
			Ok(Describe(found, await preferences.ReadAsync(found, holder, ct))));

	/// <summary>Sets the values the body names, checked as the package declares them; nothing is set if one is refused.</summary>
	[HttpPut("{group}")]
	public Task<IActionResult> Put(string group, [FromBody] PreferenceUpdateRequest request, CancellationToken ct)
		=> WithGroupAsync(group, ct, async (found, holder) =>
		{
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var (key, value) in request.Values ?? new Dictionary<string, JsonElement>())
			{
				if (value.ValueKind == JsonValueKind.Null)
				{
					return BadRequest($"{key} cannot be set to null; DELETE api/preferences/{found.Id}/{key} returns it to its default.");
				}

				// An unknown key is refused by the write, with its name; it is passed on as text to get there.
				values[key] = found.Spec.Find(key) is PackagePreferenceSpec spec ? PreferenceJson.Text(spec, value) : value.GetRawText();
			}

			return Answered(found, await preferences.WriteAsync(found, holder, values, ct));
		});

	/// <summary>Every value in the group back to its default.</summary>
	[HttpDelete("{group}")]
	public Task<IActionResult> Reset(string group, CancellationToken ct)
		=> WithGroupAsync(group, ct, async (found, holder) =>
			Answered(found, await preferences.ResetAsync(found, holder, [], ct)));

	/// <summary>One value back to its default.</summary>
	[HttpDelete("{group}/{key}")]
	public Task<IActionResult> ResetKey(string group, string key, CancellationToken ct)
		=> WithGroupAsync(group, ct, async (found, holder) =>
			Answered(found, await preferences.ResetAsync(found, holder, [key], ct)));

	/// <summary>Runs <paramref name="action"/> on the group and where the caller's values are kept, or answers why not.</summary>
	private Task<IActionResult> WithGroupAsync(string groupId, CancellationToken ct,
		Func<PreferenceGroup, DBRef, Task<IActionResult>> action)
		=> AsAccountAsync(async accountId =>
		{
			if (await preferences.FindGroupAsync(groupId, ct) is not PreferenceGroup group)
			{
				return NotFound($"No installed package declares the preference group '{groupId}'.");
			}

			var character = await User.ResolvePlayerAsync(projection, ct);
			return await preferences.HolderAsync(group, accountId, character, ct) switch
			{
				DBRef holder => await action(group, holder),
				Error<string> refused => Conflict(refused.Value)
			};
		});

	private IActionResult Answered(PreferenceGroup group, Result<IReadOnlyList<PreferenceReading>> result) => result switch
	{
		IReadOnlyList<PreferenceReading> readings => Ok(Describe(group, readings)),
		Error<string> refused => BadRequest(refused.Value)
	};

	private async Task<PreferenceGroupResponse> DescribeAsync(PreferenceGroup group, string accountId, SharpPlayer? character,
		CancellationToken ct)
		=> await preferences.HolderAsync(group, accountId, character, ct) switch
		{
			DBRef holder => Describe(group, await preferences.ReadAsync(group, holder, ct)),
			Error<string> refused => Describe(group, group.Spec.Keys.Select(key => (key, (PreferenceReading?)null)).ToList(), refused.Value)
		};

	private static PreferenceGroupResponse Describe(PreferenceGroup group, IReadOnlyList<PreferenceReading> readings)
		=> Describe(group, readings.Select(reading => (reading.Spec, (PreferenceReading?)reading)).ToList(), null);

	private static PreferenceGroupResponse Describe(PreferenceGroup group,
		IReadOnlyList<(PackagePreferenceSpec Spec, PreferenceReading? Reading)> keys, string? unavailable)
	{
		var spec = group.Spec;
		var items = keys.Select(pair => new PreferenceResponse(
				pair.Spec.Key,
				Preferences.TypeName(pair.Spec.Type),
				pair.Spec.Label.Length > 0 ? pair.Spec.Label : pair.Spec.Key,
				pair.Spec.Description,
				pair.Reading is { } reading ? PreferenceJson.Typed(pair.Spec, reading.Value) : null,
				pair.Spec.Default is { } fallback ? PreferenceJson.Typed(pair.Spec, fallback) : null,
				pair.Reading?.IsSet ?? false,
				pair.Spec.Values,
				pair.Spec.Min,
				pair.Spec.Max,
				pair.Spec.Type is PreferenceType.Text or PreferenceType.Json ? pair.Spec.MaxLength : null))
			.ToList();
		return new PreferenceGroupResponse(
			group.Id,
			group.PackageId,
			Preferences.ScopeName(spec.Scope),
			spec.Label.Length > 0 ? spec.Label : spec.Name,
			spec.Description,
			unavailable is null
				? items.ToDictionary(item => item.Key, item => item.Value!.Value)
				: null,
			items,
			unavailable);
	}
}
