# EBICO.Core

Die **geteilten EBICS-Primitives** von [EBICO](https://github.com/dominikz98/ebico): Schema-Bindings
und Serialisierung, Krypto (RSA-Schlüsselmaterial, Signatur/Verschlüsselung, Fingerprints), das
BTF-/Order-Modell, Domänen-Value-Objects (HostID/PartnerID/UserID …) und der Returncode-Katalog.
Unterstützte Protokoll­versionen: **H003, H004, H005**.

`EBICO.Core` ist das gemeinsame Fundament von **`EBICO.Connector`** (dem Client) und dem
**EBICO-Server-Emulator**. In der Regel wird es **transitiv** über `EBICO.Connector` referenziert;
ein direkter Verweis lohnt nur, wenn die Primitives eigenständig genutzt werden.

## Installation

```bash
dotnet add package EBICO.Core
```

## Dokumentation

Siehe den [Doku-Index](https://github.com/dominikz98/ebico/blob/main/docs/index.md), insbesondere die
Rubrik *Protokoll & Schemas*.

## Lizenz

MIT — siehe [LICENSE](https://github.com/dominikz98/ebico/blob/main/LICENSE). Die EBICS-Schemas/Specs
selbst sind proprietäres Eigentum der EBICS SC und nicht Teil dieses Pakets.
