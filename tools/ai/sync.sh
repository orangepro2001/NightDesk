#!/usr/bin/env bash
# Keep a NightDesk checkout exactly on origin/main.
#
# Refuses to run on a dirty tree: the AI host is a build machine, and an
# unexplained local modification there is almost always a stale experiment that
# would silently end up in a release package.
set -euo pipefail

repo="${NIGHTDESK_REPO_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$repo"

if dirty="$(git status --porcelain)" && [[ -n "$dirty" ]]; then
    printf 'REFUSING: %s has local changes\n%s\n' "$repo" "$dirty" >&2
    exit 1
fi

git fetch --prune origin
git pull --ff-only
printf 'checkout: %s\n' "$repo"
git status --short --branch
git log -1 --format='%h %ci %s'
