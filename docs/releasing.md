# Releasing

```bash
make publish-dry   # build and package only — nothing committed, nothing pushed
make publish       # build, commit, tag, push, and open a DRAFT release
```

`tools/publish.sh` is the whole flow and it does not stop to ask: running it *is*
the confirmation. What it opens on GitHub is a **draft**, so the release only
becomes visible when a human reads the page and presses publish.

`--dry-run` is a rehearsal with no side effects at all. It builds and packages,
reports what a real run would do, and leaves the repository exactly as it found
it — no commit, no tag, nothing pushed. `Components/` will show as modified
afterwards; that is the build output, uncommitted.

## What a release is made of

Two halves, and both have to happen or the release is half-broken:

| | For | Fetched from |
|---|---|---|
| `dist/InfiniteSplitter-<version>.zip` | first-time installs | the GitHub release |
| `Components/InfiniteSplitter.dll` **committed** | LiveSplit's updater | the repository, over `raw.githubusercontent.com` |

The updater does not look at GitHub releases at all. Read out of
`UpdateManager.exe`, the chain is:

```
UpdaterInternal.get_Updates   XmlReader.Create(XMLURL) -> XmlDocument.Load
Update.Parse                  attribute `version`, children `changelog` and `files`
FileChange.Parse              attributes `path`, `localPath`, `status`
UpdaterInternal.PerformUpdate UpdateURL + path -> DownloadFile
```

So every file comes from `UpdateURL + path` — for us,
`raw.githubusercontent.com/<repo>/main/` + `Components/InfiniteSplitter.dll`.
Nothing in that path touches `api.github.com`. That is why `publish.sh` commits
`Components/` as part of the release rather than leaving it to be remembered: a
GitHub release nobody's updater reads would update nobody.

`path` doubles as the install location — `PerformUpdate` only swaps the slashes —
so `Components/InfiniteSplitter.dll` lands in LiveSplit's own `Components`
folder. `localPath` overrides that when the two need to differ.

`status` parses to `ChangeStatus`: **`Added`**, **`Changed`**, **`Removed`**.
Case-insensitive, so the lowercase `changed` in the manifest is fine. `Removed`
is the way to make the updater *delete* a file it installed earlier — the tool to
reach for if a future version stops shipping something.

## One DLL

`InfiniteMemory` is merged into `InfiniteSplitter.dll` on Release builds by
`src/InfiniteSplitter/ILRepack.targets`. A user copies one file, and half an
install is no longer a state that exists.

Two things about that targets file are load-bearing:

- **The target must not be named `ILRepack`.** The package imports our file and
  *then* declares a target of that name; a later definition wins, so the merge
  would silently never run and the build would still succeed.
- **`LibraryPath` comes from `@(ReferencePath)`.** ILRepack resolves everything
  the inputs reference, including `LiveSplit.Core` and `UpdateManager`, which are
  deliberately not copied to the output.

Debug builds do not merge, so a debugger can still step into `InfiniteMemory`
with its own symbols.

## Versioning

The version is declared **once**, in
`src/InfiniteSplitter/InfiniteSplitter.csproj`. `Components/Updates.xml` has to
repeat it, because LiveSplit's updater reads the manifest rather than the
assembly — so a manifest claiming a version that was never built makes the
updater either do nothing or loop.

`make version-check` compares the two and the release refuses to build when they
disagree. Bumping a version means editing the csproj and adding an
`<update version>` entry at the top of the manifest.

## The branch is part of the URL

`raw.githubusercontent.com` serves exactly one branch, so a repository pushed as
`master` returns 404 for a URL that says `main`. The branch is the `Branch`
constant in `InfiniteSplitterFactory`, compiled into the DLL — which means a
mismatch cannot be fixed after the fact for anyone who already installed it.

`publish.sh` reads that constant and refuses to release from any other branch. It
reads `Repository` the same way, so the URLs and the release target cannot drift
apart.

## What publish.sh does, in order

Every check that can reject the release runs **before the first side effect**, so
a run either does all of it or none of it:

1. It is a git repository, and the current branch matches the compiled-in one.
2. An `origin` remote exists — otherwise it prints the command to create it.
3. Nothing outside `Components/` is uncommitted. A tag has to point at what was
   actually built.
4. `v<version>` is not already a tag.
5. GitHub credentials resolve. A real run **refuses to start** without them
   rather than pushing a tag and then failing to open the release against it.
6. `make all` — offline suite, compile, `Components/`, version check, zip.

Then, and only on a real run:

7. `Components/` is committed as `release v<version>`, if the build changed it.
8. The tag is created and both the branch and the tag are pushed.
9. The draft release is opened with the zip attached.

## GitHub credentials

Whichever is available, in this order:

- **`gh`**, the GitHub CLI, if installed and authenticated (`gh auth login`).
- **`GITHUB_TOKEN`**, a personal access token with `repo` scope, used over the
  REST API with `curl`.

With neither, `make publish` refuses to start and says so. `make publish-dry`
still works — it warns, but needs no credentials to build.

## Bumping a version

1. Edit `<Version>` in `src/InfiniteSplitter/InfiniteSplitter.csproj`.
2. Add an `<update version="…">` entry at the **top** of
   `Components/Updates.xml`, with a changelog. Newest first — LiveSplit reads the
   list in order.
3. Commit the source changes.
4. `make publish-dry` to rehearse, then `make publish`.
5. Review the draft on GitHub and publish it.

Step 3 matters: `publish.sh` refuses to run with uncommitted changes outside
`Components/`, so that the tag it creates points at exactly what was built.
