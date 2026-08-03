#!/usr/bin/env bash
#
# Cut a release: build, commit, tag, push, and open a DRAFT release on GitHub.
#
#   ./tools/publish.sh              # the whole thing, no questions asked
#   ./tools/publish.sh --dry-run    # build only — nothing committed, nothing pushed
#
# Running this IS the confirmation, so it does not stop to ask. What it opens on
# GitHub is a draft: the release only becomes visible when a human publishes it.
#
# Every check that could reject the release runs *before* the first side effect,
# so a run either does all of it or none of it. That includes the GitHub
# credentials — discovering they are missing after the push would leave a tag
# pushed with no release against it.
#
# GitHub access, in order of preference:
#   gh            the GitHub CLI, if installed and authenticated
#   GITHUB_TOKEN  a personal access token with `repo` scope, used over the REST API
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CSPROJ="src/InfiniteSplitter/InfiniteSplitter.csproj"
FACTORY="src/InfiniteSplitter/InfiniteSplitterFactory.cs"

DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,19p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

say() { printf '>> %s\n' "$*"; }
die() { printf '>> ERROR: %s\n' "$*" >&2; exit 1; }

git rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "not a git repository."

BRANCH_CONST=$(sed -n 's/.*private const string Branch = "\([^"]*\)".*/\1/p' "$FACTORY" | head -1)
BRANCH_NOW=$(git branch --show-current)
[ -n "$BRANCH_CONST" ] || die "could not read the Branch constant from $FACTORY."
[ "$BRANCH_CONST" = "$BRANCH_NOW" ] || die \
  "on branch '$BRANCH_NOW' but the update URL is compiled for '$BRANCH_CONST'. \
Release from '$BRANCH_CONST', or change the constant in $FACTORY and rebuild."

REPO=$(sed -n 's/.*private const string Repository = "\([^"]*\)".*/\1/p' "$FACTORY" | head -1)
[ -n "$REPO" ] || die "could not read the Repository constant from $FACTORY."

if ! git remote get-url origin >/dev/null 2>&1; then
  die "no 'origin' remote. Create the repository and wire it up first:
       gh repo create $REPO --public --source=. --remote=origin
   or  git remote add origin git@github.com:$REPO.git"
fi

if [ -n "$(git status --porcelain -- ':!Components')" ]; then
  git status --short -- ':!Components' >&2
  die "working tree has uncommitted changes outside Components/. Commit them first."
fi

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CSPROJ" | head -1)
[ -n "$VERSION" ] || die "could not read <Version> from $CSPROJ."
TAG="v$VERSION"

if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
  die "tag $TAG already exists. Bump <Version> in $CSPROJ and add a matching
       <update version=\"…\"> entry at the top of Components/Updates.xml."
fi

if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  GH_METHOD=gh
elif [ -n "${GITHUB_TOKEN:-}" ]; then
  GH_METHOD=token
else
  GH_METHOD=none
fi

if [ "$GH_METHOD" = none ] && [ "$DRY_RUN" != 1 ]; then
  die "no GitHub credentials, and the draft release is part of a release.
       Run 'gh auth login', or export GITHUB_TOKEN with 'repo' scope.
       ('$0 --dry-run' builds without needing either.)"
fi

say "releasing $TAG of $REPO from branch $BRANCH_NOW"

make all

ZIP="dist/InfiniteSplitter-$VERSION.zip"
[ -f "$ZIP" ] || die "$ZIP was not produced."

if [ "$DRY_RUN" = 1 ]; then
  echo
  say "--dry-run: built and packaged. Nothing committed, nothing pushed."
  echo "     would commit   $(git status --porcelain -- Components | wc -l) file(s) under Components/"
  echo "     would tag      $TAG"
  echo "     would push     $BRANCH_NOW and $TAG to origin"
  echo "     would create   a draft release on $REPO with $(basename "$ZIP")"
  [ "$GH_METHOD" = none ] &&
    say "note: no gh and no GITHUB_TOKEN — a real run would refuse to start."
  exit 0
fi

if [ -n "$(git status --porcelain -- Components)" ]; then
  git add Components
  git commit -q -m "release $TAG"
  say "committed Components/ as 'release $TAG'"
else
  say "Components/ unchanged since the last commit"
fi

git tag -a "$TAG" -m "InfiniteSplitter $VERSION"
git push -q origin "$BRANCH_NOW"
git push -q origin "$TAG"
say "pushed $BRANCH_NOW and $TAG"

NOTES="Install: drop \`InfiniteSplitter.dll\` into LiveSplit's \`Components\` folder, next to \`LiveSplit.exe\`, then Edit Layout > + > Control > InfiniteSplitter.

Already installed? LiveSplit picks this up under Help > Check for Updates."

if [ "$GH_METHOD" = gh ]; then
  gh release create "$TAG" "$ZIP" \
      --repo "$REPO" --draft --title "InfiniteSplitter $VERSION" --notes "$NOTES"
  say "draft release created with gh"
else
  BODY=$(TAG="$TAG" VERSION="$VERSION" NOTES="$NOTES" python3 -c \
    'import json,os; print(json.dumps({"tag_name":os.environ["TAG"],"name":"InfiniteSplitter "+os.environ["VERSION"],"body":os.environ["NOTES"],"draft":True}))')

  RESPONSE=$(curl -sS -X POST "https://api.github.com/repos/$REPO/releases" \
      -H "Authorization: Bearer $GITHUB_TOKEN" \
      -H "Accept: application/vnd.github+json" \
      -d "$BODY")

  UPLOAD=$(printf '%s' "$RESPONSE" | python3 -c \
    'import json,sys
d=json.load(sys.stdin)
if "upload_url" not in d:
    sys.exit("GitHub refused the release: " + d.get("message","(no message)"))
print(d["upload_url"].split("{")[0])')

  curl -sS -X POST "$UPLOAD?name=$(basename "$ZIP")" \
      -H "Authorization: Bearer $GITHUB_TOKEN" \
      -H "Content-Type: application/zip" \
      --data-binary @"$ZIP" >/dev/null
  say "draft release created over the REST API"
fi

echo
say "done. The release is a DRAFT — review and publish it at:"
say "  https://github.com/$REPO/releases"
