using EBICO.Server.ReturnCodes;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The outcome of the pipeline's <em>verify</em> stage.
/// </summary>
/// <param name="IsVerified">Whether the request passed verification.</param>
/// <param name="Failure">The return code to report when verification failed; <see langword="null"/> on success.</param>
public readonly record struct EbicsVerificationResult(bool IsVerified, EbicsReturnCode? Failure)
{
    /// <summary>A successful verification result.</summary>
    public static EbicsVerificationResult Success { get; } = new(true, null);

    /// <summary>Creates a failed verification result carrying <paramref name="code"/>.</summary>
    /// <param name="code">The return code describing the failure.</param>
    /// <returns>A failed <see cref="EbicsVerificationResult"/>.</returns>
    public static EbicsVerificationResult Fail(EbicsReturnCode code) => new(false, code);
}

/// <summary>
/// Extension point for the pipeline's <em>verify</em> stage: signature/authentication and
/// subscriber-state checks. The skeleton (#25) registers the no-op
/// <see cref="NoOpEbicsRequestVerifier"/>; the real checks (X002 auth signature, HostID/User known,
/// subscriber lifecycle) land with the M3/M4 key-management issues.
/// </summary>
public interface IEbicsRequestVerifier
{
    /// <summary>Verifies the request described by <paramref name="context"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The verification result.</returns>
    Task<EbicsVerificationResult> VerifyAsync(EbicsRequestContext context, CancellationToken ct = default);
}

/// <summary>
/// The default no-op verifier used by the host skeleton: every request passes. Replaced by a real
/// verifier in a later issue.
/// </summary>
public sealed class NoOpEbicsRequestVerifier : IEbicsRequestVerifier
{
    /// <inheritdoc />
    public Task<EbicsVerificationResult> VerifyAsync(EbicsRequestContext context, CancellationToken ct = default)
        => Task.FromResult(EbicsVerificationResult.Success);
}
