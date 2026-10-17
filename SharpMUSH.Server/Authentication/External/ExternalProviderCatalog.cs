using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services;
namespace SharpMUSH.Server.Authentication.External;
/// <summary>Server-only effective settings. Never return this record from an API.</summary>
public sealed record EffectiveExternalProvider(ExternalProviderDefinition Definition, ExternalProviderSettings Settings,
 string Generation, string? ClientSecret);
/// <summary>Startup protocol settings are fixed until restart; readiness and disable are checked on every use.</summary>
public sealed class ExternalProviderCatalog(IMediator mediator, ISecretStore secrets, IHostEnvironment environment, AccountCredentialGate credentialGate)
{
	private readonly SemaphoreSlim gate = new(1, 1);
	private readonly Dictionary<string, string> startupFingerprints = new(StringComparer.Ordinal);
	private bool initialized;
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		await gate.WaitAsync(cancellationToken);
		try
		{
			if (initialized) return;
			foreach (var definition in ExternalProviderDefinition.All)
			{
				var settings = await SettingsAsync(definition.Id, cancellationToken);
				var secret = await SecretAsync(definition, cancellationToken);
				startupFingerprints[definition.Id] = Fingerprint(settings, secret);
			}
			initialized = true;
		}
		finally { gate.Release(); }
	}
	private async Task<ExternalProviderSettings> SettingsAsync(string provider, CancellationToken ct)
	{
		var settings = await mediator.Send(new GetExternalProviderSettingsQuery(provider), ct);
		return settings with { RegistrationId = string.IsNullOrEmpty(settings.RegistrationId) ? "external-" + provider : settings.RegistrationId };
	}
	private async Task<string?> SecretAsync(ExternalProviderDefinition definition, CancellationToken ct)
	{
		if (!definition.RequiresClientSecret) return null;
		return await secrets.GetAsync(definition.SecretName, ct) switch { string value when !string.IsNullOrWhiteSpace(value) => value, _ => null };
	}
	private static string Fingerprint(ExternalProviderSettings settings, string? secret)
	 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings with { Enabled = false, Generation = "" }) + "\n" + secret)));
	internal async Task<EffectiveExternalProvider> ProtocolSettingsAsync(string provider, CancellationToken ct = default)
	{
		await InitializeAsync(ct);
		var definition = ExternalProviderDefinition.Get(provider);
		var settings = await SettingsAsync(provider, ct);
		return new(definition, settings, settings.Generation, await SecretAsync(definition, ct));
	}
	public async Task<FoundResult<EffectiveExternalProvider>> EffectiveAsync(string provider, CancellationToken cancellationToken = default)
	{
		if (!ExternalProviderDefinition.IsKnown(provider)) return new NotFound();
		await InitializeAsync(cancellationToken);
		var definition = ExternalProviderDefinition.Get(provider);
		var settings = await SettingsAsync(provider, cancellationToken);
		if (!settings.Enabled) return new Error<string>("Sign-in provider is disabled.");
		if (ExternalProviderValidation.Validate(provider, settings, environment.IsDevelopment()) is Error<string> invalid) return invalid;
		var secret = await SecretAsync(definition, cancellationToken);
		if (definition.RequiresClientSecret && secret is null) return new Error<string>("Sign-in provider secret is unavailable.");
		var fingerprint = Fingerprint(settings, secret);
		if (startupFingerprints[provider] != fingerprint) return new Error<string>("Sign-in provider configuration requires a server restart.");
		return new EffectiveExternalProvider(definition, settings, settings.Generation + ":" + fingerprint, secret);
	}
	public async Task<IReadOnlyList<ExternalProviderStatus>> StatusesAsync(CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken);
		var result = new List<ExternalProviderStatus>();
		foreach (var definition in ExternalProviderDefinition.All) result.Add(await StatusAsync(definition, cancellationToken));
		return result;
	}
	private async Task<ExternalProviderStatus> StatusAsync(ExternalProviderDefinition definition, CancellationToken ct)
	{
		var settings = await SettingsAsync(definition.Id, ct);
		var secret = await SecretAsync(definition, ct);
		var restart = startupFingerprints[definition.Id] != Fingerprint(settings, secret);
		var effective = await EffectiveAsync(definition.Id, ct);
		var message = effective switch { EffectiveExternalProvider => "Ready", Error<string> error => error.Value, _ => "Unavailable" };
		return new(definition.Id, definition.DisplayName, settings, effective is EffectiveExternalProvider, restart, message,
		 settings.PublicOrigin + definition.CallbackPath, definition.RequiresClientSecret ? definition.SecretName : null,
		 await mediator.Send(new GetExternalProviderAccountCountQuery(settings.RegistrationId), ct));
	}
	public Task<Result<ExternalProviderStatus>> SaveAsync(string provider, ExternalProviderSettings settings, bool acknowledgeAffectedAccounts, CancellationToken cancellationToken = default)
	 => credentialGate.RunAsync(() => SaveCoreAsync(provider, settings, acknowledgeAffectedAccounts, cancellationToken), cancellationToken);
	private async Task<Result<ExternalProviderStatus>> SaveCoreAsync(string provider, ExternalProviderSettings settings, bool acknowledgeAffectedAccounts, CancellationToken cancellationToken)
	{
		if (!ExternalProviderDefinition.IsKnown(provider)) return new Error<string>("Unknown sign-in provider.");
		if (ExternalProviderValidation.Validate(provider, settings, environment.IsDevelopment()) is not ExternalProviderSettings valid)
			return new Error<string>("Invalid sign-in provider settings.");
		await InitializeAsync(cancellationToken);
		await gate.WaitAsync(cancellationToken);
		try
		{
			var previous = await SettingsAsync(provider, cancellationToken);
			if (!string.IsNullOrEmpty(valid.RegistrationId) && valid.RegistrationId != previous.RegistrationId)
				return new Error<string>("Registration identity is immutable; create a separately reviewed migration to replace it.");
			var changesTrust = previous.ClientId != valid.ClientId || previous.PublicOrigin != valid.PublicOrigin
			 || previous.MicrosoftAudience != valid.MicrosoftAudience || !previous.AllowedTenants.SequenceEqual(valid.AllowedTenants);
			var affected = await mediator.Send(new GetExternalProviderAccountCountQuery(previous.RegistrationId), cancellationToken);
			if (previous.ClientId != valid.ClientId && affected > 0)
				return new Error<string>("Changing a client registration with linked identities requires an explicit identity migration.");
			if (((previous.Enabled && !valid.Enabled) || changesTrust) && !acknowledgeAffectedAccounts && affected > 0)
				return new Error<string>("Acknowledge the accounts affected by disabling or changing this sign-in registration.");
			await mediator.Send(new SaveExternalProviderSettingsCommand(provider, valid with { RegistrationId = previous.RegistrationId, Generation = Guid.NewGuid().ToString("N") }), cancellationToken);
		}
		finally { gate.Release(); }
		return await StatusAsync(ExternalProviderDefinition.Get(provider), cancellationToken);
	}
	public async Task<IReadOnlyCollection<string>> EnabledRegistrationsAsync(CancellationToken cancellationToken = default)
	{
		var registrations = new List<string>();
		foreach (var definition in ExternalProviderDefinition.All)
			if (await EffectiveAsync(definition.Id, cancellationToken) is EffectiveExternalProvider effective) registrations.Add(effective.Settings.RegistrationId);
		return registrations;
	}
}
