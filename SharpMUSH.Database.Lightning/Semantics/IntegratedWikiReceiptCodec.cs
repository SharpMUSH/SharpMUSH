namespace SharpMUSH.Database.Lightning.Semantics;

internal static class IntegratedWikiReceiptCodec
{
	internal static byte[] Encode(IntegratedWikiReceipt receipt) => WikiSemanticCodec.Encode(writer =>
	{
		writer.Byte(1); writer.Byte(6); writer.String(receipt.MutationId.ToString("N"));
		writer.String(receipt.RequestDigest); writer.String(receipt.PageId);
		writer.Int64(receipt.StateVersion); writer.Int64(receipt.CommitSequence);
		writer.Int32(receipt.BodyRevision); writer.Byte((byte)receipt.Kind);
	}, 512);
	internal static IntegratedWikiReceipt Decode(byte[] bytes)
	{
		if (bytes.Length > 512) throw new InvalidDataException("Invalid receipt size.");
		var reader = new WikiSemanticCodec.Reader(bytes, 6);
		if (!Guid.TryParseExact(reader.String(), "N", out var id) || id == Guid.Empty) throw new InvalidDataException("Invalid mutation ID.");
		var digest = reader.String();
		var page = reader.String();
		var state = reader.Int64();
		var sequence = reader.Int64();
		var revision = reader.Int32();
		var kind = (WikiLegacyMutationKind)reader.Byte();
		reader.End();
		if (digest.Length != 64 || digest.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
			|| !page.StartsWith("wiki_page/", StringComparison.Ordinal)
			|| !long.TryParse(page.AsSpan(10), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pageKey)
			|| pageKey < 0 || page != "wiki_page/" + pageKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
			|| state <= 0 || sequence <= 0 || revision <= 0 || !Enum.IsDefined(kind)) throw new InvalidDataException("Invalid receipt fields.");
		return new(id, digest, page, state, sequence, revision, kind);
	}
}
