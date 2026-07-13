using System.Security.Cryptography;
using EBICO.Core.Crypto;

namespace EBICO.Server.Handlers;

/// <summary>
/// Runs the order-data decode step of a handler (envelope extraction, E002 decryption,
/// decompression, deserialization, key reconstruction and key-version policy) and converts the
/// expected low-level failures into a single <see cref="EbicsOrderDataException"/>. This keeps the
/// list of "invalid order data" exception types in one place and lets the central error mapper make
/// an unambiguous mapping to <c>EBICS_INVALID_ORDER_DATA_FORMAT</c>.
/// </summary>
internal static class OrderDataFault
{
    /// <summary>
    /// Runs <paramref name="decode"/> and rethrows any of the known order-data failures as an
    /// <see cref="EbicsOrderDataException"/>. Exceptions outside the known set propagate unchanged
    /// (they denote a server fault, not invalid client order data).
    /// </summary>
    /// <typeparam name="T">The decoded order-data type.</typeparam>
    /// <param name="decode">The synchronous decode step.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="EbicsOrderDataException">The order data could not be decoded or validated.</exception>
    public static T Wrap<T>(Func<T> decode)
    {
        try
        {
            return decode();
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or KeyMaterialException
            or KeyVersionNotPermittedException or InvalidKeyVersionException or CryptographicException
            or ArgumentException or InvalidOperationException)
        {
            throw new EbicsOrderDataException("The order data could not be decoded, decrypted or validated.", ex);
        }
    }
}
