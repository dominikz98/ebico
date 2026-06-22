namespace EBICO.Core.Crypto;

/// <summary>
/// The role an EBICS key pair plays. Each purpose corresponds to a key-version letter
/// carried on the wire: signature keys are <c>A00x</c>, encryption keys <c>E00x</c>,
/// authentication keys <c>X00x</c>.
/// </summary>
public enum KeyPurpose
{
    /// <summary>
    /// Bank-technical signature key (version letter <c>A</c>: A004/A005/A006). Used to
    /// authorise orders.
    /// </summary>
    /// <remarks>
    /// The version letter <c>A</c> is unrelated to <c>SignatureClass.A</c> (the
    /// "Erstunterschrift" authorisation level) — they share a letter but mean different things.
    /// </remarks>
    Signature,

    /// <summary>Encryption key (version letter <c>E</c>: E001/E002). Protects transaction keys / order data.</summary>
    Encryption,

    /// <summary>Authentication/identification key (version letter <c>X</c>: X001/X002). Authenticates requests.</summary>
    Authentication,
}

/// <summary>
/// Maps a <see cref="KeyPurpose"/> to and from its EBICS key-version letter
/// (<c>A</c>/<c>E</c>/<c>X</c>).
/// </summary>
public static class KeyPurposeExtensions
{
    /// <summary>Returns the EBICS key-version letter for <paramref name="purpose"/>.</summary>
    /// <param name="purpose">The key purpose.</param>
    /// <returns><c>'A'</c> for signature, <c>'E'</c> for encryption, <c>'X'</c> for authentication.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    public static char Letter(this KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => 'A',
        KeyPurpose.Encryption => 'E',
        KeyPurpose.Authentication => 'X',
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown key purpose."),
    };

    /// <summary>Maps an EBICS key-version letter to its <see cref="KeyPurpose"/>.</summary>
    /// <param name="letter">The version letter (<c>'A'</c>, <c>'E'</c> or <c>'X'</c>).</param>
    /// <param name="purpose">The resolved purpose when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="letter"/> is <c>'A'</c>, <c>'E'</c> or <c>'X'</c>.</returns>
    public static bool TryFromLetter(char letter, out KeyPurpose purpose)
    {
        switch (letter)
        {
            case 'A':
                purpose = KeyPurpose.Signature;
                return true;
            case 'E':
                purpose = KeyPurpose.Encryption;
                return true;
            case 'X':
                purpose = KeyPurpose.Authentication;
                return true;
            default:
                purpose = default;
                return false;
        }
    }
}
