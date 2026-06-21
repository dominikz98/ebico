# Schlüssel-/Zertifikat-Fixtures

Test-Schlüsselmaterial wird **in-process generiert** statt eingecheckt — es gibt
keine echten oder proprietären Schlüssel im Repo. Bereitgestellt über den Helfer
[`TestCertificates`](../../Infrastructure/TestCertificates.cs):

```csharp
using var cert = TestCertificates.CreateSelfSigned("CN=EBICO Test");  // self-signed X.509 + Private Key
using var rsa  = TestCertificates.CreateRsaKey();                     // frisches RSA-Schlüsselpaar
```

Das deckt die Krypto-/Onboarding-Tests ab M2/M3 ab (A00x/E002/X002, INI/HIA/HPB,
X.509-Verifizierung).

## Reproduzierbare Testvektoren (später)

Sobald reproduzierbare Vektoren nötig sind (feste Hashes/Signaturen über
mehrere Läufe), können hier **fixe**, selbst erzeugte PEM-Schlüssel als
Fixtures abgelegt werden. Diese sind nicht geheim und nicht proprietär und
dürfen committet werden — im Gegensatz zu den EBICS-Beispiel-XML (siehe
`../Xml/README.md`).
