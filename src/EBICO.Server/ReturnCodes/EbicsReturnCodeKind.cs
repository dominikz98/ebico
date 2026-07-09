namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Where an <see cref="EbicsReturnCode"/> is reported inside an <c>ebicsResponse</c>: a
/// <em>technical</em> code goes into the mutable header (<c>header/mutable/ReturnCode</c>),
/// a <em>business</em> code into the body (<c>body/ReturnCode</c>).
/// </summary>
public enum EbicsReturnCodeKind
{
    /// <summary>Message-/transport-level status, reported in the mutable header.</summary>
    Technical,

    /// <summary>Order-related status, reported in the response body.</summary>
    Business,
}
