# 0005 — Connector: custom dispatch instead of MediatR

- Status: accepted
- Date: 2026-06-21

## Context

`EBICO.Connector` follows a mediator pattern: the caller knows only
`IEbicsClient.Send(request)` and receives a typed `EbicsResult<T>`. For the
request → handler mapping and the pipeline (validation → serialisation → crypto →
transport → …) there is a choice between a ready-made library (e.g. MediatR) and a
custom dispatch.

## Decision

**Custom dispatch** instead of the MediatR library.

Rationale and pipeline details: [../connector/architecture.md](../connector/architecture.md).

## Consequences

- Full control over the EBICS-specific pipeline order (crypto before transport,
  download segment loop) and the version dependency.
- **No third-party dependency** in the published NuGet package — a lean dependency
  list is a real argument for a public connector.
- Trade-off: some dispatch boilerplate that MediatR would save.
- `EbicsResult<T>` instead of exceptions for **business** return codes; genuine
  transport/crypto errors may still throw.

## Alternatives

- **MediatR:** saves boilerplate, but brings coupling to the library and less
  control over the pipeline — rejected. (MediatR is also commercially licensed by
  now, which reinforces the lean-dependency rationale.)
