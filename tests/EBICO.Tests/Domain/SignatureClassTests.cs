using AwesomeAssertions;
using EBICO.Core.Domain;

namespace EBICO.Tests.Domain;

/// <summary>
/// Tests for <see cref="SignatureClass"/> classification: the EBICS distinction between a
/// transport signature (T) and a bank-technical / authorising signature (E/A/B), issue #16.
/// </summary>
public class SignatureClassTests
{
    [Theory]
    [InlineData(SignatureClass.T, true)]
    [InlineData(SignatureClass.E, false)]
    [InlineData(SignatureClass.A, false)]
    [InlineData(SignatureClass.B, false)]
    public void IsTransportOnly_TrueOnlyForT(SignatureClass signatureClass, bool expected)
    {
        signatureClass.IsTransportOnly().Should().Be(expected);
    }

    [Theory]
    [InlineData(SignatureClass.E, true)]
    [InlineData(SignatureClass.A, true)]
    [InlineData(SignatureClass.B, true)]
    [InlineData(SignatureClass.T, false)]
    public void IsBankTechnical_TrueForEab_FalseForT(SignatureClass signatureClass, bool expected)
    {
        signatureClass.IsBankTechnical().Should().Be(expected);
    }

    [Fact]
    public void TransportAndBankTechnical_PartitionAllDefinedValues()
    {
        foreach (var signatureClass in Enum.GetValues<SignatureClass>())
        {
            signatureClass.IsTransportOnly().Should().Be(!signatureClass.IsBankTechnical());
        }
    }
}
