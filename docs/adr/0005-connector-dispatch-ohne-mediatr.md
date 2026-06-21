# 0005 — Connector: eigener Dispatch statt MediatR

- Status: accepted
- Datum: 2026-06-21

## Kontext

`EBICO.Connector` folgt einem Mediator-Muster: der Aufrufer kennt nur
`IEbicsClient.Send(request)` und bekommt ein typisiertes `EbicsResult<T>`. Für die
Zuordnung Request → Handler und die Pipeline (Validierung → Serialisierung →
Krypto → Transport → …) gibt es die Wahl zwischen einer fertigen Library (z. B.
MediatR) und einem eigenen Dispatch.

## Entscheidung

**Eigener Dispatch** statt MediatR-Library.

Begründung und Pipeline-Details: [../connector/architecture.md](../connector/architecture.md).

## Konsequenzen

- Volle Kontrolle über die EBICS-spezifische Pipeline-Reihenfolge (Krypto vor
  Transport, Download-Segmentschleife) und die Versionsabhängigkeit.
- **Keine Fremd-Dependency** im veröffentlichten NuGet-Paket — eine schlanke
  Abhängigkeitsliste ist bei einem öffentlichen Connector ein echtes Argument.
- Trade-off: etwas Dispatch-Boilerplate, die MediatR sparen würde.
- `EbicsResult<T>` statt Exceptions für **fachliche** Returncodes; echte
  Transport-/Krypto-Fehler dürfen weiterhin werfen.

## Alternativen

- **MediatR:** spart Boilerplate, bringt aber Kopplung an die Library und weniger
  Kontrolle über die Pipeline — verworfen. (MediatR ist zudem inzwischen
  kommerziell lizenziert, was die schlanke-Dependency-Begründung verstärkt.)
