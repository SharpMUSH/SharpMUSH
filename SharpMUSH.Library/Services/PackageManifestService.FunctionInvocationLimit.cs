using System.Globalization;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services;

public partial class PackageManifestService
{
	/// <summary>The first format with an object's <c>function_invocation_limit:</c>.</summary>
	public static PackageFormatVersion FunctionInvocationLimitFormat { get; } = new(1, 19);

	/// <summary>The object key that gives a created object its own function invocation limit (<see cref="LiveWire"/>).</summary>
	public const string FunctionInvocationLimitKey = "function_invocation_limit";

	/// <summary>
	/// <paramref name="attributes"/>, with the object's <c>function_invocation_limit:</c> as the engine's
	/// <see cref="LiveWire.AttributeName"/> attribute: the limit with the override on, as <c>@livewire/funceval</c>
	/// and <c>/funcover</c> leave it, so plan, review, upgrade and rollback treat it as the attribute it is.
	/// </summary>
	private static IReadOnlyDictionary<string, PackageAttributeSpec> WithFunctionInvocationLimit(
		IReadOnlyDictionary<string, PackageAttributeSpec> attributes, Dictionary<string, object?> obj, string path,
		PackageFormatVersion format, List<PackageManifestIssue> issues)
	{
		if (!obj.TryGetValue(FunctionInvocationLimitKey, out var node) || node is null)
		{
			return attributes;
		}

		var keyPath = $"{path}.{FunctionInvocationLimitKey}";
		if (format.Major == FunctionInvocationLimitFormat.Major && format.Minor < FunctionInvocationLimitFormat.Minor)
		{
			issues.Add(PackageManifestIssue.Warning(keyPath,
				$"'{FunctionInvocationLimitKey}' needs format {FunctionInvocationLimitFormat} or later and is ignored; set format: \"{FunctionInvocationLimitFormat}\"."));
			return attributes;
		}

		if (!uint.TryParse(Convert.ToString(node, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture,
				out var limit) || limit is 0 or > LiveWire.MaximumLimit)
		{
			issues.Add(PackageManifestIssue.Error(keyPath,
				$"'{FunctionInvocationLimitKey}' must be a whole number from 1 to {LiveWire.MaximumLimit}."));
			return attributes;
		}

		return new Dictionary<string, PackageAttributeSpec>(attributes, StringComparer.OrdinalIgnoreCase)
		{
			[LiveWire.AttributeName] = new(new LiveWire(limit, Override: true).ToString(), [.. LiveWire.AttributeFlags])
		};
	}
}
