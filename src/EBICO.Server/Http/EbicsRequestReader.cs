using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EBICO.Server.Http;

/// <summary>
/// The result of reading an inbound EBICS request body: either the raw XML, or a transport-level
/// HTTP status code describing why the body was rejected before it ever reached the pipeline.
/// </summary>
/// <param name="Ok">Whether a body was read successfully.</param>
/// <param name="Xml">The raw request XML when <see cref="Ok"/> is <see langword="true"/>.</param>
/// <param name="TransportStatusCode">The HTTP status to return when <see cref="Ok"/> is <see langword="false"/>.</param>
internal readonly record struct RequestReadResult(bool Ok, string? Xml, int TransportStatusCode)
{
    /// <summary>Creates a successful read result carrying <paramref name="xml"/>.</summary>
    public static RequestReadResult Success(string xml) => new(true, xml, StatusCodes.Status200OK);

    /// <summary>Creates a rejected read result carrying a transport <paramref name="statusCode"/>.</summary>
    public static RequestReadResult Transport(int statusCode) => new(false, null, statusCode);
}

/// <summary>
/// Reads an inbound EBICS request body transport-safely: it enforces the accepted content types
/// and the maximum body size, and decodes with the declared charset (UTF-8 by default). It does
/// <em>not</em> parse XML — that (XXE-hardened) happens in <c>EBICO.Core</c>.
/// </summary>
internal static class EbicsRequestReader
{
    private const int BufferSize = 8192;

    /// <summary>Reads and validates the body of <paramref name="request"/>.</summary>
    /// <param name="request">The inbound HTTP request.</param>
    /// <param name="options">The server options (allowed content types, size limit).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The read result.</returns>
    internal static async Task<RequestReadResult> ReadAsync(
        HttpRequest request,
        EbicoServerOptions options,
        CancellationToken ct)
    {
        if (!TryResolveEncoding(request.ContentType, options.AllowedContentTypes, out var encoding))
        {
            return RequestReadResult.Transport(StatusCodes.Status415UnsupportedMediaType);
        }

        // Reject early when the declared length already exceeds the limit.
        if (request.ContentLength is long declared && declared > options.MaxRequestBodyBytes)
        {
            return RequestReadResult.Transport(StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > options.MaxRequestBodyBytes)
            {
                return RequestReadResult.Transport(StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        // GetBuffer avoids the extra full-body copy that ToArray would make (the MemoryStream was
        // created with the default ctor, so its buffer is publicly visible).
        return RequestReadResult.Success(encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }

    private static bool TryResolveEncoding(
        string? contentType,
        IReadOnlyCollection<string> allowedContentTypes,
        out Encoding encoding)
    {
        encoding = Encoding.UTF8;

        if (string.IsNullOrEmpty(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        var mediaType = parsed.MediaType.Value;
        if (mediaType is null || !allowedContentTypes.Any(a => string.Equals(a, mediaType, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (parsed.Encoding is not null)
        {
            encoding = parsed.Encoding;
        }

        return true;
    }
}
