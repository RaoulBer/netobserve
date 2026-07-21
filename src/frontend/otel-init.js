// Browser OpenTelemetry setup — makes the browser the ROOT of the distributed trace.
// A manual UI span is started for each user action; fetch auto-instrumentation adds a
// child span AND injects the W3C `traceparent` header into the cross-origin API call,
// so the backend span (and its DB spans) continue the very same trace.
import { WebTracerProvider } from '@opentelemetry/sdk-trace-web';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-base';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { ZoneContextManager } from '@opentelemetry/context-zone';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';
import { trace, context } from '@opentelemetry/api';

// Config is injected by index.html before this bundle runs; defaults suit the
// docker-compose localhost setup.
const COLLECTOR_URL = window.OTEL_COLLECTOR_URL || 'http://localhost:4318/v1/traces';
const API_BASE = window.API_BASE || 'http://localhost:8080';

const provider = new WebTracerProvider({
  resource: resourceFromAttributes({ [ATTR_SERVICE_NAME]: 'lagermeister-frontend' }),
  // Short flush delay so browser spans reach the Collector quickly (snappier demo).
  spanProcessors: [new BatchSpanProcessor(
    new OTLPTraceExporter({ url: COLLECTOR_URL }),
    { scheduledDelayMillis: 2000 },
  )],
});

// Default propagator is W3C Trace Context (traceparent) — exactly what the backend reads.
provider.register({ contextManager: new ZoneContextManager() });

registerInstrumentations({
  instrumentations: [
    new FetchInstrumentation({
      // Without this, cross-origin fetches do NOT get the traceparent header. Match the
      // API base so the header is propagated to the backend (and only there).
      propagateTraceHeaderCorsUrls: [new RegExp(API_BASE.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))],
      clearTimingResources: true,
    }),
  ],
});

const tracer = trace.getTracer('lagermeister-frontend-ui');

// Public helper used by index.html. Wraps a UI action in a root browser span so the
// whole trace tree hangs off a browser operation, not off the fetch span.
window.LagerMeister = {
  apiBase: API_BASE,
  action(name, fn) {
    const span = tracer.startSpan(name);
    const ctx = trace.setSpan(context.active(), span);
    return context.with(ctx, async () => {
      try {
        return await fn();
      } catch (err) {
        span.recordException(err);
        span.setStatus({ code: 2 /* ERROR */, message: String(err) });
        throw err;
      } finally {
        span.end();
      }
    });
  },
};
