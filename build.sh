#!/usr/bin/env bash
# Kept as the documented entry point; the build itself lives in the Makefile.
set -euo pipefail
exec make -C "$(cd "$(dirname "$0")" && pwd)" all
