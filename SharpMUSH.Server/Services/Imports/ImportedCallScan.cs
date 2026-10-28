using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>
/// Reads every attribute of an imported world for the functions its code calls, and warns about each one SharpMUSH
/// does not have: not a built-in, not a dialect alias in the attribute's dialect, and not registered by an
/// <c>@function</c> somewhere in the world. A call is one <see cref="SoftcodeSyntaxAnalyzer.CalledFunctions"/> finds.
/// </summary>
public static partial class ImportedCallScan
{
	/// <summary>How many missing functions the report names, the most called first.</summary>
	public const int MaxReported = 50;

	/// <summary>How many places a warning names for one function.</summary>
	public const int MaxPlaces = 3;

	[GeneratedRegex(@"@function(?:/\w+)*\s+([0-9A-Za-z_~@`]+)\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex FunctionRegistration();

	public static async Task ReportAsync(IMediator mediator, LibraryService<string, FunctionDefinition> library,
		SoftcodeSyntaxAnalyzer analyzer, string? gameDialect, ConversionResult result, CancellationToken ct)
	{
		var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var missing = new Dictionary<string, (int Calls, List<string> Places)>(StringComparer.OrdinalIgnoreCase);

		await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery(), ct))
		{
			await foreach (var match in mediator.CreateStream(new GetAttributesQuery(obj.DBRef, ".*", false,
				IAttributeService.AttributePatternMode.Regex), ct))
			{
				var attribute = match.Attribute;
				var text = attribute.Value.ToPlainText();
				foreach (Match registration in FunctionRegistration().Matches(text))
					registered.Add(registration.Groups[1].Value);

				var dialect = (SoftcodeDialects.Of(attribute) ?? gameDialect) is { Length: > 0 } key
					? SoftcodeDialects.All.GetValueOrDefault(key)
					: null;
				var place = $"#{obj.DBRef.Number}/{attribute.LongName}";
				foreach (var name in analyzer.CalledFunctions(attribute.Value).Where(name => !Known(library, dialect, name)))
				{
					var entry = missing.TryGetValue(name, out var found) ? found : (0, []);
					if (entry.Places.Count < MaxPlaces && !entry.Places.Contains(place)) entry.Places.Add(place);
					missing[name] = (entry.Calls + 1, entry.Places);
				}
			}
		}

		var reported = missing.Where(pair => !registered.Contains(pair.Key))
			.OrderByDescending(pair => pair.Value.Calls).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();
		foreach (var (name, (calls, places)) in reported.Take(MaxReported))
			result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
				$"Softcode: {name.ToLowerInvariant()}() is called {Times(calls)}, and SharpMUSH has no function by that name. "
				+ $"First in {string.Join(", ", places)}.", NeedsAction: true));
		if (reported.Count > MaxReported)
			result.Warnings.Add(new ImportWarning(ImportWarningKind.Softcode,
				$"Softcode: {reported.Count - MaxReported} more functions SharpMUSH does not have are called: "
				+ string.Join(", ", reported.Skip(MaxReported).Select(pair => pair.Key.ToLowerInvariant() + "()")) + ".",
				NeedsAction: true));
	}

	private static bool Known(LibraryService<string, FunctionDefinition> library, SoftcodeDialect? dialect, string name)
		=> library.ContainsKey(name) || library.ContainsKey(name.ToLowerInvariant())
			|| dialect?.FunctionFor(name) is not null;

	private static string Times(int calls) => calls == 1 ? "once" : $"{calls} times";
}
