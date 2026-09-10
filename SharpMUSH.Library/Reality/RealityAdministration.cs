using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Reality;

public sealed class RealityAdministration(RealityPolicy policy, IAdministrativeCapabilityService capabilities,
	IObjectStore objects, IPermissionService permissions, IValidateService validation)
{
	private readonly SemaphoreSlim writes = new(1, 1);

	public async Task<string> ExecuteAsync(CapabilityActor actor, string operation, string target, string value,
		CancellationToken ct = default)
	{
		await writes.WaitAsync(ct);
		try
		{
			await Authorize(actor, ct);
			operation = operation.ToLowerInvariant();
			var config = await policy.ConfigurationAsync(ct);
			if (operation is "list" or "")
				return $"Reality is {(config.Enabled ? "enabled" : "disabled")}. Layers: {string.Join(' ', config.Layers)}";
			if (operation is "enable" or "disable" or "add" or "remove")
			{
				if (operation is "enable" or "disable") config = config with { Enabled = operation == "enable" };
				else
				{
					var name = target.Trim().ToLowerInvariant();
					if (!RealityPolicy.ValidLayerName(name)) throw new ArgumentException("Layer names use 1–32 ASCII letters, digits, underscores or hyphens.");
					if (operation == "add")
					{
						if (config.Layers.Contains(name)) throw new ArgumentException("That layer already exists.");
						if (config.Layers.Length == 32) throw new ArgumentException("A world can define at most 32 layers.");
						config = config with { Layers = [.. config.Layers, name] };
					}
					else
					{
						if (!config.Layers.Contains(name)) throw new ArgumentException("That layer does not exist.");
						config = config with { Layers = config.Layers.Where(layer => layer != name).ToArray() };
					}
				}
				await Authorize(actor, ct);
				await policy.SaveConfigurationAsync(config, ct);
				return "Reality configuration updated.";
			}
			if (operation is not ("rx" or "tx" or "describe" or "inspect"))
				throw new ArgumentException("Use list, enable, disable, add, remove, rx, tx, describe or inspect.");
			if (!DBRef.TryParse(target, out var parsed) || parsed is not { } reference)
				throw new ArgumentException("Provide an explicit object dbref.");
			var obj = await ControlledTarget(actor, reference, ct);
			var profile = await policy.ReadObjectAsync(obj.Object().DBRef, ct)
				?? throw new ArgumentException("The object no longer exists.");
			if (operation == "inspect")
			{
				await Authorize(actor, ct);
				return $"{profile.Object} RX: {string.Join(' ', profile.Receive)}; TX: {string.Join(' ', profile.Transmit)}; descriptions: {string.Join(' ', profile.Descriptions.OrderBy(pair => pair.Key).Select(pair => pair.Key + "/" + pair.Value))}";
			}
			if (operation is "rx" or "tx")
			{
				var names = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Select(name => name.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
				if (names.Length > 32 || names.Any(name => !config.Layers.Contains(name)))
					throw new ArgumentException("Use only configured layer names.");
				profile = operation == "rx" ? profile with { Receive = names } : profile with { Transmit = names };
			}
			else
			{
				var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
				var layer = parts[0].ToLowerInvariant();
				if (!config.Layers.Contains(layer)) throw new ArgumentException("Use a configured layer name.");
				if (parts.Length == 1 || parts[1].Length == 0) profile.Descriptions.Remove(layer);
				else
				{
					var attribute = parts[1].ToUpperInvariant();
					if (attribute.Length > 1024 || !await validation.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj))
						throw new ArgumentException("Provide a valid description attribute path.");
					profile.Descriptions[layer] = attribute;
				}
			}
			obj = await ControlledTarget(actor, obj.Object().DBRef, ct);
			await Authorize(actor, ct); // Controls and validation can await; do not retain an earlier grant.
			await policy.SaveObjectAsync(obj.Object().Id!, profile, ct);
			return $"Reality settings updated for {profile.Object}.";
		}
		finally { writes.Release(); }
	}

	private async Task<AnySharpObject> Authorize(CapabilityActor actor, CancellationToken ct)
	{
		if (actor.ActiveCharacter is not { IsObjid: true } active || actor.Executor is not { } executor
			|| !executor.Equals(active) || !await capabilities.AuthorizeAsync(actor, PortalPermission.RealityAdmin, ct))
			throw new UnauthorizedAccessException("The active player requires reality.admin.");
		var found = await objects.GetObjectNodeAsync(active, ct);
		if (found is null || !found.IsPlayer || !found.AsPlayer.Object.DBRef.Equals(active))
			throw new UnauthorizedAccessException("The active player no longer exists.");
		return found.Known;
	}

	private async Task<AnySharpObject> ControlledTarget(CapabilityActor actor, DBRef reference, CancellationToken ct)
	{
		var executor = await Authorize(actor, ct);
		var found = await objects.GetObjectNodeAsync(reference, ct);
		if (found is null || found.IsNone || !found.Known.Object().DBRef.Matches(reference) || found.Known.Object().Id is null)
			throw new ArgumentException("The object no longer exists.");
		// Controls is a legacy read-only interface. Give builtin reads the caller's
		// cancellation and bound implementations that cannot accept an explicit token.
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
		using var budget = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		using var scope = budget.Enter();
		budget.ThrowIfExceeded();
		if (!await permissions.Controls(executor, found.Known).AsTask().WaitAsync(budget.Token))
			throw new UnauthorizedAccessException("The active player must control the object.");
		return found.Known;
	}
}
