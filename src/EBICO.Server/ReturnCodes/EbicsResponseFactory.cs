using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.Transactions;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Builds minimal, well-formed <c>ebicsResponse</c> envelopes carrying a single
/// <see cref="EbicsReturnCode"/>, using the committed per-version schema bindings.
/// </summary>
/// <remarks>
/// A technical code lands in <c>header/mutable/ReturnCode</c>, a business code in
/// <c>body/ReturnCode</c>; the respective other slot is filled with <see cref="EbicsReturnCode.OkCode"/>.
/// <b>⚠️ Spec-Vorbehalt:</b> the response is <em>not</em> signed (AuthSignature) — the response
/// authentication signature (X002) is M4. Strict clients may reject unsigned responses. The
/// exact header/body placement and the mandatory-but-empty static header are still to be verified
/// against the official EBICS annexes.
/// </remarks>
public sealed class EbicsResponseFactory
{
    /// <summary>
    /// Builds an <c>ebicsResponse</c> for <paramref name="version"/> reporting
    /// <paramref name="returnCode"/>.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="returnCode">The return code to report.</param>
    /// <returns>The response envelope, ready for serialization.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public IEbicsResponseEnvelope BuildErrorResponse(EbicsVersion version, EbicsReturnCode returnCode)
    {
        var (headerCode, bodyCode, reportText) = Split(returnCode);

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsResponse
            {
                Version = "H003",
                Header = new H003.EbicsResponseHeader
                {
                    Static = new H003.ResponseStaticHeaderType(),
                    Mutable = new H003.ResponseMutableHeaderType
                    {
                        TransactionPhase = H003.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H003.EbicsResponseBody
                {
                    ReturnCode = new H003.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H004 => new H004.EbicsResponse
            {
                Version = "H004",
                Header = new H004.EbicsResponseHeader
                {
                    Static = new H004.ResponseStaticHeaderType(),
                    Mutable = new H004.ResponseMutableHeaderType
                    {
                        TransactionPhase = H004.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H004.EbicsResponseBody
                {
                    ReturnCode = new H004.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H005 => new H005.EbicsResponse
            {
                Version = "H005",
                Header = new H005.EbicsResponseHeader
                {
                    Static = new H005.ResponseStaticHeaderType(),
                    Mutable = new H005.ResponseMutableHeaderType
                    {
                        TransactionPhase = H005.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H005.EbicsResponseBody
                {
                    ReturnCode = new H005.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    /// <summary>
    /// Builds an <c>ebicsResponse</c> for a transaction step (issue #32 upload): it carries the
    /// server-assigned <paramref name="transactionId"/> in the static header, echoes the transaction
    /// <paramref name="phase"/> and — in the transfer phase — the acknowledged
    /// <paramref name="segmentNumber"/> in the mutable header, plus the step's return code.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="phase">The transaction phase to echo (Initialisation/Transfer).</param>
    /// <param name="transactionId">The 16-byte transaction id, or <see langword="null"/> when a failure occurred before one was assigned (then the element is omitted).</param>
    /// <param name="returnCode">The return code to report.</param>
    /// <param name="segmentNumber">The acknowledged segment number in the transfer phase, or <see langword="null"/>.</param>
    /// <param name="lastSegment">Whether the acknowledged segment was the last one of the transaction.</param>
    /// <returns>The transaction response envelope, ready for serialization.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> <c>NumSegments</c> is deliberately not set (the schema restricts it to
    /// download initialisation). Whether the transfer response must echo <c>SegmentNumber</c> is to be
    /// verified against the official EBICS annexes; it is emitted when supplied.
    /// </remarks>
    public IEbicsResponseEnvelope BuildTransactionResponse(
        EbicsVersion version,
        EbicsTransactionPhase phase,
        byte[]? transactionId,
        EbicsReturnCode returnCode,
        ulong? segmentNumber = null,
        bool lastSegment = false)
    {
        var (headerCode, bodyCode, reportText) = Split(returnCode);

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsResponse
            {
                Version = "H003",
                Header = new H003.EbicsResponseHeader
                {
                    Static = new H003.ResponseStaticHeaderType { TransactionId = transactionId },
                    Mutable = new H003.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH003Phase(phase),
                        SegmentNumber = segmentNumber is { } h3Segment
                            ? new H003.ResponseMutableHeaderTypeSegmentNumber { Value = h3Segment, LastSegment = lastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H003.EbicsResponseBody
                {
                    ReturnCode = new H003.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H004 => new H004.EbicsResponse
            {
                Version = "H004",
                Header = new H004.EbicsResponseHeader
                {
                    Static = new H004.ResponseStaticHeaderType { TransactionId = transactionId },
                    Mutable = new H004.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH004Phase(phase),
                        SegmentNumber = segmentNumber is { } h4Segment
                            ? new H004.ResponseMutableHeaderTypeSegmentNumber { Value = h4Segment, LastSegment = lastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H004.EbicsResponseBody
                {
                    ReturnCode = new H004.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H005 => new H005.EbicsResponse
            {
                Version = "H005",
                Header = new H005.EbicsResponseHeader
                {
                    Static = new H005.ResponseStaticHeaderType { TransactionId = transactionId },
                    Mutable = new H005.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH005Phase(phase),
                        SegmentNumber = segmentNumber is { } h5Segment
                            ? new H005.ResponseMutableHeaderTypeSegmentNumber { Value = h5Segment, LastSegment = lastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H005.EbicsResponseBody
                {
                    ReturnCode = new H005.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    /// <summary>
    /// Builds an <c>ebicsResponse</c> for a download transaction step (issue #33): it carries the
    /// transaction id, echoes the <paramref name="result"/> phase and — for a data-bearing step — the
    /// announced <c>NumSegments</c> (initialisation only), the delivered <c>SegmentNumber</c> and the
    /// order-data segment in <c>body/DataTransfer</c>. The initialisation segment additionally carries
    /// the E002 <c>DataEncryptionInfo</c> (encrypted transaction key + recipient-key digest); transfer
    /// segments carry order data only. Receipt and error responses carry no <c>DataTransfer</c>.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="result">The download transaction step outcome to render.</param>
    /// <returns>The download response envelope, ready for serialization.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> the canonical placement (NumSegments + segment 1 in the initialisation
    /// response, segments 2..N in the transfer responses, DataEncryptionInfo in the initialisation
    /// response only) is to be verified against the official EBICS annexes. The response is not signed
    /// (X002 is M4).
    /// </remarks>
    public IEbicsResponseEnvelope BuildDownloadResponse(EbicsVersion version, DownloadTransactionResult result)
    {
        var (headerCode, bodyCode, reportText) = Split(result.ReturnCode);

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsResponse
            {
                Version = "H003",
                Header = new H003.EbicsResponseHeader
                {
                    Static = new H003.ResponseStaticHeaderType
                    {
                        TransactionId = result.TransactionId,
                        NumSegments = result.NumSegments,
                    },
                    Mutable = new H003.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH003Phase(result.Phase),
                        SegmentNumber = result.SegmentNumber is { } h3Segment
                            ? new H003.ResponseMutableHeaderTypeSegmentNumber { Value = h3Segment, LastSegment = result.LastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H003.EbicsResponseBody
                {
                    DataTransfer = BuildH003DataTransfer(result.Segment),
                    ReturnCode = new H003.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H004 => new H004.EbicsResponse
            {
                Version = "H004",
                Header = new H004.EbicsResponseHeader
                {
                    Static = new H004.ResponseStaticHeaderType
                    {
                        TransactionId = result.TransactionId,
                        NumSegments = result.NumSegments,
                    },
                    Mutable = new H004.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH004Phase(result.Phase),
                        SegmentNumber = result.SegmentNumber is { } h4Segment
                            ? new H004.ResponseMutableHeaderTypeSegmentNumber { Value = h4Segment, LastSegment = result.LastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H004.EbicsResponseBody
                {
                    DataTransfer = BuildH004DataTransfer(result.Segment),
                    ReturnCode = new H004.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H005 => new H005.EbicsResponse
            {
                Version = "H005",
                Header = new H005.EbicsResponseHeader
                {
                    Static = new H005.ResponseStaticHeaderType
                    {
                        TransactionId = result.TransactionId,
                        NumSegments = result.NumSegments,
                    },
                    Mutable = new H005.ResponseMutableHeaderType
                    {
                        TransactionPhase = ToH005Phase(result.Phase),
                        SegmentNumber = result.SegmentNumber is { } h5Segment
                            ? new H005.ResponseMutableHeaderTypeSegmentNumber { Value = h5Segment, LastSegment = result.LastSegment }
                            : null,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H005.EbicsResponseBody
                {
                    DataTransfer = BuildH005DataTransfer(result.Segment),
                    ReturnCode = new H005.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    // Builds the H003 download DataTransfer: OrderData always, DataEncryptionInfo only on the
    // initialisation segment (when the encrypted transaction key is present). Null for receipt/errors.
    private static H003.DataTransferResponseType? BuildH003DataTransfer(DownloadSegmentPayload? segment)
    {
        if (segment is not { } payload)
        {
            return null;
        }

        var dataTransfer = new H003.DataTransferResponseType
        {
            OrderData = new H003.DataTransferResponseTypeOrderData { Value = payload.OrderData },
        };

        if (payload.EncryptedTransactionKey is not null
            && payload.EncryptionPubKeyDigest is not null
            && payload.EncryptionVersion is { } version)
        {
            dataTransfer.DataEncryptionInfo = new H003.DataTransferResponseTypeDataEncryptionInfo
            {
                EncryptionPubKeyDigest = new H003.DataEncryptionInfoTypeEncryptionPubKeyDigest
                {
                    Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                    Version = version.Value,
                    Value = payload.EncryptionPubKeyDigest,
                },
                TransactionKey = payload.EncryptedTransactionKey,
            };
        }

        return dataTransfer;
    }

    // Builds the H004 download DataTransfer (see BuildH003DataTransfer).
    private static H004.DataTransferResponseType? BuildH004DataTransfer(DownloadSegmentPayload? segment)
    {
        if (segment is not { } payload)
        {
            return null;
        }

        var dataTransfer = new H004.DataTransferResponseType
        {
            OrderData = new H004.DataTransferResponseTypeOrderData { Value = payload.OrderData },
        };

        if (payload.EncryptedTransactionKey is not null
            && payload.EncryptionPubKeyDigest is not null
            && payload.EncryptionVersion is { } version)
        {
            dataTransfer.DataEncryptionInfo = new H004.DataTransferResponseTypeDataEncryptionInfo
            {
                EncryptionPubKeyDigest = new H004.DataEncryptionInfoTypeEncryptionPubKeyDigest
                {
                    Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                    Version = version.Value,
                    Value = payload.EncryptionPubKeyDigest,
                },
                TransactionKey = payload.EncryptedTransactionKey,
            };
        }

        return dataTransfer;
    }

    // Builds the H005 download DataTransfer (see BuildH003DataTransfer).
    private static H005.DataTransferResponseType? BuildH005DataTransfer(DownloadSegmentPayload? segment)
    {
        if (segment is not { } payload)
        {
            return null;
        }

        var dataTransfer = new H005.DataTransferResponseType
        {
            OrderData = new H005.DataTransferResponseTypeOrderData { Value = payload.OrderData },
        };

        if (payload.EncryptedTransactionKey is not null
            && payload.EncryptionPubKeyDigest is not null
            && payload.EncryptionVersion is { } version)
        {
            dataTransfer.DataEncryptionInfo = new H005.DataTransferResponseTypeDataEncryptionInfo
            {
                EncryptionPubKeyDigest = new H005.DataEncryptionInfoTypeEncryptionPubKeyDigest
                {
                    Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                    Version = version.Value,
                    Value = payload.EncryptionPubKeyDigest,
                },
                TransactionKey = payload.EncryptedTransactionKey,
            };
        }

        return dataTransfer;
    }

    private static H003.TransactionPhaseType ToH003Phase(EbicsTransactionPhase phase) => phase switch
    {
        EbicsTransactionPhase.Transfer => H003.TransactionPhaseType.Transfer,
        EbicsTransactionPhase.Receipt => H003.TransactionPhaseType.Receipt,
        _ => H003.TransactionPhaseType.Initialisation,
    };

    private static H004.TransactionPhaseType ToH004Phase(EbicsTransactionPhase phase) => phase switch
    {
        EbicsTransactionPhase.Transfer => H004.TransactionPhaseType.Transfer,
        EbicsTransactionPhase.Receipt => H004.TransactionPhaseType.Receipt,
        _ => H004.TransactionPhaseType.Initialisation,
    };

    private static H005.TransactionPhaseType ToH005Phase(EbicsTransactionPhase phase) => phase switch
    {
        EbicsTransactionPhase.Transfer => H005.TransactionPhaseType.Transfer,
        EbicsTransactionPhase.Receipt => H005.TransactionPhaseType.Receipt,
        _ => H005.TransactionPhaseType.Initialisation,
    };

    /// <summary>
    /// Builds an <c>ebicsKeyManagementResponse</c> for <paramref name="version"/> reporting
    /// <paramref name="returnCode"/>. This is the response envelope for the unsigned key-management
    /// orders (INI, HIA and — once implemented — HPB), which are <em>not</em> answered with a plain
    /// <c>ebicsResponse</c>.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="returnCode">The return code to report.</param>
    /// <returns>The key-management response envelope, ready for serialization.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public IEbicsResponseEnvelope BuildKeyManagementResponse(EbicsVersion version, EbicsReturnCode returnCode)
    {
        var (headerCode, bodyCode, reportText) = Split(returnCode);

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsKeyManagementResponse
            {
                Version = "H003",
                Header = new H003.EbicsKeyManagementResponseHeader
                {
                    Static = new H003.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H003.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H003.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H003.EbicsKeyManagementResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H004 => new H004.EbicsKeyManagementResponse
            {
                Version = "H004",
                Header = new H004.EbicsKeyManagementResponseHeader
                {
                    Static = new H004.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H004.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H004.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H004.EbicsKeyManagementResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H005 => new H005.EbicsKeyManagementResponse
            {
                Version = "H005",
                Header = new H005.EbicsKeyManagementResponseHeader
                {
                    Static = new H005.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H005.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H005.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H005.EbicsKeyManagementResponseBodyReturnCode { Value = bodyCode },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    /// <summary>
    /// Builds a successful <c>ebicsKeyManagementResponse</c> for <paramref name="version"/> that
    /// carries an encrypted key-management <c>DataTransfer</c> (the <c>HPB</c> download response): the
    /// bank's public keys as E002-encrypted, compressed order data. Header and body return codes are
    /// <see cref="EbicsReturnCode.Ok"/>.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="payload">The encrypted transaction key, encrypted order data and recipient key digest.</param>
    /// <returns>The key-management response envelope with a populated <c>DataTransfer</c>, ready for serialization.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public IEbicsResponseEnvelope BuildKeyManagementResponse(EbicsVersion version, EbicsKeyManagementPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsKeyManagementResponse
            {
                Version = "H003",
                Header = new H003.EbicsKeyManagementResponseHeader
                {
                    Static = new H003.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H003.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = EbicsReturnCode.OkCode,
                        ReportText = EbicsReturnCode.Ok.SymbolicName,
                    },
                },
                Body = new H003.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H003.EbicsKeyManagementResponseBodyReturnCode { Value = EbicsReturnCode.OkCode },
                    DataTransfer = new H003.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H003.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H003.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = payload.EncryptionVersion.Value,
                                Value = payload.EncryptionPubKeyDigest,
                            },
                            TransactionKey = payload.EncryptedTransactionKey,
                        },
                        OrderData = new H003.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = payload.EncryptedOrderData },
                    },
                },
            },
            EbicsVersion.H004 => new H004.EbicsKeyManagementResponse
            {
                Version = "H004",
                Header = new H004.EbicsKeyManagementResponseHeader
                {
                    Static = new H004.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H004.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = EbicsReturnCode.OkCode,
                        ReportText = EbicsReturnCode.Ok.SymbolicName,
                    },
                },
                Body = new H004.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H004.EbicsKeyManagementResponseBodyReturnCode { Value = EbicsReturnCode.OkCode },
                    DataTransfer = new H004.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H004.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H004.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = payload.EncryptionVersion.Value,
                                Value = payload.EncryptionPubKeyDigest,
                            },
                            TransactionKey = payload.EncryptedTransactionKey,
                        },
                        OrderData = new H004.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = payload.EncryptedOrderData },
                    },
                },
            },
            EbicsVersion.H005 => new H005.EbicsKeyManagementResponse
            {
                Version = "H005",
                Header = new H005.EbicsKeyManagementResponseHeader
                {
                    Static = new H005.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H005.KeyMgmntResponseMutableHeaderType
                    {
                        ReturnCode = EbicsReturnCode.OkCode,
                        ReportText = EbicsReturnCode.Ok.SymbolicName,
                    },
                },
                Body = new H005.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H005.EbicsKeyManagementResponseBodyReturnCode { Value = EbicsReturnCode.OkCode },
                    DataTransfer = new H005.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H005.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H005.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = payload.EncryptionVersion.Value,
                                Value = payload.EncryptionPubKeyDigest,
                            },
                            TransactionKey = payload.EncryptedTransactionKey,
                        },
                        OrderData = new H005.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = payload.EncryptedOrderData },
                    },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    // Splits a return code into the header (technical) and body (business) slots, filling the unused
    // slot with EBICS_OK. ReportText interprets the *header* return code, so it stays consistent with
    // it: for a business code the header reports EBICS_OK (the message exchange succeeded; the
    // order-level result is in body/ReturnCode). The body return code has no text slot in the schema.
    private static (string HeaderCode, string BodyCode, string ReportText) Split(EbicsReturnCode returnCode)
    {
        var headerCode = returnCode.Kind == EbicsReturnCodeKind.Technical ? returnCode.Code : EbicsReturnCode.OkCode;
        var bodyCode = returnCode.Kind == EbicsReturnCodeKind.Business ? returnCode.Code : EbicsReturnCode.OkCode;
        var reportText = returnCode.Kind == EbicsReturnCodeKind.Technical
            ? returnCode.SymbolicName
            : EbicsReturnCode.Ok.SymbolicName;

        return (headerCode, bodyCode, reportText);
    }
}
