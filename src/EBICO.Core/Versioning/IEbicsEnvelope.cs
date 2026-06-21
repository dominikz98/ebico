namespace EBICO.Core.Versioning;

/// <summary>
/// The version-independent surface shared by every EBICS protocol envelope — request
/// or response, transaction or key-management. It is implemented by the generated
/// per-version envelope bindings through hand-written partial declarations in the
/// <c>EBICO.Core.Schema.{H003,H004,H005}</c> namespaces.
/// </summary>
/// <remarks>
/// The interface is intentionally narrow. The <c>Header</c>/<c>Body</c> of the
/// generated envelopes have a different CLR type per version, so the only common
/// shape would be <see cref="object"/> — which is less useful than the concrete,
/// strongly-typed members the bindings already expose. Code that needs the header or
/// body already knows the version (e.g. via <see cref="ProtocolVersion"/>) and uses
/// the concrete type. See <c>docs/protocol/version-dispatch.md</c> and ADR-0004.
/// </remarks>
public interface IEbicsEnvelope
{
    /// <summary>
    /// The protocol version code carried in the root <c>@Version</c> attribute
    /// (e.g. <c>"H005"</c>), or <see langword="null"/> when it has not been set.
    /// This value is free-text on the wire; prefer <see cref="ProtocolVersion"/> as
    /// the authoritative discriminator.
    /// </summary>
    string? Version { get; set; }

    /// <summary>
    /// The protocol revision carried in the root <c>@Revision</c> attribute, or
    /// <see langword="null"/> when the attribute is absent.
    /// </summary>
    byte? Revision { get; set; }

    /// <summary>
    /// The EBICS protocol family this envelope binding belongs to. Derived from the
    /// binding's CLR namespace, so it is reliable regardless of the (wire-supplied)
    /// <see cref="Version"/> attribute.
    /// </summary>
    EbicsVersion ProtocolVersion { get; }
}
