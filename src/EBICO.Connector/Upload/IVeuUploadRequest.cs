namespace EBICO.Connector.Upload;

/// <summary>
/// Internal shape shared by the VEU upload convenience requests
/// (<see cref="HveUploadRequest"/>, <see cref="HvsUploadRequest"/>): it projects the request onto the
/// generic upload inputs plus the referenced parked order, so a single
/// <see cref="VeuUploadHandlerBase{TRequest}"/> can drive them all.
/// </summary>
internal interface IVeuUploadRequest
{
    /// <summary>The order payload sent with the VEU action, as raw bytes.</summary>
    ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The classical order type code (<c>"HVE"</c> or <c>"HVS"</c>).</summary>
    string OrderType { get; }

    /// <summary>The parked order the action applies to.</summary>
    VeuOrderReference Order { get; }

    /// <summary>The maximum raw segment size in bytes, or <see langword="null"/> for the connector default.</summary>
    int? MaxSegmentSizeBytes { get; }
}
