namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Central mapping from an exception raised while processing an EBICS request to the EBICS
/// return code that should be reported to the client. Pluggable so a later, richer catalogue
/// (issue #36 / M4) can replace it.
/// </summary>
public interface IEbicsErrorMapper
{
    /// <summary>Maps <paramref name="exception"/> to an <see cref="EbicsReturnCode"/>.</summary>
    /// <param name="exception">The exception raised during request processing.</param>
    /// <returns>The return code to report to the client.</returns>
    EbicsReturnCode Map(Exception exception);
}
