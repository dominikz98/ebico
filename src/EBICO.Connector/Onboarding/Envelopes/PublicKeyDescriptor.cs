using System.Security.Cryptography.X509Certificates;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// Describes one public key to place into onboarding order data: the key material, its EBICS key
/// version code (e.g. <c>"A005"</c>/<c>"X002"</c>/<c>"E002"</c>) and — for the certificate-based
/// H005 procedure — the X.509 certificate carrying it. Pure-key procedures (H003/H004) leave
/// <see cref="Certificate"/> <see langword="null"/> and use the modulus/exponent of <see cref="Key"/>.
/// </summary>
/// <param name="Key">The public key material.</param>
/// <param name="KeyVersion">The EBICS key version code declared in the order data.</param>
/// <param name="Certificate">The X.509 certificate (H005), or <see langword="null"/> for RSAKeyValue procedures.</param>
public sealed record PublicKeyDescriptor(RsaKeyMaterial Key, string KeyVersion, X509Certificate2? Certificate = null);
