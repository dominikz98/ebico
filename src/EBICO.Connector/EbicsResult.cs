using EBICO.Core.ReturnCodes;

namespace EBICO.Connector;

/// <summary>
/// The outcome of an <see cref="IEbicsClient.Send{TResult}"/> call. It separates a
/// technically completed exchange that carries a <em>business</em> return code from a genuine
/// technical failure (which is thrown as an exception, not returned here).
/// </summary>
/// <remarks>
/// The six-digit codes come from the central catalogue in
/// <see cref="EBICO.Core.ReturnCodes.EbicsReturnCode"/> (issue #36); <see cref="OkReturnCode"/>
/// mirrors <see cref="EBICO.Core.ReturnCodes.EbicsReturnCode.OkCode"/>. Create instances via
/// <see cref="Success"/> / <see cref="Failure"/> — the <see langword="default"/> value has a
/// <see langword="null"/> <see cref="ReturnCode"/> and is not a meaningful result.
/// </remarks>
/// <typeparam name="T">The value type produced on success.</typeparam>
public readonly record struct EbicsResult<T>
{
    /// <summary>Whether the request completed successfully at the business level.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The produced value; set only when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public T? Value { get; init; }

    /// <summary>The six-digit EBICS return code (e.g. <c>"000000"</c>).</summary>
    public string ReturnCode { get; init; }

    /// <summary>An optional human-readable return text.</summary>
    public string? ReturnText { get; init; }

    /// <summary>The EBICS return code that denotes success (<c>"000000"</c>).</summary>
    public const string OkReturnCode = EbicsReturnCode.OkCode;

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The produced value.</param>
    /// <param name="returnCode">The EBICS return code; defaults to <see cref="OkReturnCode"/>.</param>
    /// <param name="returnText">An optional human-readable return text.</param>
    /// <returns>A successful <see cref="EbicsResult{T}"/>.</returns>
    public static EbicsResult<T> Success(T value, string returnCode = OkReturnCode, string? returnText = null)
        => new()
        {
            IsSuccess = true,
            Value = value,
            ReturnCode = returnCode,
            ReturnText = returnText,
        };

    /// <summary>Creates a non-successful result for a business return code.</summary>
    /// <param name="returnCode">The EBICS return code.</param>
    /// <param name="returnText">An optional human-readable return text.</param>
    /// <returns>A non-successful <see cref="EbicsResult{T}"/> with no value.</returns>
    /// <exception cref="ArgumentException"><paramref name="returnCode"/> is <see langword="null"/> or empty.</exception>
    public static EbicsResult<T> Failure(string returnCode, string? returnText = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(returnCode);
        return new()
        {
            IsSuccess = false,
            Value = default,
            ReturnCode = returnCode,
            ReturnText = returnText,
        };
    }
}
