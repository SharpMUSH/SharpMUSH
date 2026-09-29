using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Applies the configured <c>function_restrictions</c> to the live function table. PennMUSH runs the
/// <c>restrict_function</c> lines of <c>mush.cnf</c> through <c>restrict_function()</c>
/// (<c>src/function.c</c>), the <c>restrict_function</c> branch of <c>config_set</c>
/// (<c>src/conf.c</c>): the words are added to the function's own restriction bits, which
/// <c>check_func</c> tests on every call. Here they go into the built-in restriction overlay that
/// <c>@function/restrict</c> writes and the parser consults.
/// </summary>
/// <remarks>
/// The set replaces the previous one, as <c>command_restrictions</c> does (#1250): every function
/// the previous set restricted loses that restriction before the new set is applied, which is what
/// lets a restriction be loosened. A live <c>@function/restrict</c> on a function either set names
/// is discarded; functions neither set names are left alone.
/// </remarks>
public sealed class ConfiguredFunctionRestrictions(
	ILibraryProvider<FunctionDefinition> functions,
	IUserDefinedFunctionService registry,
	ILogger<ConfiguredFunctionRestrictions> logger)
{
	private readonly Lock _gate = new();
	private string[] _applied = [];

	/// <param name="restrictions">Function name to its restriction words, as <c>function_restrictions</c> holds them.</param>
	public void Apply(IReadOnlyDictionary<string, string[]> restrictions)
	{
		lock (_gate)
		{
			foreach (var name in _applied)
			{
				registry.SetBuiltinRestriction(name, null);
			}

			var library = functions.Get();
			var layer = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var (name, words) in restrictions)
			{
				var terms = string.Join(' ', words).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
				if (!library.TryGetValue(name, out var found) || terms.Length == 0)
				{
					// restrict_function returns 0 for a name it cannot find, and config_set logs it.
					logger.LogWarning("CONFIG: Invalid function or restriction for {Function}.", name);
					continue;
				}

				// A leading ! clears one of the function's own bits in PennMUSH (apply_restrictions).
				// A built-in's own restrictions are not in the overlay, so there is nothing here to clear.
				foreach (var cleared in terms.Where(term => term.StartsWith('!')))
				{
					logger.LogWarning("CONFIG: restrict_function {Function} {Restriction}: a built-in's own restrictions cannot be cleared; ignored.", name, cleared);
				}

				var added = terms.Where(term => !term.StartsWith('!')).ToArray();
				if (added.Length == 0)
				{
					continue;
				}

				// An alias is the same FUN in PennMUSH, so restricting either restricts both.
				foreach (var key in NamesOf(library, found.LibraryInformation.Attribute, name))
				{
					if (!layer.TryGetValue(key, out var list))
					{
						layer[key] = list = [];
					}

					list.AddRange(added);
				}
			}

			foreach (var (name, words) in layer)
			{
				registry.SetBuiltinRestriction(name, string.Join(' ', words));
			}

			_applied = [.. layer.Keys];
		}
	}

	/// <summary>
	/// The name the function was looked up by, its own name and its configured aliases — each only
	/// while the library still maps it to the same function. A <c>@function/clone</c> shares the
	/// definition too, but is a function of its own, as its copy is in PennMUSH.
	/// </summary>
	private static IEnumerable<string> NamesOf(LibraryService<string, FunctionDefinition> library, SharpFunctionAttribute attribute, string name)
	{
		var aliases = Configurable.FunctionAliases
			.Where(entry => entry.Key.Equals(attribute.Name, StringComparison.OrdinalIgnoreCase))
			.SelectMany(entry => entry.Value);

		return new[] { name, attribute.Name }.Concat(aliases)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(key => library.TryGetValue(key, out var entry) && ReferenceEquals(entry.LibraryInformation.Attribute, attribute));
	}
}
