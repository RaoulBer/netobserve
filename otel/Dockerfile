# Rehost the OTel Collector (contrib) binary on a slim glibc base.
#
# WHY: the upstream otel/opentelemetry-collector-contrib:0.116.0 image is FROM scratch
# and ships a *dynamically linked* binary (interpreter /lib64/ld-linux-x86-64.so.2) with
# no glibc loader in the image. On this host's rootless/overlayfs Docker it fails to exec
# ("exec /otelcol-contrib: no such file or directory"). Copying the identical binary onto
# debian-slim gives it the loader it needs — same Collector, runnable everywhere.
FROM otel/opentelemetry-collector-contrib:0.116.0 AS upstream

FROM debian:bookworm-slim
COPY --from=upstream /otelcol-contrib /otelcol-contrib
COPY --from=upstream /etc/otelcol-contrib/config.yaml /etc/otelcol-contrib/config.yaml
COPY --from=upstream /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt
EXPOSE 4317 4318 8888 8889
ENTRYPOINT ["/otelcol-contrib"]
CMD ["--config=/etc/otelcol-contrib/config.yaml"]
