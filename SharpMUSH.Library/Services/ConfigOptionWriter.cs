using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class ConfigOptionWriter(
	IExpandedObjectDataService serverData,
	IOptionsWrapper<SharpMUSHOptions> options,
	IEnumerable<IValidateOptions<SharpMUSHOptions>> validators,
	ConfigurationReloadService reload,
	ILogger<ConfigOptionWriter> logger) : IConfigOptionWriter
{
	/// <summary>
	/// PennMUSH's CP_GODONLY options (<c>src/conf.c:153-158</c>): the SQL credentials, which
	/// <c>can_view_config_option</c> hides from everyone but God.
	/// </summary>
	public static readonly IReadOnlySet<string> GodOnlyOptions = new HashSet<string>(StringComparer.Ordinal)
	{
		nameof(NetOptions.SqlUsername), nameof(NetOptions.SqlPassword), nameof(NetOptions.SqlDatabase)
	};

	// Every write reads the stored options, changes part of them and stores them all: two at once must not
	// each drop the other's change. Every writer of the options document goes through this lock.
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	/// <inheritdoc />
	public string? PropertyFor(string optionName) => PropertyOf(optionName);

	/// <inheritdoc cref="IConfigOptionWriter.PropertyFor"/>
	public static string? PropertyOf(string optionName)
		=> ConfigMetadata.PropertyToAttributeName
			.FirstOrDefault(kvp => kvp.Value.Equals(optionName, StringComparison.OrdinalIgnoreCase)).Key;

	/// <inheritdoc />
	public string NameOf(string property) => ConfigMetadata.PropertyMetadata[property].Name;

	/// <summary>Whether <paramref name="property"/> holds a dbref, which a package may set to one of its objects.</summary>
	public static bool IsDbref(string property) => ConfigMetadata.PropertyMetadata[property].Dbref;

	/// <inheritdoc />
	public bool IsSettable(string property) => Settable(property);

	/// <inheritdoc cref="IConfigOptionWriter.IsSettable"/>
	public static bool Settable(string property)
		=> !GodOnlyOptions.Contains(property)
			&& ConfigAccessor.GetCategoryForProperty(property) is not "File"
			&& ConfigAccessor.GetPropertyType(property) is { } type
			&& (Nullable.GetUnderlyingType(type) ?? type) is var scalar
			&& (scalar.IsEnum || scalar == typeof(bool) || scalar == typeof(uint) || scalar == typeof(int)
				|| scalar == typeof(string) || scalar == typeof(char));

	/// <inheritdoc />
	public bool TryParse(string property, string text, out object? value) => TryParseValue(property, text, out value);

	/// <inheritdoc cref="IConfigOptionWriter.TryParse"/>
	public static bool TryParseValue(string property, string text, out object? value)
	{
		value = null;
		if (ConfigAccessor.GetPropertyType(property) is not { } type)
		{
			return false;
		}

		var number = text.StartsWith('#') ? text[1..] : text;

		if (Nullable.GetUnderlyingType(type) is { } underlying)
		{
			if (number is "-1")
			{
				return true;
			}

			type = underlying;
		}

		switch (type)
		{
			case not null when type == typeof(bool):
				value = text.ToLowerInvariant() switch
				{
					"yes" or "true" or "1" => true,
					"no" or "false" or "0" => false,
					_ => null
				};
				return value is not null;
			case not null when type == typeof(uint):
				value = uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var unsigned) ? unsigned : null;
				return value is not null;
			case not null when type == typeof(int):
				value = int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed) ? signed : null;
				return value is not null;
			case not null when type == typeof(char):
				value = text.Length == 1 ? text[0] : null;
				return value is not null;
			case not null when type == typeof(string):
				value = text;
				return true;
			case { IsEnum: true }:
				value = text.Length > 0 && !char.IsDigit(text[0])
					&& Enum.TryParse(type, text, ignoreCase: true, out var member) && Enum.IsDefined(type, member!) ? member : null;
				return value is not null;
			default:
				return false;
		}
	}

	/// <inheritdoc />
	public string? Format(string property, object? value) => FormatValue(property, value);

	/// <inheritdoc cref="IConfigOptionWriter.Format"/>
	public static string? FormatValue(string property, object? value) => value switch
	{
		null => null,
		bool flag => flag ? "yes" : "no",
		uint number when ConfigMetadata.PropertyMetadata[property].Dbref => $"#{number}",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString()
	};

	/// <inheritdoc />
	public async ValueTask<SharpMUSHOptions> CurrentAsync()
		=> await serverData.GetExpandedServerDataAsync<SharpMUSHOptions>() ?? options.CurrentValue;

	/// <inheritdoc />
	public async ValueTask<string?> CurrentTextAsync(string property)
		=> Format(property, ConfigAccessor.GetValue(await CurrentAsync(), property));

	/// <inheritdoc />
	public async ValueTask<Result<SharpMUSHOptions>> PreviewAsync(string property, object? value)
	{
		var corrections = new List<ConfigBoundCorrection>();
		var updated = ConfigAccessor.WithValue(await CurrentAsync(), property, value, corrections.Add);
		return corrections.Count > 0
			? new Error<string>(string.Join(" ", corrections.Select(c => c.ToString())))
			: Validated(updated);
	}

	/// <inheritdoc />
	public async ValueTask<Result<SharpMUSHOptions>> SetAsync(string property, object? value)
	{
		await _writeLock.WaitAsync();
		try
		{
			var corrections = new List<ConfigBoundCorrection>();
			return Validated(ConfigAccessor.WithValue(await CurrentAsync(), property, value, corrections.Add)) switch
			{
				SharpMUSHOptions updated => await StoreAsync(updated, corrections),
				Error<string> refused => refused
			};
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async ValueTask<SharpMUSHOptions> UpdateAsync(Func<SharpMUSHOptions, SharpMUSHOptions> change,
		CancellationToken cancellationToken = default)
	{
		await _writeLock.WaitAsync(cancellationToken);
		try
		{
			var current = await CurrentAsync();
			var updated = change(current);
			if (!ReferenceEquals(updated, current))
			{
				// Checked last: a request cancelled while it waited for the lock stores nothing.
				cancellationToken.ThrowIfCancellationRequested();
				await serverData.SetExpandedServerDataAsync(updated);
				reload.SignalChange();
			}

			return updated;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async ValueTask<Result<SharpMUSHOptions>> StoreAsync(SharpMUSHOptions updated, List<ConfigBoundCorrection> corrections)
	{
		await serverData.SetExpandedServerDataAsync(updated);
		reload.SignalChange();
		// Logged once stored: a correction the validators refused never took effect.
		foreach (var correction in corrections)
		{
			logger.LogWarning("Config option clamped to its declared range: {Correction}", correction.ToString());
		}

		return updated;
	}

	private Result<SharpMUSHOptions> Validated(SharpMUSHOptions updated)
	{
		var failures = validators
			.Select(validator => validator.Validate(Options.DefaultName, updated))
			.Where(result => result.Failed)
			.SelectMany(result => result.Failures ?? [])
			.ToArray();
		return failures.Length > 0 ? new Error<string>(string.Join(" ", failures)) : updated;
	}
}
