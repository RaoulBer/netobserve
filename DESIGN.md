# DESIGN — dotnet-legacy-to-observable

A reference implementation: end-to-end OpenTelemetry instrumentation and Strangler
Fig modernization of a legacy .NET system, exported to a self-hosted Grafana stack
(with Datadog as a documented fan-out point).

## 1. Purpose

Demonstrates, in one reproducible `docker compose` setup, a set of production
observability patterns for .NET:

1. OpenTelemetry instrumentation of a .NET system end-to-end, including bridging
   existing **NLog** logging into OpenTelemetry with trace correlation.
2. A **single distributed trace** spanning a JavaScript frontend → C# backend → database.
3. Operating a **Grafana stack** (Tempo, Loki, Prometheus, Grafana) against the
   telemetry pipeline. Datadog (APM, dashboards, monitors) and Azure Monitor are
   additional exporters — a Collector-config concern, documented rather than wired
   here (see §8).
4. A written **head-vs-tail sampling strategy** with cost trade-offs, cardinality
   analysis, and semantic-convention discipline.
5. A **Strangler Fig migration** of one endpoint out of the monolith, routed through
   a facade against an **OpenAPI 3 contract**.

The scenario is synthetic; the engineering decisions are real. It is intentionally
*not* a product.

## 2. Non-goals (scope guardrails)

Explicitly out of scope — the guardrails that keep the implementation focused:

- No authentication/authorization.
- No polished frontend. Plain HTML + vanilla JS `fetch` is sufficient.
- No CI/CD beyond a single smoke check (`docker compose up` + health probe). No test pyramid.
- No Kubernetes. `docker compose` only.
- No more than **one** migrated endpoint in the Strangler Fig step.
- One overview Grafana dashboard; alerting is demonstrated via Datadog monitors
  (documented), not Grafana rules.
- No performance benchmarking.

**Definition of done:**

- `docker compose up` brings up the full system from a clean checkout.
- One trace visible in Grafana Tempo showing frontend span → backend spans → SQL
  span, with correlated NLog log lines in Loki carrying the same `TraceId`.
- `docs/SAMPLING.md` is complete (§7).
- `docs/STRANGLER.md` documents the facade routing decision against the OpenAPI contract.

## 3. Scenario

**"LagerMeister"** — a fictional warehouse-inventory monolith, built deliberately in
legacy style:

- ASP.NET Core (net8.0) MVC-style monolith, ~3 endpoints:
  - `GET /api/items` — list inventory items
  - `GET /api/items/{id}` — item detail (includes an intentional N+1 query for interesting traces)
  - `POST /api/orders` — create an order (validate → reserve stock → persist; a
    multi-span trace with a deterministic insufficient-stock failure for error-trace demos)
- Logging exclusively via **NLog** with `nlog.config` (file + console targets) — the
  existing logging investment the bridge has to preserve.
- PostgreSQL via EF Core (Npgsql), no read-model separation, direct DbContext usage.
- No tracing, no metrics, no structured correlation at project start.

A minimal **JS frontend** (single `index.html`, vanilla `fetch`) calls the API,
instrumented with `@opentelemetry/sdk-trace-web` + W3C Trace Context propagation so
the browser span is the trace root.

## 4. Target architecture

```
  Browser (OTel-Web, trace root)                         ┌── Grafana LGTM ──┐
      │  fetch + W3C traceparent                         │  Tempo   (traces)│
      ▼                                                  │  Loki    (logs)  │
  ┌─────────────┐   /api/items/{id}   ┌───────────────┐  │  Prometheus(mtx) │
  │ YARP Facade │────────────────────►│ InventorySvc  │  │  Grafana (UI)    │
  │ (Strangler) │                     │ (new, DDD)    │  └────────▲─────────┘
  │             │   everything else   ├───────────────┤           │ scrape/OTLP
  │             │────────────────────►│ Monolith      │           │
  └─────────────┘                     │ (legacy,NLog) │   ┌────────┴─────────┐
        │  every service exports          └──────┬────┘   │  OTel Collector  │
        └── OTLP ──────────────────── OTLP ───────┼───────►│ one hub, N sinks │
                                                  ▼        └────────┬─────────┘
                                             PostgreSQL             └─► (Datadog:
                                                                        documented)
```

**One instrumentation, one Collector, N backends.** Application code knows only OTLP;
backend routing (Grafana stack vs. Datadog vs. Azure Monitor) is a Collector
configuration concern. The design argument: no vendor SDK lock-in in application code.

## 5. Components & deliverables

### 5.1 Monolith instrumentation

- OpenTelemetry SDK for .NET: `AddAspNetCoreInstrumentation`,
  `AddHttpClientInstrumentation`, `AddNpgsql` — traces and metrics.
- **NLog bridge:** `NLog.Extensions.Logging` routed into `Microsoft.Extensions.Logging`,
  exported via the OTel Logs API, **and** `TraceId`/`SpanId` injected into every NLog
  layout (`${activity}`) so legacy file/console targets remain correlated. Two
  strategies (replace targets vs. dual-write) documented in `docs/NLOG-BRIDGE.md`,
  with dual-write as the low-risk path.
- One custom `ActivitySource` with manual spans in the order flow (`order.validate`,
  `stock.reserve`) carrying semantic-convention-compliant attributes.
- Attributes reviewed against **OTel semantic conventions** (`http.*`, `db.*`, plus an
  `app.*` namespace for domain attributes) — cardinality-safe by design (identifiers on
  spans, never as metric labels; see `docs/SAMPLING.md`).

### 5.2 Frontend trace root

- `index.html` + `@opentelemetry/sdk-trace-web`, `fetch` auto-instrumentation, W3C
  `traceparent` propagation to the backend. The browser span is the trace root.

### 5.3 Collector + Grafana stack

- `otel-collector` (contrib) with pipelines: `traces → [tempo]`, `logs → [loki]`,
  `metrics → [prometheus]`; a tail-sampling pipeline defined as the documented option.
- Grafana LGTM via docker compose (provisioned datasources + one overview dashboard
  under `grafana/provisioning/`).

### 5.4 Datadog integration (documented)

- Datadog exporter added at the **Collector** (not the Datadog .NET tracer — the
  architectural point). Trade-off noted: Datadog's own tracer offers deeper .NET
  runtime profiling; the Collector route preserves vendor neutrality. Left as a
  commented fan-out point in the Collector config in this build.

### 5.5 Strangler Fig migration

- **YARP** reverse proxy as the facade in front of the monolith.
- `openapi/facade.yaml` — the OpenAPI 3 contract for the public API surface. The
  contract, not the monolith's implementation, is the routing authority.
- Extract `GET /api/items/{id}` into a new minimal service (`InventoryService`, net8.0),
  same OTel instrumentation, same Collector.
- `docs/STRANGLER.md` covers: why the facade owns the contract; routing-rule design;
  how the trace view proves the migration (facade span → new-service span); and the
  config-flip rollback story.

### 5.6 Sampling strategy document

See §7 / `docs/SAMPLING.md`.

## 6. Repository layout

```
├── README.md
├── DESIGN.md                     # this file
├── docker-compose.yml
├── src/
│   ├── LagerMeister.Monolith/    # legacy app, NLog, EF Core
│   ├── LagerMeister.Inventory/   # extracted service (DDD)
│   └── frontend/index.html
├── facade/                       # YARP facade
├── openapi/facade.yaml
├── otel/collector-config.yaml
├── grafana/provisioning/
└── docs/
    ├── NLOG-BRIDGE.md
    ├── STRANGLER.md
    └── SAMPLING.md
```

## 7. Sampling — required contents

`docs/SAMPLING.md` works the problem end to end: baseline arithmetic for a concrete
workload; head sampling (`TraceIdRatioBased`) with its blind-decision trade-off; tail
sampling (Collector `tailsamplingprocessor` policies, buffer-memory math, the stateful
scaling cost); a cost model against a real price sheet; a cardinality section (why it
is a metrics problem, the `user_id`-as-metric-label anti-pattern, the span-attribute vs.
metric-label rule, semantic conventions as the guard rail); and a hybrid recommendation
with 10× behavior.

## 8. Optional extensions

- **Application Insights as a third Collector exporter** (Azure Monitor exporter) —
  closes the third named stack; a Collector-config addition, not app code.
- eBPF-based auto-instrumentation (Datadog USM, Grafana Beyla) as a complement to SDK
  instrumentation — a note on kernel-level telemetry.
- A k6 load script to make the sampling arithmetic in `SAMPLING.md` empirically
  observable.
