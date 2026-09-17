#!/bin/sh
# Bind-mounted volumes arrive owned by whatever created them on the host (usually
# root), which overrides the image's build-time `chown app:app /data`. If we're
# root, take ownership of the data dir, then drop to the non-root "app" user
# before exec'ing the app. If we're already non-root (compose `user:` override),
# just run as-is and rely on the host dir being writable.
set -e

# TORRENTRULER_GIT_SHA / _BRANCH: an explicit --build-arg (baked in as an image ENV, see
# Dockerfile) or a `docker run -e` / compose `environment:` override always wins. Otherwise
# fall back to the values auto-derived from .git during the Docker build.
if [ -f /build-info.env ]; then
    if [ -z "$TORRENTRULER_GIT_SHA" ] || [ -z "$TORRENTRULER_GIT_BRANCH" ]; then
        set -a
        . /build-info.env
        set +a
    fi
fi

DATA_DIR="${TORRENTRULER_DATA_DIR:-/data}"
LOG_DIR="${TORRENTRULER_LOG_DIR:-/log}"
mkdir -p "$DATA_DIR" "$LOG_DIR"

if [ "$(id -u)" = "0" ]; then
    chown -R app:app "$DATA_DIR" "$LOG_DIR"
    exec su-exec app "$@"
fi

exec "$@"
