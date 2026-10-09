using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Reality;

/// <summary>
/// The <c>@reality</c> operations. Each answers the message to show, or an <see cref="Error{T}"/> saying why the
/// operation was refused; the actor's grant is checked again right before every write, because the checks
/// between can await.
/// </summary>
public sealed class RealityAdministration(RealityPolicy policy, IAdministrativeCapabilityService capabilities,
	IObjectStore objects, IPermissionService permissions, IValidateService validation)
{
	private const int MaxLayers = 32;

	private readonly SemaphoreSlim writes = new(1, 1);

	public async Task<Result<string>> ExecuteAsync(CapabilityActor actor, string operation, string target, string value,
		CancellationToken ct = default)
	{
		await writes.WaitAsync(ct);
		try
		{
			return await AuthorizeAsync(actor, ct) switch
			{
				AnySharpObject => await RunAsync(actor, operation.ToLowerInvariant(), target, value, ct),
				Error<string> refused => refused
			};
		}
		finally
		{
			writes.Release();
		}
	}

	private async Task<Result<string>> RunAsync(CapabilityActor actor, string operation, string target, string value,
		CancellationToken ct)
	{
		var config = await policy.ConfigurationAsync(ct);
		return operation switch
		{
			"list" or "" => $"Reality is {(config.Enabled ? "enabled" : "disabled")}. Layers: {string.Join(' ', config.Layers)}",
			"enable" or "disable" => await SaveConfigurationAsync(actor, config with { Enabled = operation == "enable" }, ct),
			"add" => AddLayer(config, target) switch
			{
				RealityConfiguration added => await SaveConfigurationAsync(actor, added, ct),
				Error<string> error => error
			},
			"remove" => RemoveLayer(config, target) switch
			{
				RealityConfiguration removed => await SaveConfigurationAsync(actor, removed, ct),
				Error<string> error => error
			},
			"rx" or "tx" or "describe" or "inspect" => await ObjectOperationAsync(actor, config, operation, target, value, ct),
			_ => new Error<string>("Use list, enable, disable, add, remove, rx, tx, describe or inspect.")
		};
	}

	private static Result<RealityConfiguration> AddLayer(RealityConfiguration config, string target)
		=> LayerName(target) switch
		{
			Error<string> error => error,
			string name when config.Layers.Contains(name) => new Error<string>("That layer already exists."),
			string when config.Layers.Length == MaxLayers => new Error<string>($"A world can define at most {MaxLayers} layers."),
			string name => config with { Layers = [.. config.Layers, name] }
		};

	private static Result<RealityConfiguration> RemoveLayer(RealityConfiguration config, string target)
		=> LayerName(target) switch
		{
			Error<string> error => error,
			string name when !config.Layers.Contains(name) => new Error<string>("That layer does not exist."),
			string name => config with { Layers = config.Layers.Where(layer => layer != name).ToArray() }
		};

	private static Result<string> LayerName(string target)
	{
		var name = target.Trim().ToLowerInvariant();
		return RealityPolicy.ValidLayerName(name)
			? name
			: new Error<string>("Layer names use 1–32 ASCII letters, digits, underscores or hyphens.");
	}

	private async Task<Result<string>> SaveConfigurationAsync(CapabilityActor actor, RealityConfiguration config,
		CancellationToken ct)
	{
		if (await AuthorizeAsync(actor, ct) is Error<string> refused)
		{
			return refused;
		}

		await policy.SaveConfigurationAsync(config, ct);
		return "Reality configuration updated.";
	}

	private async Task<Result<string>> ObjectOperationAsync(CapabilityActor actor, RealityConfiguration config,
		string operation, string target, string value, CancellationToken ct)
	{
		if (!DBRef.TryParse(target, out var parsed) || parsed is not { } reference)
		{
			return new Error<string>("Provide an explicit object dbref.");
		}

		return await ControlledTargetAsync(actor, reference, ct) switch
		{
			AnySharpObject obj => await ProfileOperationAsync(actor, config, obj, operation, value, ct),
			Error<string> error => error
		};
	}

	private async Task<Result<string>> ProfileOperationAsync(CapabilityActor actor, RealityConfiguration config,
		AnySharpObject obj, string operation, string value, CancellationToken ct)
	{
		if (await policy.ReadObjectAsync(obj.Object().DBRef, ct) is not { } profile)
		{
			return new Error<string>("The object no longer exists.");
		}

		if (operation == "inspect")
		{
			return await AuthorizeAsync(actor, ct) is Error<string> refused ? refused : Describe(profile);
		}

		var change = operation is "rx" or "tx"
			? WithLayers(config, profile, operation, value)
			: await WithDescriptionAsync(config, profile, obj, value);

		return change switch
		{
			ObjectReality changed => await SaveProfileAsync(actor, obj, changed, ct),
			Error<string> error => error
		};
	}

	private static string Describe(ObjectReality profile)
		=> $"{profile.Object} RX: {string.Join(' ', profile.Receive)}; TX: {string.Join(' ', profile.Transmit)}; "
			+ $"descriptions: {string.Join(' ', profile.Descriptions.OrderBy(pair => pair.Key).Select(pair => pair.Key + "/" + pair.Value))}";

	/// <summary>The profile with the layers it receives (<c>rx</c>) or transmits (<c>tx</c>) replaced.</summary>
	private static Result<ObjectReality> WithLayers(RealityConfiguration config, ObjectReality profile, string operation,
		string value)
	{
		var names = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(name => name.ToLowerInvariant())
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		if (names.Length > MaxLayers || names.Any(name => !config.Layers.Contains(name)))
		{
			return new Error<string>("Use only configured layer names.");
		}

		return operation == "rx" ? profile with { Receive = names } : profile with { Transmit = names };
	}

	/// <summary>
	/// The profile with one layer's description attribute set (<c>layer/ATTRIBUTE</c>) or cleared (<c>layer</c>).
	/// </summary>
	private async ValueTask<Result<ObjectReality>> WithDescriptionAsync(RealityConfiguration config, ObjectReality profile,
		AnySharpObject obj, string value)
	{
		var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
		var layer = parts[0].ToLowerInvariant();
		if (!config.Layers.Contains(layer))
		{
			return new Error<string>("Use a configured layer name.");
		}

		if (parts.Length == 1 || parts[1].Length == 0)
		{
			profile.Descriptions.Remove(layer);
			return profile;
		}

		var attribute = parts[1].ToUpperInvariant();
		if (attribute.Length > 1024
				|| !await validation.Valid(IValidateService.ValidationType.AttributeName, MarkupText.Plain(attribute), obj))
		{
			return new Error<string>("Provide a valid description attribute path.");
		}

		profile.Descriptions[layer] = attribute;
		return profile;
	}

	/// <summary>Writes the profile once the actor still controls the object and still holds the grant.</summary>
	private async Task<Result<string>> SaveProfileAsync(CapabilityActor actor, AnySharpObject obj, ObjectReality profile,
		CancellationToken ct)
		=> await ControlledTargetAsync(actor, obj.Object().DBRef, ct) switch
		{
			AnySharpObject current => await WriteProfileAsync(actor, current, profile, ct),
			Error<string> error => error
		};

	private async Task<Result<string>> WriteProfileAsync(CapabilityActor actor, AnySharpObject obj, ObjectReality profile,
		CancellationToken ct)
	{
		if (await AuthorizeAsync(actor, ct) is Error<string> refused)
		{
			return refused;
		}

		await policy.SaveObjectAsync(obj.Object().Id!, profile, ct);
		return $"Reality settings updated for {profile.Object}.";
	}

	/// <summary>The actor's active player, when it is the executor, holds <c>reality.admin</c> and still exists.</summary>
	private async Task<Result<AnySharpObject>> AuthorizeAsync(CapabilityActor actor, CancellationToken ct)
	{
		if (actor.ActiveCharacter is not { IsObjid: true } active
				|| actor.Executor is not { } executor
				|| !executor.Equals(active)
				|| !await capabilities.AuthorizeAsync(actor, PortalPermission.RealityAdmin, ct))
		{
			return new Error<string>("The active player requires reality.admin.");
		}

		return await objects.GetObjectNodeAsync(active, ct) is AnySharpObject found and SharpPlayer player
				&& player.Object.DBRef.Equals(active)
			? found
			: new Error<string>("The active player no longer exists.");
	}

	private async Task<Result<AnySharpObject>> ControlledTargetAsync(CapabilityActor actor, DBRef reference,
		CancellationToken ct)
		=> await AuthorizeAsync(actor, ct) switch
		{
			AnySharpObject executor => await ControlledByAsync(executor, reference, ct),
			Error<string> refused => refused
		};

	private async Task<Result<AnySharpObject>> ControlledByAsync(AnySharpObject executor, DBRef reference,
		CancellationToken ct)
	{
		if (await objects.GetObjectNodeAsync(reference, ct) is not AnySharpObject found
				|| !found.Object().DBRef.Matches(reference)
				|| found.Object().Id is null)
		{
			return new Error<string>("The object no longer exists.");
		}

		// Controls is a legacy read-only interface. Give builtin reads the caller's
		// cancellation and bound implementations that cannot accept an explicit token.
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, ExecutionBudget.CurrentToken);
		using var budget = ExecutionBudget.FromMilliseconds(0, cancellation.Token);
		using var scope = budget.Enter();
		budget.ThrowIfExceeded();
		return await permissions.Controls(executor, found).AsTask().WaitAsync(budget.Token)
			? found
			: new Error<string>("The active player must control the object.");
	}
}
