using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Reality;

public sealed class RealityPolicy(IExpandedDataStore store, IObjectStore objects) : IRealityPolicy, IRealityObservationProvider
{
	public const string ConfigurationKey = "sharpmush.reality.config.v1";
	public const string ObjectKey = "sharpmush.reality.object.v1";
	private readonly SemaphoreSlim configurationGate = new(1, 1);
	private RealityConfiguration? configuration;

	public async ValueTask<bool> IsEnabledAsync(CancellationToken ct = default)
		=> (await ConfigurationAsync(ct)).Enabled;

	public async ValueTask<bool> CanPerceiveAsync(DBRef receiver, DBRef target, CancellationToken ct = default)
	{
		var config = await ConfigurationAsync(ct);
		if (!config.Enabled) return true;
		var receiving = await ReadObjectCoreAsync(receiver, false, ct);
		var transmitting = await ReadObjectCoreAsync(target, false, ct);
		return Perceives(config, receiving, transmitting);
	}

	public async ValueTask<Func<DBRef, CancellationToken, ValueTask<bool>>> ObserveAsync(DBRef receiver, CancellationToken ct = default)
	{
		var config = await ConfigurationAsync(ct);
		var receiving = config.Enabled ? await ReadObjectCoreAsync(receiver, false, ct) : null;
		return async (target, token) =>
		{
			token.ThrowIfCancellationRequested();
			if (!config.Enabled) return true;
			if (receiving is null) return false;
			if (target.Equals(receiving.Object)) return true;
			return Perceives(config, receiving, await ReadObjectCoreAsync(target, false, token));
		};
	}

	private static bool Perceives(RealityConfiguration config, ObjectReality? receiving, ObjectReality? transmitting)
		=> receiving is not null && transmitting is not null
			&& (receiving.Object.Equals(transmitting.Object) || SharedLayers(config, receiving, transmitting).Any());

	public async ValueTask<string?> DescriptionAttributeAsync(DBRef receiver, DBRef target, CancellationToken ct = default)
	{
		var config = await ConfigurationAsync(ct);
		if (!config.Enabled) return null;
		var receiving = await ReadObjectCoreAsync(receiver, false, ct);
		var transmitting = await ReadObjectCoreAsync(target, false, ct);
		if (receiving is null || transmitting is null) return null;
		return SharedLayers(config, receiving, transmitting)
			.Select(layer => transmitting.Descriptions.GetValueOrDefault(layer)).FirstOrDefault(attribute => attribute is not null);
	}

	private static IEnumerable<string> SharedLayers(RealityConfiguration config, ObjectReality receiver, ObjectReality target)
		=> config.Layers.Where(layer => receiver.Receive.Contains(layer, StringComparer.OrdinalIgnoreCase)
			&& target.Transmit.Contains(layer, StringComparer.OrdinalIgnoreCase));

	public async ValueTask<RealityConfiguration> ConfigurationAsync(CancellationToken ct = default)
	{
		await configurationGate.WaitAsync(ct);
		try
		{
			configuration ??= Validate(await store.GetExpandedServerData<RealityConfiguration>(ConfigurationKey, ct)
				?? RealityConfiguration.Default);
			return configuration with { Layers = [.. configuration.Layers] };
		}
		finally { configurationGate.Release(); }
	}

	internal async ValueTask SaveConfigurationAsync(RealityConfiguration value, CancellationToken ct)
	{
		value = Validate(value);
		await configurationGate.WaitAsync(ct);
		try
		{
			await store.SetExpandedServerData(ConfigurationKey, value, ct);
			configuration = value with { Layers = [.. value.Layers] };
		}
		finally { configurationGate.Release(); }
	}

	public ValueTask<ObjectReality?> ReadObjectAsync(DBRef reference, CancellationToken ct = default)
		=> ReadObjectCoreAsync(reference, true, ct);

	private async ValueTask<ObjectReality?> ReadObjectCoreAsync(DBRef reference, bool rejectMalformed, CancellationToken ct)
	{
		var found = await objects.GetObjectNodeAsync(reference, ct);
		if (found is null || found.IsNone) return null;
		var obj = found.Known.Object();
		if (!obj.DBRef.Matches(reference) || obj.Id is null) return null;
		var value = await store.GetExpandedObjectData<ObjectReality>(obj.Id, ObjectKey, ct);
		if (value is null || !value.Object.Equals(obj.DBRef)) return ObjectReality.Default(obj.DBRef);
		if (value.Version != 1 || value.Receive is null || value.Transmit is null || value.Descriptions is null)
		{
			if (rejectMalformed) throw new InvalidDataException("Invalid object reality data.");
			// A malformed participant must not reveal itself or interrupt unrelated gameplay.
			return null;
		}
		return value with
		{
			Receive = [.. value.Receive],
			Transmit = [.. value.Transmit],
			Descriptions = new(value.Descriptions, StringComparer.OrdinalIgnoreCase)
		};
	}

	internal async ValueTask SaveObjectAsync(string id, ObjectReality value, CancellationToken ct)
		=> await store.SetExpandedObjectData(id, ObjectKey, value, ct);

	private static RealityConfiguration Validate(RealityConfiguration value)
	{
		if (value.Version != 1 || value.Layers is null || value.Layers.Length > 32
			|| value.Layers.Any(layer => !ValidLayerName(layer))
			|| value.Layers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Layers.Length)
			throw new InvalidDataException("Invalid reality configuration.");
		return value with { Layers = value.Layers.Select(layer => layer.ToLowerInvariant()).ToArray() };
	}

	public static bool ValidLayerName(string? name) => name is { Length: > 0 and <= 32 }
		&& name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
