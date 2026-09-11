using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The result of evaluating a lock attribute: its output, or why it could not be evaluated.
/// </summary>
public partial union LockEvaluation(string, LockEvaluationFailure);
