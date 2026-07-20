using EBICO.Connector.Configuration;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.ReturnCodes;

namespace EBICO.Connector.Validation;

/// <summary>
/// The client-side send-pipeline stage 1 (<i>Validierung — Berechtigung, BTF</i>): validates a request
/// <b>before</b> any key I/O, crypto, serialisation or transport, so a malformed or unauthorised request
/// fails fast without a server round-trip. It is a pure, static helper (no DI service, mirroring the
/// server-side BTF/authorisation decision in ADR-0016) invoked at the top of the upload/download executors.
/// </summary>
/// <remarks>
/// <para>
/// Two responsibilities: (1) <b>structural/BTF</b> validation — the order identity resolves for the
/// configured version and direction, a catalogued order type is not used in the wrong direction, the
/// upload payload is non-empty and an explicit segment size is positive; violations are a
/// programming/configuration error and throw <see cref="EbicsConfigurationException"/>. (2) opt-in
/// <b>authorisation</b> ("Berechtigung") — when <see cref="EbicsConnection.AllowedOrderTypes"/> is
/// non-empty, a request whose effective classical order type is not listed is denied locally with
/// <see cref="EbicsReturnCode.AuthorisationOrderTypeFailed"/> (<c>090003</c>), exactly as the bank would.
/// The allow-list is a convenience guard, not the authorisation authority; the bank remains authoritative.
/// </para>
/// <para>Onboarding (INI/HIA/HPB) does not run through the executors and is therefore never validated here.</para>
/// </remarks>
internal static class RequestValidator
{
    /// <summary>The generic H003/H004 file-upload order type.</summary>
    private const string FulOrderType = "FUL";

    /// <summary>The generic H005 business-transaction-upload order type.</summary>
    private const string BtuOrderType = "BTU";

    /// <summary>The generic H003/H004 download order type (file download).</summary>
    private const string FdlOrderType = "FDL";

    /// <summary>The generic H005 download order type (business transaction download).</summary>
    private const string BtdOrderType = "BTD";

    /// <summary>Validates an upload request and resolves its version-appropriate order identity.</summary>
    /// <param name="connection">The resolved connection (carries the version and the client-side allow-list).</param>
    /// <param name="orderData">The upload payload; must not be empty.</param>
    /// <param name="maxSegmentSizeBytes">The requested maximum segment size, or <see langword="null"/> for the default; when supplied it must be positive.</param>
    /// <param name="orderType">The classical order type, or <see langword="null"/>.</param>
    /// <param name="btf">The H005 business transaction format, or <see langword="null"/>.</param>
    /// <param name="fileFormat">The H003/H004 FUL file format, or <see langword="null"/>.</param>
    /// <returns>An authorised outcome with the resolved identity, or a denied outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsConfigurationException">The payload is empty, the segment size is not positive, the order type is used in the wrong direction, or the order identity is incomplete.</exception>
    public static RequestValidation<ValidatedUploadIdentity> ValidateUpload(
        EbicsConnection connection,
        ReadOnlyMemory<byte> orderData,
        int? maxSegmentSizeBytes,
        string? orderType,
        BusinessTransactionFormat? btf,
        string? fileFormat)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (orderData.IsEmpty)
        {
            throw new EbicsConfigurationException("The upload order data must not be empty.");
        }

        if (maxSegmentSizeBytes is <= 0)
        {
            throw new EbicsConfigurationException("The maximum segment size must be a positive number of bytes.");
        }

        CheckDirection(orderType, isUpload: true);

        var identity = ResolveUploadIdentity(connection.Version, orderType, btf, fileFormat);
        return Authorize(connection, identity.EffectiveOrderType, identity);
    }

    /// <summary>Validates a download request and resolves its version-appropriate order identity.</summary>
    /// <param name="connection">The resolved connection (carries the version and the client-side allow-list).</param>
    /// <param name="orderType">The classical or administrative order type, or <see langword="null"/>.</param>
    /// <param name="btf">The H005 business transaction format, or <see langword="null"/>.</param>
    /// <param name="fileFormat">The H003/H004 FDL file format, or <see langword="null"/>.</param>
    /// <returns>An authorised outcome with the resolved identity, or a denied outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsConfigurationException">The order type is used in the wrong direction or the order identity is incomplete.</exception>
    public static RequestValidation<ValidatedDownloadIdentity> ValidateDownload(
        EbicsConnection connection,
        string? orderType,
        BusinessTransactionFormat? btf,
        string? fileFormat)
    {
        ArgumentNullException.ThrowIfNull(connection);

        CheckDirection(orderType, isUpload: false);

        var identity = ResolveDownloadIdentity(connection.Version, orderType, btf, fileFormat);
        return Authorize(connection, identity.EffectiveOrderType, identity);
    }

    // Applies the opt-in client-side allow-list to the effective classical order type. An empty allow-list
    // (the default) defers authorisation to the server; an unresolved effective type is not checked here
    // (an incomplete identity has already thrown in the resolve step).
    private static RequestValidation<TIdentity> Authorize<TIdentity>(
        EbicsConnection connection, string? effectiveOrderType, TIdentity identity)
    {
        if (connection.AllowedOrderTypes.Count > 0
            && effectiveOrderType is not null
            && !connection.AllowedOrderTypes.Contains(effectiveOrderType))
        {
            return RequestValidation<TIdentity>.Denied(
                EbicsReturnCode.AuthorisationOrderTypeFailed.Code,
                $"The subscriber is not authorised for order type '{effectiveOrderType}' by the client-side " +
                $"allow-list ({EbicsReturnCode.AuthorisationOrderTypeFailed.SymbolicName}).");
        }

        return RequestValidation<TIdentity>.Authorized(identity);
    }

    // Rejects using a catalogued order type in the wrong direction (e.g. a download code such as "STA" on an
    // upload). Order types that are not in the BTF catalog — including the administrative codes (HTD/HKD/…) —
    // pass through, because the catalog is a best-effort seed, not an exhaustive registry.
    private static void CheckDirection(string? orderType, bool isUpload)
    {
        if (string.IsNullOrWhiteSpace(orderType))
        {
            return;
        }

        if (isUpload
            && BtfOrderTypeCatalog.IsDownloadOrderType(orderType)
            && !BtfOrderTypeCatalog.IsUploadOrderType(orderType))
        {
            throw new EbicsConfigurationException(
                $"Order type '{orderType}' is a download order type and cannot be used for an upload request.");
        }

        if (!isUpload
            && BtfOrderTypeCatalog.IsUploadOrderType(orderType)
            && !BtfOrderTypeCatalog.IsDownloadOrderType(orderType))
        {
            throw new EbicsConfigurationException(
                $"Order type '{orderType}' is an upload order type and cannot be used for a download request.");
        }
    }

    // Resolves the version-specific upload order identity: H005 submits BTU + a BTF (resolved from the order
    // type when not supplied); H003/H004 submit a classical order type directly, or FUL + a file format.
    private static ValidatedUploadIdentity ResolveUploadIdentity(
        EbicsVersion version, string? orderType, BusinessTransactionFormat? btf, string? fileFormat)
    {
        if (version == EbicsVersion.H005)
        {
            var resolvedBtf = btf;
            if (resolvedBtf is null)
            {
                if (string.IsNullOrEmpty(orderType) || !BtfOrderTypeCatalog.TryGetBtf(orderType, out var mapped))
                {
                    throw new EbicsConfigurationException(
                        $"H005 uploads require a business transaction format (BTF); none was supplied and " +
                        $"order type '{orderType}' has no BTF mapping.");
                }

                resolvedBtf = mapped;
            }

            var effective = BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, resolvedBtf, null);
            return new ValidatedUploadIdentity(BtuOrderType, resolvedBtf, null, effective);
        }

        if (!string.IsNullOrEmpty(fileFormat))
        {
            var effective = BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, null, fileFormat);
            return new ValidatedUploadIdentity(FulOrderType, null, fileFormat, effective);
        }

        if (string.IsNullOrEmpty(orderType))
        {
            throw new EbicsConfigurationException(
                "H003/H004 uploads require an order type (e.g. \"CCT\") or a file format for the generic FUL upload.");
        }

        var directEffective = BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, null, null);
        return new ValidatedUploadIdentity(orderType, null, null, directEffective);
    }

    // Resolves the version-specific download order identity: H005 requests statements as BTD + a BTF
    // (resolved from the order type when not supplied) and administrative orders (HTD/…) as their
    // AdminOrderType directly; H003/H004 request a classical order type directly, or FDL + a file format.
    private static ValidatedDownloadIdentity ResolveDownloadIdentity(
        EbicsVersion version, string? orderType, BusinessTransactionFormat? btf, string? fileFormat)
    {
        if (version == EbicsVersion.H005)
        {
            if (btf is { } explicitBtf)
            {
                var effective = BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, explicitBtf, null);
                return new ValidatedDownloadIdentity(BtdOrderType, explicitBtf, null, effective);
            }

            if (string.IsNullOrEmpty(orderType))
            {
                throw new EbicsConfigurationException(
                    "H005 downloads require an order type (e.g. \"STA\") or a business transaction format (BTF).");
            }

            // Statement/report order types map to a BTF (BTD); administrative order types (HTD/HKD/…)
            // are not BTF services and stay AdminOrderTypes (their own code is the effective key).
            if (BtfOrderTypeCatalog.TryGetBtf(orderType, out var mapped))
            {
                var effective = BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, mapped, null);
                return new ValidatedDownloadIdentity(BtdOrderType, mapped, null, effective);
            }

            return new ValidatedDownloadIdentity(orderType, null, null, orderType);
        }

        if (!string.IsNullOrEmpty(fileFormat))
        {
            var effective = BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, null, fileFormat);
            return new ValidatedDownloadIdentity(FdlOrderType, null, fileFormat, effective);
        }

        if (string.IsNullOrEmpty(orderType))
        {
            throw new EbicsConfigurationException(
                "H003/H004 downloads require an order type (e.g. \"STA\") or a file format for the generic FDL download.");
        }

        var directEffective = BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, null, null);
        return new ValidatedDownloadIdentity(orderType, null, null, directEffective);
    }
}
