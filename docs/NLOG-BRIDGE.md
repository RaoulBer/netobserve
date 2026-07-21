# NLog → OpenTelemetry bridge

One requirement dominates observability work on established .NET systems: *bridging
existing NLog logging into OpenTelemetry with trace correlation.* This is how it is
done here, and why it is done this way.

## The constraint

A production system already logs through **NLog** — file targets feeding log
shippers, console targets read by operators, layouts other tools parse. That
investment is not disposable. The task is not "replace NLog with OTel"; it is
"make NLog logs correlate with distributed traces **and** flow to OTLP backends,
without a risky big-bang cutover."

## Two bridging strategies

### 1. Replace targets (big-bang)

Rip out NLog, route `ILogger<T>` straight to the OpenTelemetry logging provider,
delete `nlog.config`. Every log becomes an OTLP `LogRecord`.

- **Pro:** one pipeline, no duplication.
- **Con:** every downstream consumer of the NLog *file* output (log shippers,
  grep-based runbooks, SIEM ingest rules, on-call muscle memory) breaks on the
  day of the switch. The blast radius is the entire logging surface, and you find
  out what depended on the old format in production.

### 2. Dual-write (the low-risk path — used here)

Keep NLog exactly as it is, and add the OpenTelemetry logging provider **alongside**
it. Application code logs **once** through `ILogger<T>`; the log fans out to both:

```
                       ┌── NLog provider ──► file + console targets
ILogger<T>.LogInformation(...) ─┤            (with TraceId in the layout)
                       └── OpenTelemetry provider ──► OTLP ──► Loki
```

- **Pro:** the legacy file/console output is byte-for-byte unchanged; OTLP export
  is added non-destructively; you can cut consumers over to Loki at their own
  pace and delete NLog targets only once nothing reads them. Reversible at any
  step by removing one provider.
- **Con:** logs are serialized twice (cheap) and briefly live in two systems
  during migration (intended).

For any system with an existing logging investment, dual-write is the
professional default. Replacement is only cheap on a greenfield service — which
is exactly why the **new** InventoryService in this repo skips NLog entirely and
is OTel-native from line one. Different lifecycle stage, different right answer.

## How it is wired here

Both halves are set up in
[`src/LagerMeister.Monolith/Program.cs`](../src/LagerMeister.Monolith/Program.cs)
and
[`Observability/ObservabilityExtensions.cs`](../src/LagerMeister.Monolith/Observability/ObservabilityExtensions.cs):

```csharp
builder.Logging.ClearProviders();
builder.Host.UseNLog();                 // provider 1: file + console
builder.AddLagerMeisterObservability(); // provider 2: OpenTelemetry -> OTLP -> Loki
```

### Half 1 — OTLP export (structured, backend-bound)

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter();
});
```

OTel `LogRecord`s automatically carry `TraceId`/`SpanId` from `Activity.Current`,
so a log emitted inside a request is stamped with that request's trace context
with **no code change at the call site**. In Loki these arrive as structured
metadata (`trace_id`, `span_id`), which is what makes the trace↔log jump in
Grafana work in both directions.

### Half 2 — TraceId in the NLog layout (legacy targets stay correlated)

The file/console targets never touch OTLP, so they need the trace id rendered
into the line itself. That is the `${activity}` layout renderer, provided by the
**`NLog.DiagnosticSource`** package (NLog core has no such renderer — the field
renders empty without it, which is a real gotcha this repo hit and fixed):

```xml
<extensions>
  <add assembly="NLog.DiagnosticSource" />
</extensions>
...
layout="${longdate}|${level:uppercase=true}|trace=${activity:property=TraceId}|span=${activity:property=SpanId}|${logger}|${message}"
```

Result — the same request produces a correlated line in the legacy file target
**and** a correlated `LogRecord` in Loki:

```
2026-07-21 ...|INFO|trace=7c5a0fc3aacb60707233af4bd79528cb|span=47dd28b3a34e3ad9|LagerMeister.Monolith.Ordering.OrderService|Order 2 reserved for BRIDGE-2 (1 lines)
```

That `trace=7c5a0fc3...` value is a real trace queryable in Tempo — grep the file,
paste the id into Grafana, land on the waterfall.

## Fallback if the renderer fights you

`NLog.DiagnosticSource` must match the NLog major version (5.x package for NLog 5,
6.x for NLog 6 — mismatch is a silent empty field). If a version conflict can't be
resolved on a given system, the documented fallback is: keep the OTLP provider for
structured correlation and drop the `${activity}` renderer from the file layout,
accepting that only the OTLP path is trace-correlated. That is a smaller loss than
fighting package versions in a release window — the trace correlation that matters
most (structured, in the backend) is on the provider, not the layout.
