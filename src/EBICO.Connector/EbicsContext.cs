using EBICO.Connector.Configuration;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core.Versioning;

namespace EBICO.Connector;

/// <summary>
/// The execution context handed to an <see cref="IEbicsRequestHandler{TRequest, TResult}"/> for
/// a single <see cref="IEbicsClient.Send{TResult}"/> call. It bundles the resolved connection
/// together with the collaborators a handler needs (key store, transport), so handlers stay
/// free of service-location concerns.
/// </summary>
/// <remarks>Created by the client per send; handlers never construct it.</remarks>
public sealed class EbicsContext
{
    internal EbicsContext(EbicsConnection connection, IKeyStore keys, ITransport transport)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(transport);

        Connection = connection;
        Keys = keys;
        Transport = transport;
    }

    /// <summary>The resolved, validated connection configuration.</summary>
    public EbicsConnection Connection { get; }

    /// <summary>The target protocol version metadata (shortcut for <c>Connection.VersionInfo</c>).</summary>
    public EbicsVersionInfo Version => Connection.VersionInfo;

    /// <summary>The key store providing subscriber and bank key material.</summary>
    public IKeyStore Keys { get; }

    /// <summary>The transport used to exchange EBICS messages with the server.</summary>
    public ITransport Transport { get; }
}
