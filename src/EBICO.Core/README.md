# EBICO.Core

The **shared EBICS primitives** of [EBICO](https://github.com/dominikz98/ebico): schema bindings
and serialisation, crypto (RSA key material, signing/encryption, fingerprints), the
BTF/order model, domain value objects (HostID/PartnerID/UserID …) and the return-code catalogue.
Supported protocol versions: **H003, H004, H005**.

`EBICO.Core` is the common foundation of **`EBICO.Connector`** (the client) and the
**EBICO server emulator**. As a rule it is referenced **transitively** via `EBICO.Connector`;
a direct reference is only worthwhile if you use the primitives on their own.

## Installation

```bash
dotnet add package EBICO.Core
```

## Documentation

See the [doc index](https://github.com/dominikz98/ebico/blob/main/docs/index.md), in particular the
*Protocol & schemas* section.

## License

MIT — see [LICENSE](https://github.com/dominikz98/ebico/blob/main/LICENSE). The EBICS schemas/specs
themselves are the proprietary property of the EBICS SC and are not part of this package.
