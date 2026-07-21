# dotnet-legacy-to-observable

**End-to-end OpenTelemetry instrumentation and Strangler Fig modernization of a
legacy .NET system, fanned out to a self-hosted Grafana stack through one Collector.**

A deliberately legacy ASP.NET Core 8 warehouse monolith (NLog, EF Core, PostgreSQL,
an intentional N+1) is instrumented end-to-end with OpenTelemetry — bridging its
existing NLog logging into OTLP with trace correlation — so a **single distributed
trace spans a JavaScript frontend → C# backend → the database**. One OpenTelemetry
Collector routes traces/logs/metrics to Grafana **Tempo / Loki / Prometheus** (with
Datadog as a documented fan-out point), and one endpoint is strangled out of the
monolith through a **YARP facade** governed by an **OpenAPI 3 contract**. The whole
system comes up from a clean checkout with `docker compose up`.

> Scenario is synthetic; the engineering decisions are real. See
> [DESIGN.md](DESIGN.md) for scope and non-goals.

## Architecture

```
  Browser (OTel-Web, trace root)                         ┌── Grafana LGTM ──┐
      │  fetch + W3C traceparent                         │  Tempo   (traces)│
      ▼                                                  │  Loki    (logs)  │
  ┌─────────────┐   /api/items/{id}   ┌───────────────┐  │  Prometheus(mtx) │
  │ YARP Facade │────────────────────►│ InventorySvc  │  │  Grafana (UI)    │
  │ (Strangler) │                     │ (new, DDD)    │  └────────▲─────────┘
  │  :8080      │   everything else   ├───────────────┤           │ scrape/OTLP
  │             │────────────────────►│ Monolith      │           │
  └─────────────┘                     │ (legacy,NLog) │   ┌────────┴─────────┐
        │  every service exports          └──────┬────┘   │  OTel Collector  │
        └── OTLP ──────────────────── OTLP ───────┼───────►│ one hub, N sinks │
                                                  ▼        └────────┬─────────┘
                                             PostgreSQL             └─► (Datadog:
                                                                        documented)
```

**One instrumentation, one Collector, N backends.** Application code knows only OTLP;
which backend the telemetry lands in is a Collector-config concern, never app code.
No vendor SDK lock-in in the services.

## What it proves (verified against the live stack)

**A single trace, browser → backend → database** (from Tempo):

```
(lagermeister-frontend)  ui.place-order         ← browser root span
  (lagermeister-frontend)  HTTP POST
    (lagermeister-facade)    POST /api/{**catch-all}
      (lagermeister-facade)    proxy.forwarder
        (lagermeister-monolith)  POST api/orders
          (lagermeister-monolith)  order.validate    ← custom span
          (lagermeister-monolith)  stock.reserve      ← custom span
          (lagermeister-monolith)  <postgres> ×3      ← Npgsql SQL spans
```

**NLog → OTel bridge with correlation** — the monolith's own NLog line and its Loki
`LogRecord` share the request's `TraceId`, and that id resolves to the trace above:

```
...|INFO|trace=7c5a0fc3...|span=47dd28b3...|LagerMeister.Monolith.Ordering.OrderService|Order 2 reserved...
```

**Strangler Fig migration, visible in the trace** — `GET /api/items/{id}` now routes
`facade → InventoryService` (not the monolith), and the rewrite drops the item-detail
SQL span count from ~24 (the monolith's N+1) to **2**. See [docs/STRANGLER.md](docs/STRANGLER.md).

## Quickstart

```bash
docker compose up --build          # brings up the whole system from a clean checkout
```

Then:

| URL | What |
|-----|------|
| <http://localhost:8081> | Frontend console — click the buttons to generate traces |
| <http://localhost:3000> | Grafana (opens as admin, no login) |
| <http://localhost:8080> | Public API via the YARP facade |
| <http://localhost:9090> | Prometheus |
| <http://localhost:8082> · <http://localhost:8083> | Monolith · InventoryService — direct, for debugging/comparison |

Generate some traffic — open the frontend and click, or:

```bash
curl -s localhost:8080/api/items/1 >/dev/null                      # → facade → InventoryService
curl -s -XPOST localhost:8080/api/orders -H 'Content-Type: application/json' \
     -d '{"customerRef":"DEMO","lines":[{"sku":"WIDGET-001","quantity":1}]}'   # → facade → monolith
```

**See the trace.** Grafana → **Explore** → **Tempo** → **Search** → Service Name
`lagermeister-frontend` → open a `ui.item-detail` / `ui.place-order` trace for the
`browser → facade → service → PostgreSQL` waterfall. Click a backend span →
**Logs for this span** to jump to the correlated Loki logs (same `trace_id`). The
dashboard is under **Dashboards → LagerMeister — Service Overview**.

**See the strangler routing.** The extracted service returns a `lowStock` field the
monolith never did, so you can tell which service answered:

```bash
curl -s localhost:8080/api/items/1 | jq .lowStock   # via facade → InventoryService: true/false
curl -s localhost:8082/api/items/1 | jq .lowStock   # direct monolith :8082: null (old shape)
```

## Tests

Unit tests (pure domain logic) run in a container — no local .NET SDK needed:

```bash
scripts/dn test LagerMeister.sln     # 25 tests: order-admission rules + DDD domain
```

`scripts/dn` runs the .NET 8 SDK in Docker against this workspace (see the file header).

## Repository layout

```
src/LagerMeister.Monolith/    legacy app — NLog, EF Core, OTel + NLog bridge
src/LagerMeister.Inventory/   extracted service — DDD (value objects, aggregate), N+1 fixed
src/frontend/                 vanilla HTML + fetch, OpenTelemetry-Web trace root
facade/LagerMeister.Facade/   YARP Strangler Fig facade
openapi/facade.yaml           the routing authority (OpenAPI 3)
otel/                         Collector config (+ glibc rehost Dockerfile)
grafana/                      Tempo / Loki / Prometheus config + provisioned datasources & dashboard
tests/                        xUnit domain tests
docs/                         NLOG-BRIDGE · STRANGLER · SAMPLING
```

## Key design decisions

- **Vendor-neutral by construction.** The Datadog exporter is a commented fan-out
  point in [otel/collector-config.yaml](otel/collector-config.yaml), not a code
  dependency — the trade-off (Datadog's own .NET tracer offers deeper runtime
  profiling; the Collector route preserves neutrality) is deliberate. This build
  ships the OSS stack; Datadog wiring is a config line away.
- **Dual-write NLog bridge**, not a big-bang replacement — the low-risk path for a
  system with an existing logging investment. The *new* service skips NLog entirely.
  [docs/NLOG-BRIDGE.md](docs/NLOG-BRIDGE.md)
- **Head-vs-tail sampling** with worked cost arithmetic and a dated Datadog price
  model. [docs/SAMPLING.md](docs/SAMPLING.md)
- **Strangler by routing first, data later** — the facade owns the OpenAPI contract;
  rollback is a config flip, no deploy. [docs/STRANGLER.md](docs/STRANGLER.md)

## Docs

- [DESIGN.md](DESIGN.md) — scope, non-goals, architecture
- [docs/NLOG-BRIDGE.md](docs/NLOG-BRIDGE.md) — bridging NLog into OpenTelemetry
- [docs/STRANGLER.md](docs/STRANGLER.md) — the migration, proven in traces
- [docs/SAMPLING.md](docs/SAMPLING.md) — head vs. tail, cost, cardinality
