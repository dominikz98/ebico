using System.Text;
using AwesomeAssertions;
using EBICO.Core.Payments;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Core.Payments;

/// <summary>
/// Tests for the structural/semantic <see cref="SepaPaymentValidator"/> (issue #39): a valid
/// <c>pain.001</c>/<c>pain.008</c> passes and yields the message identifiers; the negative cases cover a
/// wrong message family, a missing group header field, the <c>NbOfTxs</c>/<c>CtrlSum</c> cross-checks, a
/// missing payment-information block, malformed XML and an unsupported order type.
/// </summary>
public class SepaPaymentValidatorTests
{
    [Theory]
    [InlineData(PaymentOrderTypes.CreditTransfer)]
    [InlineData(PaymentOrderTypes.InstantCreditTransfer)]
    public void Validate_ValidCreditTransfer_ReturnsValidWithIdentifiers(string orderType)
    {
        var xml = PainSamples.CreditTransfer([100.00m, 50.00m], messageId: "MSG-1", messageVersion: "pain.001.001.09");

        var result = SepaPaymentValidator.Validate(orderType, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.MessageId.Should().Be("MSG-1");
        result.MessageNameId.Should().Be("pain.001.001.09");
    }

    [Theory]
    [InlineData(PaymentOrderTypes.DirectDebitCore)]
    [InlineData(PaymentOrderTypes.DirectDebitB2B)]
    public void Validate_ValidDirectDebit_ReturnsValidWithIdentifiers(string orderType)
    {
        var xml = PainSamples.DirectDebit([25.50m], messageId: "MSG-DD", messageVersion: "pain.008.001.02");

        var result = SepaPaymentValidator.Validate(orderType, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeTrue();
        result.MessageId.Should().Be("MSG-DD");
        result.MessageNameId.Should().Be("pain.008.001.02");
    }

    [Fact]
    public void Validate_WrongMessageFamily_ReturnsInvalid()
    {
        // A pain.008 payload validated as a credit transfer (expects pain.001).
        var xml = PainSamples.DirectDebit([10.00m]);

        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_MissingMessageId_ReturnsInvalid()
    {
        var xml = PainSamples.CreditTransfer([100.00m], messageId: "MSG-1")
            .Replace("<MsgId>MSG-1</MsgId>", string.Empty, StringComparison.Ordinal);

        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MsgId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NbOfTxsMismatch_ReturnsInvalid()
    {
        var xml = PainSamples.CreditTransfer([100.00m, 50.00m]) // NbOfTxs = 2
            .Replace("<NbOfTxs>2</NbOfTxs>", "<NbOfTxs>5</NbOfTxs>", StringComparison.Ordinal);

        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NbOfTxs", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CtrlSumMismatch_ReturnsInvalid()
    {
        var xml = PainSamples.CreditTransfer([100.00m, 50.00m]) // CtrlSum = 150.00
            .Replace("<CtrlSum>150.00</CtrlSum>", "<CtrlSum>999.00</CtrlSum>", StringComparison.Ordinal);

        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("CtrlSum", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NoPaymentInformation_ReturnsInvalid()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.001.001.09">
              <CstmrCdtTrfInitn>
                <GrpHdr>
                  <MsgId>MSG-1</MsgId>
                  <CreDtTm>2026-07-14T10:00:00</CreDtTm>
                  <NbOfTxs>0</NbOfTxs>
                </GrpHdr>
              </CstmrCdtTrfInitn>
            </Document>
            """;

        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("PmtInf", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MalformedXml_ReturnsInvalid()
    {
        var result = SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, Encoding.UTF8.GetBytes("<Document>"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_UnsupportedOrderType_ReturnsInvalid()
    {
        var xml = PainSamples.CreditTransfer([100.00m]);

        var result = SepaPaymentValidator.Validate("XXX", Encoding.UTF8.GetBytes(xml));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NullPayload_Throws()
    {
        var act = () => SepaPaymentValidator.Validate(PaymentOrderTypes.CreditTransfer, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
