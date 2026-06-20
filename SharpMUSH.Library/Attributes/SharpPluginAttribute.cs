namespace SharpMUSH.Library.Attributes;

/// <summary>
/// Marks the single entry type in a plugin assembly that implements
/// <see cref="Plugins.IPlugin"/>. The loader discovers this type without a blind assembly scan.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SharpPluginAttribute : Attribute;
