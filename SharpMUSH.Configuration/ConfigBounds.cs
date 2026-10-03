using System.Globalization;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// A value that sat outside the range its option declares, and the bound it was moved to.
/// </summary>
/// <param name="Property">The option's property name (<c>PlayerQueueLimit</c>).</param>
/// <param name="Option">The option's configuration name (<c>player_queue_limit</c>).</param>
/// <param name="Given">The value as it was given.</param>
/// <param name="Applied">The bound that was stored instead.</param>
/// <param name="BelowMinimum">True when <paramref name="Given"/> was under the minimum, false when over the maximum.</param>
public readonly record struct ConfigBoundCorrection(string Property, string Option, object Given, object Applied, bool BelowMinimum)
{
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture,
			$"{Option} {Given}: {(BelowMinimum ? "below its minimum" : "above its maximum")} of {Applied}, so {Applied} was used instead.");
}

/// <summary>
/// Holds every option to the <see cref="SharpConfigAttribute.Min"/> and <see cref="SharpConfigAttribute.Max"/>
/// it declares, as PennMUSH's <c>cf_int</c> (<c>src/conf.c</c>) clamps a value to its limits and logs it.
/// Without it a value that parses but sits outside the range is stored as given, and for a limit the
/// admission test reads as <c>count &gt;= limit</c>, <c>0</c> turns off the thing it limits (#1335).
/// </summary>
/// <remarks>
/// Applied on both write paths: <see cref="ReadPennMushConfig"/> for a <c>mush.cnf</c>, and
/// <see cref="ConfigAccessor.WithValue"/> for <c>@config/set</c> and the portal's configuration page.
/// The engine's options factory also applies it to the stored document on load, for a document written
/// before the ranges were enforced.
/// Records built directly (tests forcing a limit to 0) are not touched.
/// </remarks>
public static class ConfigBounds
{
	/// <summary>
	/// <paramref name="value"/> held to <paramref name="propertyName"/>'s declared range, of the same type.
	/// A value with no declared range, a non-numeric value and null come back unchanged.
	/// </summary>
	/// <param name="corrected">Told about each value that was moved.</param>
	public static object? Clamp(string propertyName, object? value, Action<ConfigBoundCorrection>? corrected = null)
	{
		if (value is null || !ConfigMetadata.PropertyMetadata.TryGetValue(propertyName, out var metadata)
			|| !TryNumber(value, out var given))
		{
			return value;
		}

		var below = TryNumber(metadata.Min, out var min) && given < min;
		var above = !below && TryNumber(metadata.Max, out var max) && given > max;
		if (!below && !above)
		{
			return value;
		}

		var applied = Convert.ChangeType(below ? metadata.Min : metadata.Max, value.GetType(), CultureInfo.InvariantCulture)!;
		corrected?.Invoke(new ConfigBoundCorrection(propertyName, metadata.Name, value, applied, below));
		return applied;
	}

	/// <summary><paramref name="options"/> with every option held to its declared range.</summary>
	public static SharpMUSHOptions ClampAll(SharpMUSHOptions options, Action<ConfigBoundCorrection>? corrected = null)
	{
		foreach (var property in ConfigMetadata.PropertyNames)
		{
			var value = ConfigAccessor.GetValue(options, property);
			var clamped = Clamp(property, value, corrected);
			if (!ReferenceEquals(clamped, value))
			{
				options = ConfigAccessor.WithValue(options, property, clamped);
			}
		}

		return options;
	}

	private static bool TryNumber(object? value, out decimal number)
	{
		try
		{
			switch (value)
			{
				case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
					number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
					return true;
				case double d when double.IsFinite(d):
					number = (decimal)d;
					return true;
				case float f when float.IsFinite(f):
					number = (decimal)f;
					return true;
			}
		}
		catch (OverflowException)
		{
			// A double past decimal's range has no range to sit outside of here.
		}

		number = 0;
		return false;
	}
}
