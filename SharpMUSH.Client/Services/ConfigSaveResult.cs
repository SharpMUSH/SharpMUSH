using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What a configuration save came to: the configuration the server now runs with, the server's
/// refusal of particular values, or the <see cref="ApiFailure"/> that stopped the request.
/// </summary>
public union ConfigSaveResult(ConfigurationResponse, ConfigSaveRefusal, ApiFailure);

/// <summary>
/// The server refused a save on its values: <c>{ "errors": … }</c>.
/// </summary>
/// <param name="Fields">The refusal of each named property, by path. A <see langword="null"/> reason is a refusal that gave none.</param>
/// <param name="Messages">
/// Refusals of the save as a whole: each <c>_global</c> entry, or <c>errors</c> itself when it is not
/// an object. A <see langword="null"/> one gave no reason.
/// </param>
public sealed record ConfigSaveRefusal(IReadOnlyDictionary<string, string?> Fields, IReadOnlyList<string?> Messages)
{
	private const string Global = "_global";

	/// <summary>The refusal a response body carries, or <see cref="NotFound"/> when it is not one.</summary>
	public static Found<ConfigSaveRefusal> Parse(string? body)
	{
		if (string.IsNullOrWhiteSpace(body)) return new NotFound();

		try
		{
			using var document = JsonDocument.Parse(body);
			if (document.RootElement.ValueKind != JsonValueKind.Object ||
					!document.RootElement.TryGetProperty("errors", out var errors))
				return new NotFound();

			if (errors.ValueKind != JsonValueKind.Object)
				return new ConfigSaveRefusal(new Dictionary<string, string?>(), [Text(errors)]);

			var fields = new Dictionary<string, string?>();
			var messages = new List<string?>();
			foreach (var error in errors.EnumerateObject())
			{
				if (error.Name == Global)
					messages.Add(Text(error.Value));
				else
					fields[error.Name] = Text(error.Value);
			}

			return new ConfigSaveRefusal(fields, messages);
		}
		catch (JsonException)
		{
			return new NotFound();
		}
	}

	private static string? Text(JsonElement element) =>
		element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
