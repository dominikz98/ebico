# Key/certificate fixtures

Test key material is **generated in-process** instead of being checked in — there are
no real or proprietary keys in the repo. Provided via the helper
[`TestCertificates`](../../Infrastructure/TestCertificates.cs):

```csharp
using var cert = TestCertificates.CreateSelfSigned("CN=EBICO Test");  // self-signed X.509 + private key
using var rsa  = TestCertificates.CreateRsaKey();                     // fresh RSA key pair
```

That covers the crypto/onboarding tests from M2/M3 onwards (A00x/E002/X002, INI/HIA/HPB,
X.509 verification).

## Reproducible test vectors (later)

As soon as reproducible vectors are needed (fixed hashes/signatures across
several runs), **fixed**, self-generated PEM keys can be placed here as
fixtures. Those are neither secret nor proprietary and
may be committed — unlike the EBICS sample XML (see
`../Xml/README.md`).
