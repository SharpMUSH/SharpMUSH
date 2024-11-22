using MediatR;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Requests;

public record AttributeEvaluationRequest(ParserState State, DBAttribute Attribute, DBRef Evaluee) : INotification;
