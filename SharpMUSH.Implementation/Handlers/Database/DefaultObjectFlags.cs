using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Applies the configured default flags to a newly created object — the <c>player_flags</c>,
/// <c>room_flags</c>, <c>thing_flags</c> and <c>exit_flags</c> config options.
/// <para>
/// It lives at the create-command handlers rather than at the commands that build things, because
/// there are a dozen ways to make an object — <c>@create</c>, <c>@dig</c>, <c>@open</c>,
/// <c>@pcreate</c>, <c>@clone</c>, and the <c>create()</c>, <c>dig()</c>, <c>open()</c> and
/// <c>clone()</c> functions — and all of them go through these four commands. Applying it at each
/// call site instead would mean a new way to build an object silently opting out.
/// </para>
/// <para>
/// The options themselves were parsed, validated, exposed on the config page and read back by
/// <c>@config</c>, and then never applied to anything: their only reader decided whether to
/// <i>display</i> a flag as non-default. A game that set <c>thing_flags</c> got no thing flags.
/// </para>
/// </summary>
internal static class DefaultObjectFlags
{
	public static async ValueTask ApplyAsync(
		IFlagAndPowerStore flags,
		IObjectStore objects,
		DBRef created,
		string[]? defaults,
		CancellationToken cancellationToken)
	{
		if (defaults is not { Length: > 0 })
		{
			return;
		}

		var node = await objects.GetObjectNodeAsync(created, cancellationToken);
		if (node.IsNone())
		{
			return;
		}

		var target = node.Known();

		foreach (var name in defaults)
		{
			// A configured name that matches no flag is skipped rather than failing the creation: the
			// object exists by this point, and refusing to hand it back over a typo in mush.cnf would
			// lose it. @config shows what was configured, so the mismatch stays visible.
			var flag = await flags.GetObjectFlagAsync(name, cancellationToken);

			if (flag is not null)
			{
				await flags.SetObjectFlagAsync(target, flag, cancellationToken);
			}
		}
	}
}
