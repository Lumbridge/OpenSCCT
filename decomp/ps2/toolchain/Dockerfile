FROM debian:12-slim@sha256:734a72b3d6142a71993b4bc580ca01d8ca687f77a2c422d96a961d3376b179bc

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl git python3-venv \
        gcc-mipsel-linux-gnu=4:12.2.0-4 gcc-12-mipsel-linux-gnu=12.2.0-14cross5 \
        binutils-mipsel-linux-gnu=2.40-2cross2 && rm -rf /var/lib/apt/lists/*
ENV PATH="/opt/tools/bin:${PATH}"
COPY requirements.txt /tmp/requirements.txt
RUN python3 -m venv /opt/tools && pip install --no-cache-dir -r /tmp/requirements.txt

ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) asset=x86_64; sha=e823ae932a56c68dcd86b68d12f2268c953e0c027402162c8a6dfa6a00d65008 ;; \
      arm64) asset=aarch64; sha=0e630457e2f33a43da3723e195ecd0b39b683a6a83122955fdc68c5b17183cbc ;; \
      *) exit 1 ;; \
    esac && curl --fail --location --retry 3 "https://github.com/encounter/objdiff/releases/download/v3.8.3/objdiff-cli-linux-$asset" -o /opt/tools/bin/objdiff-cli \
    && echo "$sha  /opt/tools/bin/objdiff-cli" | sha256sum -c - \
    && chmod +x /opt/tools/bin/objdiff-cli
WORKDIR /work
