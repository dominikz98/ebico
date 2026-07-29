# Server: Transaction recovery & timeouts

> Implementation of **Issue #35** (Milestone M4 — Server: Transaction Engine). This page
> describes how the transaction engine handles interrupted transactions: the **expiry
> (timeout) of transaction IDs**, the **eviction** of orphaned/completed transactions
> (lazy on access **and** via a background service) and the **idempotency** of repeated
> messages.
>
> Deliberately **included**: the sliding idle timeout per transaction (`LastActivityAt`/`Touch`/
> `IsExpired`), the lazy expiry in the transfer, the `TransactionCleanupService` (BackgroundService)
> together with `ITransactionEvictor`, the re-enqueue of the dequeued download data on expiry, the
> upper bound of concurrent transactions (`091115`) and the defined idempotency for duplicate
> segments/init/receipt.
> Deliberately **not yet**: an **active recovery-sync flow** (client recovery flag in the
> initialisation, server responds with a resume segment) — the exact EBICS recovery flow depends
> on the proprietary XSDs/annexes and must be verified against the spec; `061101`
> `EBICS_TX_RECOVERY_SYNC` is present in the [catalog](../protocol/return-codes.md), but is
> (still) **not** actively triggered.

## Purpose

An EBICS transaction spans **several** messages (initialisation → transfer …,
plus receipt on download). If a client aborts in between — lost connection, no receipt —,
state is left behind on the server: on upload the segment buffer and the transaction key, on
download the already **dequeued** order data "in progress". Until #35 this state was **never**
cleaned up (the upload engine never called `Remove`, the download engine only on receipt) — the in-memory
store grew unbounded (see [ADR-0013](../adr/0013-upload-transaktions-engine.md)/
[ADR-0014](../adr/0014-download-transaktions-engine.md)).

#35 gives every transaction a **limited lifetime** and evicts expired transactions.
The same retention is at the same time the **idempotency/replay window**: as long as a (even completed)
transaction has not expired, it remains recognisable, so that repeats are answered cleanly.

## Idle timeout (sliding window)

Besides `CreatedAt`, every transaction carries a `LastActivityAt` timestamp. It is set on creation to
`CreatedAt` and shifted to "now" on **every** accepted transfer step via `Touch(now)`
(lock-free via `Interlocked`). `IsExpired(now, timeout)` is `true` as soon as the transaction
has been **idle** for at least `timeout`:

```csharp
public bool IsExpired(DateTimeOffset now, TimeSpan timeout)
    => timeout > TimeSpan.Zero && now.UtcTicks - LastActivityAt.UtcTicks >= timeout.Ticks;
```

The window is **sliding** (activity, not creation time) — a long but running
multi-segment transfer does not expire mid-way. A `timeout` ≤ `TimeSpan.Zero` **disables** the
expiry entirely.

Configuration in [`EbicoServerOptions`](host.md):

| Option | Default | Effect |
| --- | --- | --- |
| `TransactionTimeout` | `1h` | idle timeout per transaction; `≤ 0` = disabled |
| `TransactionCleanupInterval` | `1min` | sweep interval of the background service; `≤ 0` = sweeper off |
| `MaxConcurrentTransactions` | `0` | upper bound of concurrent transactions per store; `0` = unbounded |

## Eviction: lazy + background sweeper

**Lazy (on access).** When a transfer finds the transaction, it is checked for expiry **before**
further processing. If it has expired, it is removed and answered like an unknown ID —
`091101` `EBICS_TX_UNKNOWN_TXID`. Otherwise `Touch(now)` is set and processing continues normally.

**Background sweeper.** `TransactionCleanupService` (`BackgroundService`) sweeps every
`TransactionCleanupInterval` over the registered `ITransactionEvictor` (both engines) and removes
expired transactions — including those the client **never touches again** (which the lazy expiry
would therefore never see). This bounds the memory regardless of client behaviour. The sweeper is robust:
with a disabled interval it starts no timer at all, and an error in a single sweep is logged
without tearing down the loop or the host.

```csharp
public interface ITransactionEvictor
{
    Task<int> EvictExpiredAsync(CancellationToken ct = default); // removes expired, returns count
}
```

Both engines implement `ITransactionEvictor`; the registration in `AddEbicoServer` additionally forwards the
existing engine singletons as `ITransactionEvictor` (the same instances) and adds
`AddHostedService<TransactionCleanupService>()`.

### Download: re-enqueue on expiry

A download dequeues the order data already in the initialisation (consumption semantics, see
[download transaction](download-transaction.md)). If the transaction expires (lazy or via sweeper),
the data is **re-enqueued** (`IDownloadDataProvider.EnqueueAsync`) — analogous to a negative
acknowledgement — so that it is not lost. The `Remove` return value serves as an **"exactly once" guard**
against the race between the lazy path ↔ sweeper: whoever actually removes the transaction re-enqueues; the
loser does nothing. This way the data lands back in the queue exactly once.

The **receipt** deliberately checks for **no** expiry: if the transaction is still present at receipt time,
the client has actually received and acknowledged the data — honouring the acknowledgement is more correct than
wrongly discarding it (and re-enqueuing the data on a positive acknowledgement). If the transaction has
already been evicted, the normal `TryGet` failure path applies → `091101`.

## Idempotency / duplicate segments

Retention makes repeats recognisable; the behaviour is therefore defined:

| Repeat | Response | Note |
| --- | --- | --- |
| duplicate **transfer segment** (upload, within retention) | `091103` `EBICS_TX_MESSAGE_REPLAY` | existing segment duplicate detection (#32) |
| **transfer** against an expired/removed transaction | `091101` `EBICS_TX_UNKNOWN_TXID` | retention window exceeded |
| repeated **initialisation** | new transaction (new random ID) | EBICS has no client idempotency key in the init; a download dequeues again in the process |
| repeated **receipt** after completion (download) | `091101` | the first acknowledgement removed the transaction |

## Concurrent-transaction bound (091115)

If `MaxConcurrentTransactions > 0`, an initialisation is rejected (`091115`
`EBICS_MAX_TRANSACTIONS_EXCEEDED`) as soon as the respective store reaches the bound — on download
**before** dequeuing the data (a rejected init must not consume any data). The check is a
**soft** limit (count-then-create is not atomic) and also counts completed, not-yet-evicted
transactions within the retention window. Deliberately accepted for the emulator.

## Return codes

**No** new return codes were needed — all are already in the
[catalog](../protocol/return-codes.md):

| Situation | Return code | Placement |
| --- | --- | --- |
| expired/removed `TransactionID` (transfer/receipt) | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| duplicate upload segment (replay) | `091103` EBICS_TX_MESSAGE_REPLAY | Body |
| too many concurrent transactions | `091115` EBICS_MAX_TRANSACTIONS_EXCEEDED | Body |
| (available, not triggered) recovery resync | `061101` EBICS_TX_RECOVERY_SYNC | Header |

### ⚠️ Spec caveats

- **No active recovery-sync flow.** State preservation (retention) is the prerequisite for recovery;
  a spec-exact client-driven recovery flow (recovery flag, resume segment number, `061101`)
  must be verified against the official EBICS annexes and is **deferred**.
- **Timeout value.** EBICS does not prescribe a fixed transaction timeout; the default (1 h) is
  emulator-pragmatic and configurable.
- **Receipt ignores expiry** deliberately (see above) — the exact bank policy for "receipt after timeout"
  must be verified against the spec.
- **Soft concurrent limit** (not atomic) and the retention counting are deliberate emulator trade-offs.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; time via the `MutableTimeProvider` from
`tests/EBICO.Tests/Connector/TestDoubles.cs`, which can advance the clock):

- `TransactionRecoveryTests` — end-to-end over the pipeline: upload/download transfer after timeout →
  `091101` (upload removed, download re-enqueued); sliding window (an active multi-segment transfer
  does not expire despite total duration > timeout); replay within retention (`091103`) vs. after expiry
  (`091101`); `MaxConcurrentTransactions` (`091115`); direct `EvictExpiredAsync` check (removes
  expired, keeps active; disabled = no-op; re-enqueue exactly once); receipt after timeout is
  honoured; repeated receipt → `091101`.
- `UploadTransactionStoreTests` / `DownloadTransactionStoreTests` — `GetAll()` snapshot (decoupled from
  a later `Remove`) and the object semantics `LastActivityAt`/`Touch`/`IsExpired` (incl. disabled
  timeout).
- `TransactionCleanupServiceTests` — disabled interval completes immediately; null guards.
- `EbicoServerServiceCollectionExtensionsTests` — default options, registration of the hosted service and
  both engines as `ITransactionEvictor` (the same instances).

## Related documentation

- [Upload transaction (initialisation + transfer)](upload-transaction.md) — the two-phase upload
- [Download transaction (initialisation + transfer + receipt)](download-transaction.md) — the three-phase download incl. consumption semantics
- [Hostable server skeleton](host.md) — pipeline, `EbicoServerOptions`, DI
- [EBICS return code catalog](../protocol/return-codes.md) — the transaction/segment codes used
- [ADR-0013](../adr/0013-upload-transaktions-engine.md) / [ADR-0014](../adr/0014-download-transaktions-engine.md) — the engines that #35 extends with eviction/TTL
