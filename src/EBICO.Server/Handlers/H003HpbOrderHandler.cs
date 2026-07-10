using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H003 (EBICS 2.4) HPB handler. Like H004 it is a <b>pure-key</b> procedure: the bank's
/// authentication and encryption keys are returned as <c>RSAKeyValue</c> (modulus/exponent) elements
/// inside the <c>HPBResponseOrderData</c>.
/// </summary>
public sealed class H003HpbOrderHandler : HpbOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    /// <param name="bankKeyStore">The bank key store.</param>
    public H003HpbOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
        : base(masterData, keyStore, bankKeyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override HpbRequestData ExtractHpbRequest(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsNoPubKeyDigestsRequest
            ?? throw new InvalidDataException("The HPB request is not an H003 ebicsNoPubKeyDigestsRequest.");
        var header = request.Header?.Static;
        return new HpbRequestData(header?.HostId, header?.PartnerId, header?.UserId);
    }

    /// <inheritdoc />
    protected override byte[] SerializeBankPubKeyOrderData(BankKeyPair bankKeys, string hostId)
        => EbicsXmlSerializer.SerializeOrderData(new H.HpbResponseOrderDataType
        {
            AuthenticationPubKeyInfo = new H.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = bankKeys.AuthenticationVersion.Value,
                PubKeyValue = new H.PubKeyValueType { RsaKeyValue = ToRsaKeyValue(bankKeys.Authentication) },
            },
            EncryptionPubKeyInfo = new H.EncryptionPubKeyInfoType
            {
                EncryptionVersion = bankKeys.EncryptionVersion.Value,
                PubKeyValue = new H.PubKeyValueType { RsaKeyValue = ToRsaKeyValue(bankKeys.Encryption) },
            },
            HostId = hostId,
        });
}
