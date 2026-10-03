using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// What a retention pass did to one kind of history: purged what its rule allowed, left it alone because
/// the rule keeps everything, or stopped part-way with a reason.
/// </summary>
public union HistoryPurgeOutcome(HistoryPurged, HistoryKeptEverything, HistoryPurgeFailed);

/// <summary>The pass ran to the end of the kind's history.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Rule">The rule it ran under.</param>
/// <param name="Records">Records deleted from the live world.</param>
/// <param name="Bytes">What they occupied, keys and values together.</param>
/// <param name="Batches">How many write transactions it took.</param>
public readonly record struct HistoryPurged(string Kind, HistoryRetentionRule Rule, long Records, long Bytes, int Batches);

/// <summary>The kind's rule sets no bound, so the pass did not scan it.</summary>
public readonly record struct HistoryKeptEverything(string Kind);

/// <summary>
/// The pass stopped part-way through the kind. Everything counted in <paramref name="Records"/> was
/// archived (when an archive is configured) and deleted; nothing after it was touched.
/// </summary>
public readonly record struct HistoryPurgeFailed(string Kind, long Records, long Bytes, string Reason);
