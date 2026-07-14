using EBICO.Core.Schema.H005;

namespace EBICO.Core.Btf;

/// <summary>
/// A typed EBICS 3.0 (H005) <b>Business Transaction Format</b> (BTF): the business-level identity of
/// an order carried by the generic <c>BTU</c> (upload) / <c>BTD</c> (download) admin order types. It
/// combines the service code (<see cref="Service"/> + optional <see cref="Option"/>/<see cref="Scope"/>/
/// <see cref="Container"/>) with the message it transports (<see cref="MessageName"/> plus the optional
/// ISO 20022 <see cref="MessageVariant"/>/<see cref="MessageVersion"/> and encoding <see cref="MessageFormat"/>).
/// </summary>
/// <remarks>
/// This is the hand-written domain projection of the generated <see cref="ServiceType"/> binding; use
/// <see cref="FromSchema(ServiceType)"/> / <see cref="ToServiceType"/> to convert. Construct via
/// <see cref="BusinessTransactionFormat(string, string?, string?, ContainerStringType?, string?, string?, string?, string?)"/>;
/// the <see langword="default"/> value has a <see langword="null"/> <see cref="Service"/>.
/// </remarks>
public readonly record struct BusinessTransactionFormat
{
    /// <summary>Creates a business transaction format.</summary>
    /// <param name="service">The mandatory service code (<c>ServiceName</c>, e.g. <c>"SCT"</c>, <c>"SDD"</c>, <c>"EOP"</c>).</param>
    /// <param name="option">The optional service option (<c>ServiceOption</c>, e.g. <c>"COR"</c>, <c>"B2B"</c>).</param>
    /// <param name="scope">The optional scope (ISO country / issuer code) defining whose rules apply.</param>
    /// <param name="container">The optional container flag (<c>SVC</c>/<c>XML</c>/<c>ZIP</c>).</param>
    /// <param name="messageName">The optional message name (<c>MsgName</c>, e.g. <c>"pain.001"</c>, <c>"camt.053"</c>, <c>"mt940"</c>).</param>
    /// <param name="messageVariant">The optional ISO 20022 message variant.</param>
    /// <param name="messageVersion">The optional ISO 20022 message version.</param>
    /// <param name="messageFormat">The optional message encoding format (e.g. <c>"XML"</c>, <c>"JSON"</c>, <c>"PDF"</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="service"/> is null, empty or whitespace.</exception>
    public BusinessTransactionFormat(
        string service,
        string? option = null,
        string? scope = null,
        ContainerStringType? container = null,
        string? messageName = null,
        string? messageVariant = null,
        string? messageVersion = null,
        string? messageFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        Service = service;
        Option = option;
        Scope = scope;
        Container = container;
        MessageName = messageName;
        MessageVariant = messageVariant;
        MessageVersion = messageVersion;
        MessageFormat = messageFormat;
    }

    /// <summary>The service code (<c>ServiceName</c>) — the target system for further processing.</summary>
    public string Service { get; }

    /// <summary>The optional service option (<c>ServiceOption</c>), or <see langword="null"/>.</summary>
    public string? Option { get; }

    /// <summary>The optional scope (ISO country / issuer code) defining whose rules apply, or <see langword="null"/>.</summary>
    public string? Scope { get; }

    /// <summary>The optional container flag (<c>SVC</c>/<c>XML</c>/<c>ZIP</c>), or <see langword="null"/> when absent.</summary>
    public ContainerStringType? Container { get; }

    /// <summary>The optional message name (<c>MsgName</c>, e.g. <c>"pain.001"</c>), or <see langword="null"/>.</summary>
    public string? MessageName { get; }

    /// <summary>The optional ISO 20022 message variant, or <see langword="null"/>.</summary>
    public string? MessageVariant { get; }

    /// <summary>The optional ISO 20022 message version, or <see langword="null"/>.</summary>
    public string? MessageVersion { get; }

    /// <summary>The optional message encoding format (e.g. <c>"XML"</c>), or <see langword="null"/>.</summary>
    public string? MessageFormat { get; }

    /// <summary>
    /// A deterministic, human-readable key for this BTF (e.g. <c>"SCT:pain.001:COR"</c>), built from the
    /// non-empty components. Used for logging and as the fall-back authorisation key when the BTF has no
    /// classical order-type mapping.
    /// </summary>
    public string CanonicalKey => string.Join(
        ":",
        new[] { Service, MessageName, Option, Scope, MessageVariant, MessageVersion, MessageFormat, ContainerToXmlValue(Container) }
            .Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// Projects a generated <see cref="ServiceType"/> (or <see cref="RestrictedServiceType"/>) binding into
    /// a <see cref="BusinessTransactionFormat"/>.
    /// </summary>
    /// <param name="service">The generated service binding.</param>
    /// <returns>The typed business transaction format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="ServiceType.ServiceName"/> is null, empty or whitespace.</exception>
    public static BusinessTransactionFormat FromSchema(ServiceType service)
    {
        ArgumentNullException.ThrowIfNull(service);

        var message = service.MsgName;
        return new BusinessTransactionFormat(
            service: service.ServiceName,
            option: NullIfEmpty(service.ServiceOption),
            scope: NullIfEmpty(service.Scope),
            container: TryReadContainer(service.Container),
            messageName: NullIfEmpty(message?.Value),
            messageVariant: NullIfEmpty(message?.Variant),
            messageVersion: NullIfEmpty(message?.Version),
            messageFormat: NullIfEmpty(message?.Format));
    }

    /// <summary>
    /// Extracts the BTF carried by a generated <see cref="BtfParamsTyp"/> (i.e. a <c>BTUOrderParams</c> /
    /// <c>BTDOrderParams</c>) via its <see cref="BtfParamsTyp.Service"/> element.
    /// </summary>
    /// <param name="btfParams">The BTF order-params binding, or <see langword="null"/>.</param>
    /// <param name="btf">The extracted business transaction format when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a service with a non-empty <c>ServiceName</c> was present.</returns>
    public static bool TryFromBtfParams(BtfParamsTyp? btfParams, out BusinessTransactionFormat btf)
    {
        if (btfParams?.Service is { } service && !string.IsNullOrWhiteSpace(service.ServiceName))
        {
            btf = FromSchema(service);
            return true;
        }

        btf = default;
        return false;
    }

    /// <summary>Builds a generated <see cref="ServiceType"/> binding from this BTF.</summary>
    /// <returns>The populated service binding.</returns>
    public ServiceType ToServiceType()
    {
        var service = new ServiceType();
        Populate(service);
        return service;
    }

    /// <summary>Builds a generated <see cref="RestrictedServiceType"/> binding from this BTF.</summary>
    /// <returns>The populated restricted service binding (as used by <c>BTUOrderParams</c>/<c>BTDOrderParams</c>).</returns>
    public RestrictedServiceType ToRestrictedServiceType()
    {
        var service = new RestrictedServiceType();
        Populate(service);
        return service;
    }

    private void Populate(ServiceType service)
    {
        service.ServiceName = Service;
        service.ServiceOption = Option;
        service.Scope = Scope;

        // The container's SVC/XML/ZIP value lives on an untyped attribute in the generated binding
        // (see docs/server/btf-framework.md — Spec-Vorbehalt); only its presence is projected here.
        if (Container.HasValue)
        {
            service.Container = new ContainerFlagType();
        }

        if (MessageName is not null || MessageVariant is not null || MessageVersion is not null || MessageFormat is not null)
        {
            service.MsgName = new MessageType
            {
                Value = MessageName,
                Variant = MessageVariant,
                Version = MessageVersion,
                Format = MessageFormat,
            };
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static ContainerStringType? TryReadContainer(ContainerFlagType? flag)
    {
        if (flag is null)
        {
            return null;
        }

        foreach (var attribute in flag.AnyAttribute)
        {
            switch (attribute.Value)
            {
                case "SVC": return ContainerStringType.Svc;
                case "XML": return ContainerStringType.Xml;
                case "ZIP": return ContainerStringType.Zip;
            }
        }

        return null;
    }

    private static string? ContainerToXmlValue(ContainerStringType? container) => container switch
    {
        ContainerStringType.Svc => "SVC",
        ContainerStringType.Xml => "XML",
        ContainerStringType.Zip => "ZIP",
        _ => null,
    };
}
