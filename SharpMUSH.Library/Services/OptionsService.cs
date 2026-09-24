using Microsoft.Extensions.Options;
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
	IEnumerable<IValidateOptions<SharpMUSHOptions>> validations) : IOptionsFactory<SharpMUSHOptions>
{
	public SharpMUSHOptions Create(string name)
	{
		var data = database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions))
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		if (data is not null)
		{
			return Validated(name, data);
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
