#!/usr/bin/env bash
# Identify what CI reads of a commit: a hash over its tree with the paths no
# CI job looks at removed.
#
# Usage: ci/code-tree.sh [<commit>]   → prints a 64-hex id   (default HEAD)
#
# CI memoises its expensive jobs on this id (refs/green-trees/<id> in the
# repo, see .github/workflows/ci.yml and docs/merge-queue.md): two commits
# with the same id have the same inputs, so a passing run on one vouches for
# the other. Leaving the no-op paths out means a docs-only commit on main
# does not invalidate every open PR's result. Git's own tree id would, since
# it covers every path.
#
# The id is over `git ls-tree -r` output (mode, type, blob id, path per
# entry), so it changes whenever any other file's content, mode, or path
# changes, and nothing else.
set -euo pipefail

# Paths no CI job reads, kept in ci/paths.py beside the models-only fast
# path's allow-list, which takes them too. Anything not matched counts as an
# input, including this file, ci/paths.py and the workflows, so the list can
# only ever be widened deliberately.
noop=$(python3 "$(dirname "$0")/paths.py" noop-regex)

# ls-tree lines are "<mode> <type> <blob>\t<path>"; anchor the pattern at
# the start of the path, after the tab.
git ls-tree -r --full-tree "${1:-HEAD}" \
  | grep -vE $'\t'"$noop" \
  | sha256sum | cut -d' ' -f1
