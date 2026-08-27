# Nakshatra — Architecture & System Design

Nakshatra is an online shopping platform built as a set of small .NET microservices behind a
single API gateway, with a React single-page portal as the only user-facing client. Services
communicate synchronously over HTTP for reads and asynchronously over Kafka for domain events.

- [1. Goals and constraints](#1-goals-and-constraints)
- [2. System context](#2-system-context)
- [3. Container / service map](#3-container--service-map)
- [4. Service catalog](#4-service-catalog)
- [5. Event-driven design](#5-event-driven-design)
- [6. Key flows](#6-key-flows)
- [7. Data design](#7-data-design)
- [8. Cross-cutting concerns](#8-cross-cutting-concerns)
- [9. Deployment topology](#9-deployment-topology)
- [10. Design decisions and trade-offs](#10-design-decisions-and-trade-offs)

---

## 1. Goals and constraints

| Goal | How it is met |
|------|---------------|
| Independent deployability | Each service is its own ASP.NET Core minimal-API process, built into its own image from the shared `backend/Dockerfile`. |
| One entry point for the portal | `Nakshatra.Gateway` (YARP reverse proxy) exposes every `/api/*` route; the portal never calls a service directly. |
| Loose coupling on the write path | State changes are published as Kafka events; downstream services react without the producer knowing about them. |
| Runs anywhere, including a laptop | Mongo, Redis and Kafka are optional — when unconfigured, `ServiceDefaults` substitutes in-process implementations so every service still starts and serves traffic. |
| Horizontal scalability | Services are stateless; all state lives in MongoDB, Redis, Kafka or the media volume, so replicas can be added or removed freely. |

## 2. System context

```mermaid
flowchart LR
    Customer([Customer])
    Vendor([Vendor])
    Admin([Admin])
    Agent([Fulfillment agent])

    subgraph Nakshatra
        Portal[React portal<br/>Vite + TypeScript]
        Gateway[API gateway<br/>YARP]
        Services[14 domain microservices]
    end

    Mongo[(MongoDB<br/>documents)]
    Redis[(Redis<br/>cache)]
    Kafka[(Kafka<br/>event log)]
    Files[(Media volume<br/>originals + renditions)]
    PSP[[Payment gateway<br/>simulated]]

    Customer --> Portal
    Vendor --> Portal
    Admin --> Portal
    Agent --> Portal
    Portal -->|/api/*| Gateway
    Gateway --> Services
    Services --> Mongo
    Services --> Redis
    Services <--> Kafka
    Services --> Files
    Services --> PSP
```

The four personas (`Customer`, `Vendor`, `Admin`, `FulfillmentAgent`) are modelled in
`Nakshatra.Shared.Models.Persona` and drive the portal's navigation and route guards.

## 3. Container / service map

```mermaid
flowchart TB
    Portal[Portal<br/>nginx :8080]
    GW[Gateway :5000]
    Portal --> GW

    subgraph Identity[Identity]
        USER[User :5001]
        VENDOR[Vendor :5002]
    end

    subgraph Discovery[Browse and discovery]
        CATALOG[Catalog :5003]
        SEARCH[Search :5004]
        REC[Recommendation :5006]
        MEDIA[Media :5011]
    end

    subgraph Buying[Buying]
        CART[Cart :5005]
        ORDER[Order :5007]
        PAY[Payment :5009]
        BILL[Billing :5010]
    end

    subgraph Supply[Supply and delivery]
        INV[Inventory :5012]
        FUL[Fulfillment :5008]
        SC[SupplyChain :5013]
    end

    NOTIF[Notification :5014]

    GW --> USER
    GW --> VENDOR
    GW --> CATALOG
    GW --> SEARCH
    GW --> REC
    GW --> MEDIA
    GW --> CART
    GW --> ORDER
    GW --> PAY
    GW --> BILL
    GW --> INV
    GW --> FUL
    GW --> SC
    GW --> NOTIF

    SEARCH -->|HTTP| CATALOG
    CART -->|HTTP| CATALOG
    ORDER -->|HTTP| CATALOG
    REC -->|HTTP| CATALOG

    ORDER -. publish/subscribe .-> KAFKA[(Kafka)]
    PAY -. publish .-> KAFKA
    INV -. publish/subscribe .-> KAFKA
    MEDIA -. publish/subscribe .-> KAFKA
    SC -. publish/subscribe .-> KAFKA
    KAFKA -. subscribe .-> FUL
    KAFKA -. subscribe .-> BILL
    KAFKA -. subscribe .-> REC
    KAFKA -. subscribe .-> NOTIF
```

Ports shown are the local development ports (`launchSettings.json`, and the gateway's
`ReverseProxy` cluster addresses). In Docker Compose and Kubernetes every service listens on
`8080` and is addressed by DNS name.

## 4. Service catalog

| Service | Responsibility | Key endpoints | Publishes | Consumes |
|---------|----------------|---------------|-----------|----------|
| **Gateway** | Single ingress, CORS, `/api/*` routing to the owning service | `/health`, `/api/**` | — | — |
| **User** | Users and personas | `/api/users`, `/api/personas`, `/api/users/by-persona/{persona}` | — | — |
| **Vendor** | Vendor master data | `/api/vendors` | — | — |
| **Catalog** | Product master data, stock reservation, reviews | `/api/products`, `/api/products/{id}/reserve`, `/api/products/categories` | — | — |
| **Search** | Keyword search, filtering, paging and type-ahead over catalog data | `/api/search`, `/api/search/suggest` | — | — |
| **Cart** | Per-user cart lines | `/api/cart/{userId}` and item add/update/remove | `cart.item-added` | — |
| **Recommendation** | Co-purchase graph, "also purchased", basket recommendations | `/api/recommendations/also-purchased/{productId}`, `/api/recommendations/basket` | — | `orders.created` |
| **Order** | Order creation, pricing (subtotal + tax), status transitions, cancellation | `/api/orders`, `/api/orders/{id}/status`, `/api/orders/{id}/cancel` | `orders.created`, `orders.status-changed` | `payments.completed` |
| **Payment** | Authorization and refunds through a simulated payment gateway | `/api/payments`, `/api/payments/{id}/refund` | `payments.completed` | — |
| **Billing** | Invoices derived from orders and payments | `/api/billing/invoices` | — | `orders.created`, `payments.completed` |
| **Fulfillment** | Shipments and their stage transitions | `/api/fulfillment/shipments`, `.../advance` | — | `orders.created` |
| **Inventory** | Stock levels, adjustments, replenishment, demand forecasting, reorder suggestions | `/api/inventory`, `.../adjust`, `.../replenish`, `.../forecast`, `/api/inventory/reorder-suggestions` | `inventory.adjusted`, `inventory.low`, `inventory.replenishment-ordered` | `orders.created` |
| **SupplyChain** | End-to-end traceability of orders and replenishments | `/api/supplychain/traces`, `/api/supplychain/stages` | `supplychain.event-recorded` | `orders.created`, `orders.status-changed`, `inventory.replenishment-ordered` |
| **Media** | Uploads, sandboxed storage, background transcoding into renditions | `/api/media`, `/api/media/{id}/renditions/{rendition}` | `media.uploaded`, `media.transcoded` | `media.uploaded` |
| **Notification** | Per-user notification inbox fed by domain events | `/api/notifications`, `.../read`, `.../read-all` | — | `orders.created`, `orders.status-changed`, `payments.completed`, `supplychain.event-recorded`, `inventory.low`, `inventory.replenishment-ordered`, `media.transcoded` |

## 5. Event-driven design

Topic names are centralized in `Nakshatra.Shared.Messaging.Topics`. Every message is wrapped in an
`EventEnvelope(Topic, Key, Payload)` where the key is the aggregate id (order id, product id, …),
so Kafka partitioning preserves per-aggregate ordering.

```mermaid
flowchart LR
    ORDER[Order] -->|orders.created| FUL[Fulfillment]
    ORDER -->|orders.created| BILL[Billing]
    ORDER -->|orders.created| INV[Inventory]
    ORDER -->|orders.created| REC[Recommendation]
    ORDER -->|orders.created| SC[SupplyChain]
    ORDER -->|orders.created| NOTIF[Notification]
    ORDER -->|orders.status-changed| SC
    ORDER -->|orders.status-changed| NOTIF

    PAY[Payment] -->|payments.completed| ORDER
    PAY -->|payments.completed| BILL
    PAY -->|payments.completed| NOTIF

    INV -->|inventory.low| NOTIF
    INV -->|inventory.replenishment-ordered| SC
    INV -->|inventory.replenishment-ordered| NOTIF

    SC -->|supplychain.event-recorded| NOTIF

    MEDIA[Media] -->|media.uploaded| MEDIA
    MEDIA -->|media.transcoded| NOTIF
```

Consumers derive from `EventConsumerBackgroundService`, which subscribes on startup and keeps
processing for the lifetime of the host. Each consumer uses its own consumer group, so adding a
new subscriber never affects existing ones. `inventory.adjusted` and `cart.item-added` are
published as integration points for future consumers and analytics; nothing subscribes to them
today.

## 6. Key flows

### 6.1 Browse and add to cart

```mermaid
sequenceDiagram
    participant P as Portal
    participant G as Gateway
    participant S as Search
    participant C as Catalog
    participant R as Redis
    participant Ca as Cart
    participant K as Kafka

    P->>G: GET /api/search?q=lamp
    G->>S: GET /api/search
    S->>C: GET /api/products
    C->>R: read-through cache
    C-->>S: products
    S-->>P: ranked, paged results
    P->>Ca: POST /api/cart/{userId}/items
    Ca->>C: GET /api/products/{id}
    Ca->>K: publish cart.item-added
    Ca-->>P: updated cart
```

### 6.2 Checkout: order → payment → fulfillment

```mermaid
sequenceDiagram
    participant P as Portal
    participant O as Order
    participant C as Catalog
    participant K as Kafka
    participant Pay as Payment
    participant F as Fulfillment
    participant B as Billing
    participant I as Inventory
    participant N as Notification

    P->>O: POST /api/orders
    O->>C: validate and price each line
    O->>O: subtotal + tax = total
    O->>K: orders.created
    K-->>F: create shipment
    K-->>B: draft invoice
    K-->>I: decrement stock (may emit inventory.low)
    K-->>N: "order placed" notification

    P->>Pay: POST /api/payments
    Pay->>Pay: authorize (simulated PSP)
    Pay->>K: payments.completed
    K-->>O: mark order paid, emit orders.status-changed
    K-->>B: settle invoice
    K-->>N: "payment received" notification
```

### 6.3 Media upload and transcoding

```mermaid
sequenceDiagram
    participant P as Portal
    participant M as Media
    participant D as Media volume
    participant K as Kafka
    participant W as TranscodingWorker
    participant N as Notification

    P->>M: POST /api/media (multipart)
    M->>D: store original under a sandboxed path
    M->>K: media.uploaded
    K-->>W: transcode to renditions
    W->>D: write renditions
    W->>K: media.transcoded
    K-->>N: "media ready" notification
```

## 7. Data design

- **MongoDB** is the system of record. Every service owns its own collections; no service reads
  another service's collection — cross-service reads go through HTTP clients
  (`CatalogClient`, `OrderClient`, `RecommendationClient`) or through events.
- **Redis** is a read-through cache in front of hot document reads
  (`MapCachedCrud<T>` in `Nakshatra.Shared.Endpoints`), with explicit invalidation on write.
- **Kafka** is the durable event log and the integration backbone.
- **A filesystem volume** holds media originals and renditions; `MediaStorage` resolves paths and
  rejects anything that escapes the configured root (path-traversal protection).
- Shared domain models live in `Nakshatra.Shared.Models.Domain`, so producers and consumers agree
  on the wire shape of events.

```mermaid
erDiagram
    USER ||--o{ CART : owns
    USER ||--o{ ORDER : places
    USER ||--o{ NOTIFICATION : receives
    VENDOR ||--o{ PRODUCT : sells
    PRODUCT ||--o{ CART_ITEM : "added as"
    PRODUCT ||--o| INVENTORY_ITEM : "tracked by"
    PRODUCT ||--o{ REVIEW : has
    REVIEW ||--o{ MEDIA_ASSET : attaches
    ORDER ||--|{ CART_ITEM : contains
    ORDER ||--o| PAYMENT : "paid by"
    ORDER ||--o| INVOICE : "billed as"
    ORDER ||--o| SHIPMENT : "shipped as"
    ORDER ||--o{ SUPPLY_CHAIN_EVENT : "traced by"
```

## 8. Cross-cutting concerns

These are applied uniformly through `ServiceDefaults.AddNakshatraInfrastructure` and
`UseNakshatraDefaults`:

| Concern | Implementation |
|---------|----------------|
| Configuration | `appsettings.json` plus environment variables (`Mongo__…`, `Redis__…`, `Kafka__…`) |
| Graceful degradation | Missing Mongo/Redis/Kafka configuration falls back to in-memory repository, cache and event bus |
| Health | `/health` on every service, used by Compose healthchecks and Kubernetes probes |
| Rate limiting | Fixed-window limiter per client IP, per replica (`RateLimit:PermitsPerMinute`, default 600, rejecting with HTTP 429) |
| CORS | A single `nakshatra-portal` policy driven by `Cors:AllowedOrigins` |
| Serialization | Web-style camelCase JSON with string enum converters |
| Compression | Response compression enabled by default |
| Portal hardening | nginx sets `X-Content-Type-Options`, `X-Frame-Options` and `Referrer-Policy`; hashed assets are cached immutably |

## 9. Deployment topology

```mermaid
flowchart TB
    subgraph K8s[Kubernetes namespace: nakshatra]
        ING[Ingress] --> PORTALSVC[portal Service]
        ING --> GWSVC[gateway Service]
        GWSVC --> DEPLOYS[14 service Deployments<br/>each with a ClusterIP Service]
        CM[[ConfigMap: nakshatra-config]] -.-> DEPLOYS
        SEC[[Secret: placeholder]] -.-> DEPLOYS
        DEPLOYS --> MONGO[(mongo)]
        DEPLOYS --> REDIS[(redis)]
        DEPLOYS --> KAFKA[(kafka)]
    end
```

- **Local**: `docker compose up --build` starts Mongo, Redis, Kafka, all services and the portal.
  The gateway is published on `localhost:5000` and the portal on `localhost:8080`.
- **Kubernetes**: manifests live in `infra/k8s/` — namespace, ConfigMap and Secret in
  `00-namespace-and-config.yaml`, one manifest per service, portal and ingress in
  `90-portal-and-ingress.yaml`. Discovery is DNS-based, and every routing/backing-store address
  comes from the ConfigMap so images stay environment-agnostic.
- **Images**: one parameterized `backend/Dockerfile` (`SERVICE` and `INSTALL_FFMPEG` build args)
  for every .NET service; `frontend.Dockerfile` builds the portal and serves it from nginx.

## 10. Design decisions and trade-offs

| Decision | Rationale | Trade-off |
|----------|-----------|-----------|
| Gateway owns all `/api/*` routing | The portal has one origin and one CORS policy, and services can move without client changes | The gateway is a shared dependency that must be scaled and monitored carefully |
| Events for write-side integration | Order placement does not block on fulfillment, billing, inventory or notifications | Downstream state is eventually consistent |
| HTTP for cross-service reads | Simple, and avoids duplicating product data into search, cart and order | The read path depends on Catalog availability, mitigated by Redis caching |
| Database per service, no shared collections | Preserves service autonomy and independent schema evolution | Cross-domain queries must be composed by the caller |
| In-memory fallbacks for Mongo/Redis/Kafka | Fast local development and cheap integration tests without infrastructure | Fallback behaviour is single-process only and must never be used in production |
| Minimal APIs plus a shared `ServiceDefaults` | Little boilerplate and consistent cross-cutting behaviour across 15 processes | The shared library is a coupling point that must stay thin and stable |
| Filesystem media storage with sandboxed paths | Keeps the stack runnable anywhere | Object storage would be required for multi-replica production media |
