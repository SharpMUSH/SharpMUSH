using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Onboarding;

/// <summary>
/// A few configuration options edited inside the identity step of Prepare for players, with the same rows as their
/// configuration page (<see cref="ConfigFieldRow"/>), and saved with the identity form (<see cref="SaveAsync"/>).
/// A component names its options by path in <see cref="OptionPaths"/>.
/// </summary>
public abstract class ConfigOptionFields : ComponentBase
{
	[Inject] protected IStringLocalizer<SharedResource> Loc { get; set; } = null!;
	[Inject] protected ConfigSchemaService SchemaService { get; set; } = null!;
	[Inject] protected AdminConfigService AdminConfigService { get; set; } = null!;

	/// <summary>Raised after an edit, so the form can mark itself unsaved.</summary>
	[Parameter]
	public EventCallback OnChanged { get; set; }

	/// <summary>The options edited here, by path, in the order they are listed.</summary>
	protected abstract IReadOnlyList<string> OptionPaths { get; }

	/// <summary>The resource key of the message shown when the options cannot be loaded, with the reason as {0}.</summary>
	protected abstract string LoadFailedKey { get; }

	protected readonly ConfigDraft Draft = new();
	protected List<PropertyMetadata> Properties = [];
	protected bool Loaded;
	protected string? Error;

	protected override async Task OnInitializedAsync()
	{
		switch (await SchemaService.GetSchemaAsync())
		{
			case ConfigurationSchema schema:
				await LoadValuesAsync(schema);
				break;
			case ApiFailure failure:
				Error = Loc[LoadFailedKey, failure.Message].Value;
				break;
		}
	}

	private async Task LoadValuesAsync(ConfigurationSchema schema)
	{
		var byPath = schema.Properties.Values.ToDictionary(property => property.Path, StringComparer.OrdinalIgnoreCase);
		Properties = OptionPaths.Select(path => byPath.GetValueOrDefault(path)).OfType<PropertyMetadata>().ToList();
		switch (await AdminConfigService.FetchConfigurationFromServer())
		{
			case ConfigurationResponse response:
				Draft.Load(Properties, response.Configuration);
				Loaded = true;
				break;
			case ApiFailure failure:
				Error = Loc[LoadFailedKey, failure.Message].Value;
				break;
		}
	}

	protected Task Changed() => OnChanged.InvokeAsync();

	/// <summary>The edited value of the text option at <paramref name="path"/>; empty when it is not loaded.</summary>
	public string Text(string path) =>
		Properties.FirstOrDefault(property => property.Path == path) is { } property ? Draft.String(property) : string.Empty;

	/// <summary>The edited value of the switch at <paramref name="path"/>; on when it is not loaded.</summary>
	public bool Switch(string path) =>
		Properties.FirstOrDefault(property => property.Path == path) is not { } property || Draft.Bool(property);

	protected static string? NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	/// <summary>
	/// Saves the edited options, if any. Answers null once they are saved, else why not; a refused value is also
	/// marked beside its field, as the configuration page marks it.
	/// </summary>
	public async Task<string?> SaveAsync()
	{
		if (!Loaded) return null;
		Draft.ClearErrors();
		var changes = Draft.Changes();
		if (changes.Count == 0) return null;
		return await AdminConfigService.UpdateConfigAsync(changes) switch
		{
			ConfigurationResponse => Saved(changes.Keys),
			ConfigSaveRefusal refusal => Refused(refusal),
			ApiFailure failure => string.Format(Loc["SaveFailed"].Value, failure.Message)
		};
	}

	private string? Saved(IEnumerable<string> paths)
	{
		Draft.MarkSaved(paths);
		return null;
	}

	private string Refused(ConfigSaveRefusal refusal)
	{
		Draft.SetErrors(refusal.Fields, Loc["ResInvalid"].Value);
		var messages = string.Join(' ', refusal.Messages.Select(message => message ?? Loc["ResValidationFailed"].Value));
		return messages.Length > 0 ? messages : Loc["ResValidationFailed"].Value;
	}
}
