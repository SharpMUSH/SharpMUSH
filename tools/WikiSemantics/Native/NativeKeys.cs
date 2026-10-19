using SharpMUSH.Database.Lightning.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>B1 compatibility surface for the single provider-owned fixed-ID key format.</summary>
public static class NativeKeys
{
	public static byte[] EncodeInteger(long value) => WikiSemanticKeys.EncodeInteger(value);
	public static long DecodeInteger(ReadOnlySpan<byte> encoded) => WikiSemanticKeys.DecodeInteger(encoded);
	public static byte[] EncodeId(ulong value) => WikiSemanticKeys.EncodeId(value);
	public static ulong DecodeId(ReadOnlySpan<byte> encoded) => WikiSemanticKeys.DecodeId(encoded);
	public static byte[] Subject(ulong subject) => WikiSemanticKeys.Subject(subject);
	public static byte[] Forward(ulong subject, ulong property, NativeScalarTag type, ReadOnlySpan<byte> value)
		=> WikiSemanticKeys.Forward(subject, property, (WikiSemanticScalarTag)type, value);
	public static byte[] Posting(ulong collection, ulong classId, ulong property, NativeScalarTag type, ReadOnlySpan<byte> value, ulong subject)
		=> WikiSemanticKeys.Posting(collection, classId, property, (WikiSemanticScalarTag)type, value, subject);
	public static byte[] RegistryLookup(NativeRegistryKind kind, string identity) => WikiSemanticKeys.RegistryLookup((WikiSemanticRegistryKind)kind, identity);
	public static byte[] RegistryReverse(NativeRegistryKind kind, ulong id) => WikiSemanticKeys.RegistryReverse((WikiSemanticRegistryKind)kind, id);
}
