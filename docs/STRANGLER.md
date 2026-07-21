# Strangler Fig migration: extracting `GET /api/items/{id}`

The Strangler Fig pattern replaces a monolith incrementally: a facade sits in front
of it, and one route at a time is peeled off to a new service while everything else
keeps flowing to the monolith. The old system is *strangled* — shrunk route by
route — instead of rewritten in a big bang. This repo does exactly one extraction,
end to end, with the migration visible in a distributed trace.

## What moved

| Route | Before | After |
|-------|--------|-------|
| `GET /api/items/{id}` | monolith | **InventoryService** (new) |
| `GET /api/items` | monolith | monolith |
| `POST /api/orders` | monolith | monolith |

One route out. That restraint is the point — Strangler Fig is a sequence of small,
reversible moves, and this repo demonstrates the *mechanism* on a single route
rather than diluting it across many.

## The facade owns the contract, not the monolith

The routing authority is [`openapi/facade.yaml`](../openapi/facade.yaml) — an
OpenAPI 3 description of the **public** API surface. This matters:

- The client codes against the contract, not against "the monolith." When
  `GET /api/items/{id}` moves to a new service, the client sees no change: same
  path, same response shape (the InventoryService DTO is deliberately shaped like
  the monolith's, plus one *additive* backward-compatible field, `lowStock`).
- The facade's job is to make the contract true regardless of which service backs
  each path. Backend topology is an implementation detail hidden behind the
  contract — which is what lets it change without a client release.

## Routing-rule design

YARP config in
[`facade/LagerMeister.Facade/appsettings.json`](../facade/LagerMeister.Facade/appsettings.json):

```
/api/items/{id}   (Order 1)  ──► cluster: inventory   (http://inventory:8080)
/api/{**catch-all}(Order 2)  ──► cluster: monolith    (http://monolith:8080)
```

The specific route (`/api/items/{id}`) is ordered ahead of the catch-all, so the
extracted path wins while every other `/api/*` path — including the *list*
`/api/items`, which was **not** extracted — falls through to the monolith. Adding
the next migrated route is a two-line config change, no code.

## The trace proves the migration

Both routes enter through the same facade; the trace shows where each one lands.
From Tempo:

**`GET /api/items/{id}` — strangled onto InventoryService:**

```
(lagermeister-frontend)  ui.item-detail
  (lagermeister-frontend)  HTTP GET
    (lagermeister-facade)    GET /api/items/{id}
      (lagermeister-facade)    proxy.forwarder
        (lagermeister-facade)    GET
          (lagermeister-inventory) GET /api/items/{id:int}
            (lagermeister-inventory) inventory.lookup
              (lagermeister-inventory) <db>   ← 2 SQL spans
```

**`POST /api/orders` — still the monolith:**

```
(lagermeister-frontend)  ui.place-order
  (lagermeister-frontend)  HTTP POST
    (lagermeister-facade)    POST /api/{**catch-all}
      (lagermeister-facade)    proxy.forwarder
        (lagermeister-facade)    POST
          (lagermeister-monolith)  POST api/orders
            (lagermeister-monolith)  order.validate
            (lagermeister-monolith)  stock.reserve
            (lagermeister-monolith)  <db>   ← 3 SQL spans
```

You can *see* the migration: item-detail now traverses **facade → inventory**;
orders still traverse **facade → monolith**. This nesting only works because the
facade re-propagates trace context — YARP's `Yarp.ReverseProxy` ActivitySource is
registered in the tracer, so the forward span (`proxy.forwarder`) carries the
context injected into the downstream request. Without that, the downstream span
would attach to the browser instead of nesting under the facade, and the trace
would not show the hop.

### The migration also *fixed* a defect

The monolith's item-detail has an intentional **N+1**: it loads the movements,
then queries each movement's location separately (`1 + 1 + N` SQL spans — ~24 for
an item with 22 movements). The rewrite fetches movements + locations in a single
`LEFT JOIN` — **2 SQL spans, flat, regardless of movement count**
([`EfInventoryRepository`](../src/LagerMeister.Inventory/Infrastructure/EfInventoryRepository.cs)).
The trace makes the win measurable, not just asserted: the DB span count on that
route drops from ~24 to 2. Strangling a route is an opportunity to modernize it,
not just relocate it.

## Data: shared store, for now

Both services read the same PostgreSQL tables; InventoryService maps them through
its **own** domain model (value objects, an aggregate) via a persistence-separated
EF adapter — an anti-corruption boundary, so the legacy schema never leaks into the
new model. Sharing the store is the honest *intermediate* state of a real strangle:
you move the **routing** first (cheap, reversible) and split the **data** later
(a separate migration with its own dual-write/backfill risk). Giving InventoryService
its own schema is the documented next step, deliberately out of scope for this one
extraction.

## Rollback story: a route flip, not a deploy

Because the facade owns routing, reverting the migration is a **config change**, not
a code deploy:

```jsonc
// facade/LagerMeister.Facade/appsettings.json — point the route back at the monolith
"inventory-item-detail": { "ClusterId": "monolith", "Match": { "Path": "/api/items/{id}" } }
```

Flip the `ClusterId`, and `GET /api/items/{id}` is served by the monolith again —
no rebuild of any service, no client change. In production this is a config push
(or a feature-flagged YARP config provider) with a rollback measured in seconds.
That reversibility is the safety property that makes incremental extraction
tolerable in a system you cannot afford to break.
