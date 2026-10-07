using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The engine's configuration is a database document, not a configuration section, so this replaces
/// the framework's <c>IOptionsFactory</c> rather than configuring it.
/// </summary>
/// <remarks>
/// It runs the registered <see cref="IValidateOptions{TOptions}"/> itself, because replacing the
/// framework factory replaces the only thing that ran them: a closed <c>IOptionsFactory&lt;T&gt;</c>
/// registration wins over the open generic <c>AddOptions()</c> adds, so nothing else ever calls a
/// validator — <c>ValidateOnStart()</c> included, which asks the monitor for the value and therefore
/// asks this. Until this ran them, a stored configuration that could not be used started the server
/// anyway and failed later, wherever it was first read.
/// </remarks>
public class OptionsService(
	IExpandedDataStore database,
	IEnumerable<IValidateOptions<SharpMUSHOptions>> validations,
	ILogger<OptionsService>? logger = null) : IOptionsFactory<SharpMUSHOptions>
{
	public SharpMUSHOptions Create(string name)
	{
		var stored = database.GetExpandedServerData<JsonObject>(nameof(SharpMUSHOptions))
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		if (stored is not null)
		{
			return Clamped(name, Completed(stored));
		}

		// Validated BEFORE it is stored. This branch only runs when nothing is stored, so a rejected
		// default written here would be reloaded on every later start, fail the same validation, and
		// leave no path back that does not repair the document by hand.
		var defaultSettings = Validated(name, Default());

		database.SetExpandedServerData(nameof(SharpMUSHOptions), defaultSettings)
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		return defaultSettings;
	}

	/// <summary>
	/// The stored document with every option it does not hold taken from <see cref="Default()"/>. A document
	/// written before an option existed lacks it, and deserialising it as it is leaves a string option null
	/// (and a whole category, if the category is new), which the validators then dereference.
	/// </summary>
	private SharpMUSHOptions Completed(JsonObject stored)
	{
		var filled = new List<string>();
		var defaults = JsonSerializer.SerializeToNode(Default())!.AsObject();
		foreach (var (category, defaultOptions) in defaults)
		{
			if (!stored.TryGetPropertyValue(category, out var storedOptions))
			{
				stored[category] = defaultOptions?.DeepClone();
				filled.Add(category);
			}
			else if (storedOptions is JsonObject storedCategory && defaultOptions is JsonObject defaultCategory)
			{
				Fill(storedCategory, defaultCategory, category, filled);
			}
		}

		var options = stored.Deserialize<SharpMUSHOptions>()!;
		if (filled.Count == 0)
		{
			return options;
		}

		database.SetExpandedServerData(nameof(SharpMUSHOptions), options)
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
		logger?.LogInformation("Stored configuration completed with the defaults of options it did not hold: {Options}",
			string.Join(", ", filled));

		return options;
	}

	/// <summary>
	/// One category's absent options. An option that is there is kept whole: a dictionary option the game
	/// removed an entry from is a value, not a gap the defaults should fill.
	/// </summary>
	private static void Fill(JsonObject stored, JsonObject defaults, string category, List<string> filled)
	{
		foreach (var (option, value) in defaults)
		{
			// Only an absent option: a stored null is a value the game chose for an option that allows one.
			if (!stored.ContainsKey(option))
			{
				stored[option] = value?.DeepClone();
				filled.Add($"{category}.{option}");
			}
		}
	}

	/// <summary>
	/// The stored document held to every option's declared range, and stored again when that moved a value.
	/// A document written before the ranges were enforced can hold a value that is now out of range (a
	/// queue limit of 0 refuses every queue entry), and only the write paths clamp.
	/// </summary>
	private SharpMUSHOptions Clamped(string name, SharpMUSHOptions stored)
	{
		var corrections = new List<ConfigBoundCorrection>();
		var clamped = ConfigBounds.ClampAll(stored, corrections.Add);
		if (corrections.Count == 0)
		{
			return Validated(name, stored);
		}

		var validated = Validated(name, clamped);
		database.SetExpandedServerData(nameof(SharpMUSHOptions), validated)
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
		foreach (var correction in corrections)
		{
			logger?.LogWarning("Stored configuration corrected: {Correction}", correction);
		}

		return validated;
	}

	/// <summary>
	/// Every validator runs and their failures are reported together, the same way
	/// <c>OptionsFactory&lt;T&gt;</c> does it: stopping at the first one means learning about a broken
	/// configuration one edit at a time.
	/// </summary>
	private SharpMUSHOptions Validated(string name, SharpMUSHOptions options)
	{
		List<string>? failures = null;

		foreach (var validation in validations)
		{
			var result = validation.Validate(name, options);
			if (result.Failed)
			{
				(failures ??= []).AddRange(result.Failures ?? []);
			}
		}

		return failures is null
			? options
			: throw new OptionsValidationException(name, typeof(SharpMUSHOptions), failures);
	}

	/// <summary>
	/// The configuration a game starts with when nothing is stored yet.
	/// </summary>
	/// <remarks>
	/// One line, on purpose: <see cref="SharpMUSHOptions.Default()"/> is the only place a shipped default
	/// is written down, and <c>ReadPennMushConfig.Create</c> reads the same defaults for the options a
	/// PennMUSH <c>mush.cnf</c> leaves out. This method stays because it is the name the rest of the
	/// engine already asks by (<c>Configurable.DefaultFloatPrecision</c> among others).
	/// </remarks>
	public static SharpMUSHOptions Default() => SharpMUSHOptions.Default();
}
