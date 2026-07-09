namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// A version-agnostic projection of an <c>ebicsKeyManagementResponse</c> (the response to INI, HIA
/// and HPB). The version-specific builder maps the concrete H003/H004/H005 binding onto this shape
/// so the handlers do not touch generated types.
/// </summary>
public sealed class KeyManagementResponseView
{
    /// <summary>The six-digit business return code (<c>Body/ReturnCode</c>), e.g. <c>"000000"</c>.</summary>
    public required string ReturnCode { get; init; }

    /// <summary>The optional human-readable report text from the response header.</summary>
    public string? ReportText { get; init; }

    /// <summary>The RSA-encrypted transaction key from the HPB data transfer (<see langword="null"/> for INI/HIA).</summary>
    public byte[]? EncryptedTransactionKey { get; init; }

    /// <summary>The encrypted, compressed HPB order data (<see langword="null"/> for INI/HIA).</summary>
    public byte[]? EncryptedOrderData { get; init; }
}
