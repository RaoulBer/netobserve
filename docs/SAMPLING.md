# Sampling strategy: head vs. tail, with the cost trade-offs

This document works the sampling problem end to end: it picks a concrete workload,
derives every cost claim from it, and lands on a recommendation with the math shown.

All Datadog prices below were **retrieved 2026-07-21** from
<https://www.datadoghq.com/pricing/>; rates change, so verify at the source before
quoting a number in a contract. The *method* is provider-independent — only the
per-unit rates are Datadog-specific.

## 1. Baseline arithmetic

Assume one service under steady load:

| Quantity | Value |
|----------|-------|
| Request rate | 200 req/s |
| Spans per trace | 12 |
| Bytes per span (ingest) | 1.5 KB |

Everything else derives from these three numbers:

```
spans/s      = 200 × 12                     = 2,400 spans/s
spans/day    = 2,400 × 86,400               = 207.4 million spans/day
spans/month  = 207.4M × 30                  = 6.22 billion spans/month

bytes/s      = 2,400 × 1.5 KB               = 3.6 MB/s
GB/day       = 3.6 MB/s × 86,400            = 311 GB/day
GB/month     = 311 × 30                     = 9.33 TB/month   (raw ingest at 100%)

requests/day = 200 × 86,400                 = 17.28 million req/day
```

**9.33 TB/month and 6.22 billion spans/month at 100% retention.** That is the number
every strategy below is trying to bring down without going blind.

## 2. Head sampling

Decision made **before** the trace exists, at the SDK, via `TraceIdRatioBased`:

```csharp
// SDK-level, in the app: keep 1% of traces, decided at root-span creation.
.SetSampler(new TraceIdRatioBasedSampler(0.01))
```

**Properties**

- **Cheapest possible.** Dropped spans are never created, never serialized, never
  transported. You save CPU and network on the emitting side, not just backend $.
- **Decision is blind.** The 1% keep/drop is chosen before the request's outcome is
  known — you cannot say "keep this one *because* it errored," because the error
  hasn't happened yet.
- **Rare events vanish proportionally.** This is the failure mode. Take an error
  class occurring 1-in-10⁷ requests:

  ```
  unsampled:  17.28M req/day ÷ 10⁷      = 1.73 occurrences/day
  at 1% head: 1.73 × 0.01               = 0.017 occurrences/day
                                        ≈ 1 captured every ~58 days
  ```

  The error is happening ~1.7 times a day and your traces show it roughly twice a
  quarter. Head sampling makes rare-but-important failures **statistically
  invisible** exactly when you most need the trace.

Head sampling is a *volume* instrument, not a *fidelity* instrument.

## 3. Tail sampling

Decision made **after** the trace completes, at the Collector, via the
`tailsamplingprocessor`. This repo defines the policy in
[`otel/collector-config.yaml`](../otel/collector-config.yaml) (present as the
documented option; the demo pipeline keeps 100%):

```yaml
tail_sampling:
  decision_wait: 5s
  num_traces: 50000
  policies:
    - name: keep-errors        # status_code == ERROR  -> keep 100%
      type: status_code
      status_code: { status_codes: [ERROR] }
    - name: keep-slow          # latency > 1s          -> keep 100%
      type: latency
      latency: { threshold_ms: 1000 }
    - name: baseline-probabilistic  # everything else   -> keep 5%
      type: probabilistic
      probabilistic: { sampling_percentage: 5 }
```

**What it buys:** you keep **100% of errors and latency outliers** — the traces you
actually reach for — while sampling the boring majority down to 5%.

**What it costs:**

- **No emit-side saving.** Every span is still created, serialized, and shipped to
  the Collector (the drop happens *at* the Collector). Tail sampling reduces the
  **backend** bill, not the application or the app→Collector network.
- **Collector memory.** Spans are buffered until their trace is decided:

  ```
  in-flight traces = traces/s × decision_wait = 200 × 5s      = 1,000 traces
  buffer           = 1,000 × 12 spans × 1.5 KB                ≈ 18 MB
  ```

  ~18 MB at baseline, and it grows linearly with both traffic and `decision_wait`.
  Set `num_traces` (here 50,000) to bound worst case.
- **The Collector becomes stateful.** All spans of a trace must reach the *same*
  Collector instance for the tail decision to see the whole trace. Horizontal
  scaling therefore requires a trace-ID-aware `loadbalancingexporter` tier in front
  of the sampling Collectors. That is real operational complexity head sampling
  never incurs.

## 4. Cost model (Datadog, retrieved 2026-07-21)

Rates: APM ingested spans **$0.10/GB** (first 150 GB/host/month included); APM
indexed/retained spans **$1.70 per million spans/month** (15-day, annual). Indexing
is the dominant, allotment-light cost driver — so the strategy that shrinks
*retained* spans wins.

Applying each strategy to the §1 baseline (6.22 B spans, 9.33 TB/month):

| Strategy | Spans → Datadog | GB ingested | Ingest $/mo | Index $/mo | **Total $/mo** | Rare errors kept? |
|----------|-----------------|-------------|-------------|------------|----------------|-------------------|
| **100% (no sampling)** | 6.22 B | 9,331 | $933 | $10,575 | **≈ $11,500** | yes (but you pay for everything) |
| **Head 1%** | 62 M | 93 | ~$9 | $106 | **≈ $115** | **no** — lost proportionally |
| **Tail hybrid** (100% err + 100% p99 + 5% rest ≈ 6.2%) | 386 M | 578 | $58 | $656 | **≈ $714** (+ Collector compute) | **yes** — 100% of them |

Reading the table:

- **100% → head-1%** cuts the bill ~100× but blinds you to rare failures.
- **Tail hybrid** costs ~6× a naive head-1% ($714 vs $115) yet keeps **every** error
  and latency outlier — you pay for signal, not noise. Still ~16× cheaper than 100%.
- The tail row hides an off-bill cost: the stateful Collector tier (compute +
  the trace-ID-aware load balancer). At this scale that is a small EC2 line item;
  at 10× it is a design problem (see §6).

(Per-host allotments — 150 GB ingest and a small included indexed-span quota per APM
host — zero out the low-volume ingest rows and trim the tail row; they do **not**
meaningfully offset the 100% indexing cost, which is the point.)

## 5. Cardinality — a metrics problem, not a span problem

Sampling controls *trace volume*. Cardinality controls *metric cost*, and it is a
different, sharper cliff.

**The distinction.** A **span attribute** is stored once per span — attaching
`app.order.customer_ref` or `app.order.id` to a span costs one string on one span.
A **metric label** multiplies the metric's time-series count by the number of
distinct label values. A counter tagged with `customer_ref` becomes one time series
*per customer*. That is the anti-pattern:

```
# WRONG — customer_id as a metric label
orders_total{customer_id="C-8842"}   ← one new time series per customer, forever
```

**The cost mechanism (Datadog).** Custom metrics are billed by the count of unique
tag combinations (distinct time series). A high-cardinality label like `customer_id`,
`order_id`, or `email` turns one metric into millions of series — and Datadog custom
metrics are billed per 100 series. High-cardinality labels are the classic surprise
five-figure line item.

**The decision rule used in this repo:** high-cardinality, per-request identifiers go
on **spans**, never on **metrics**. See
[`Observability/Diagnostics.cs`](../src/LagerMeister.Monolith/Observability/Diagnostics.cs):
`app.order.customer_ref` and `app.order.id` are set as span *tags* (unbounded, fine —
you find one trace by its id), while the metric instruments carry only bounded labels
(`http.route`, `http.response.status_code`). The `app.*` attribute keys carry an
explicit comment: *allowed on spans, forbidden on metric labels.*

**Semantic conventions are the guardrail.** Using OTel's `http.*` / `db.*` conventions
(rather than ad-hoc keys) keeps metric dimensions on a known, bounded set — `http.route`
is the *templated* path (`/api/items/{id}`), not the raw URL (`/api/items/8842`), so
route metrics stay low-cardinality by construction. Convention discipline is cardinality
discipline.

## 6. Recommendation

**Hybrid: light head sampling as a volume ceiling, tail policies for fidelity.**

- A modest **head** rate (e.g. keep 50–100%, drop only if raw emit volume threatens
  the app or the Collector ingress) as a cheap safety valve on span *creation*.
- **Tail** policies as the primary filter: 100% errors, 100% latency outliers, ~5%
  baseline. This is the row that keeps signal while cutting the bill ~16× vs 100%.
- **Cardinality** governed separately and always: identifiers on spans, bounded
  semantic-convention labels on metrics.

Net for the baseline: **≈ $714/mo** with full error/outlier fidelity, versus
**$11,500/mo** at 100% or a **blind** $115/mo at head-1%.

**What changes at 10× traffic (2,000 req/s, 24,000 spans/s):**

- Tail-buffer memory scales linearly: ~180 MB per Collector at `decision_wait: 5s`.
  Shorten the window or shard earlier.
- A single Collector no longer holds a trace's spans reliably — you now **need** the
  trace-ID-aware `loadbalancingexporter` tier so every span of a trace lands on the
  same sampling Collector. The stateful design becomes mandatory, not optional.
- The 5% baseline may need to drop to 1–2% to hold the bill flat, but the error and
  latency policies stay at 100% — you scale down the noise, never the signal.
- Reconsider the head ceiling: at 10× you may introduce real head sampling (e.g.
  20–50%) purely to protect app CPU and Collector ingress, accepting the rare-event
  blindness on the *head-dropped* fraction while the tail policies still guarantee
  errors among what survives.
