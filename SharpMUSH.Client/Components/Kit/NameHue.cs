namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// A hue (0–359) derived from a name with a stable hash, so a tinted no-image fallback keeps its
/// colour across reloads. <c>string.GetHashCode()</c> is randomised per process, which is why the
/// older <c>GetHashCode() % 360</c> avatars change colour on every page load.
/// </summary>
public static class NameHue
{
	public static int Of(string? name)
	{
		unchecked
		{
			var hash = 2166136261u;
			foreach (var c in name ?? string.Empty)
			{
				hash ^= c;
				hash *= 16777619u;
			}

			return (int)(hash % 360u);
		}
	}
}
