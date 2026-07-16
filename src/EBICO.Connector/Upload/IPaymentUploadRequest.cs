namespace EBICO.Connector.Upload;

/// <summary>
/// Internal shape shared by the SEPA payment convenience requests
/// (<see cref="CctUploadRequest"/> etc.): it projects the request onto the generic upload inputs
/// (payload + classical order type) so a single <see cref="PaymentUploadHandlerBase{TRequest}"/> can
/// drive them all.
/// </summary>
internal interface IPaymentUploadRequest
{
    /// <summary>The order payload (a pain.001/pain.008 message), as raw bytes.</summary>
    ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The classical order type code (e.g. <c>"CCT"</c>).</summary>
    string OrderType { get; }

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    int? MaxSegmentSizeBytes { get; }
}
