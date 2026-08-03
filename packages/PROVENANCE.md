# Vendored assemblies

Two third-party binaries live here. They are **compile-time references only** —
both are declared `<Private>false</Private>` in
`src/InfiniteSplitter/InfiniteSplitter.csproj`, so neither is ever copied next to
the shipped component. At runtime they come from LiveSplit itself.

They are committed because the build must work without a LiveSplit install; that
is what lets the component be compiled from Linux/WSL.

| File | Origin |
|---|---|
| `LiveSplit.Core.dll` | LiveSplit 1.8.37, taken verbatim from the release's program directory |
| `UpdateManager.dll` | LiveSplit 1.8.37, same directory |

Both are unmodified copies. LiveSplit is MIT-licensed
(<https://github.com/LiveSplit/LiveSplit>).

`net481` is not a stylistic choice: these assemblies are themselves `net481`, and
referencing them from `net48` fails to resolve.

## Integrity

`SHA256SUMS` pins the exact bytes. CI verifies it on every pull request
(`.github/workflows/supply-chain.yml`), so a silent swap of either file fails the
build rather than reaching a release.

Verify locally:

```bash
cd packages && sha256sum -c SHA256SUMS
```

## Updating to a newer LiveSplit

Only when the component is deliberately retargeted at a new LiveSplit release:

1. Copy the two DLLs out of the new release's program directory.
2. Regenerate the manifest — from within `packages/`, so the paths stay relative:

   ```bash
   cd packages && sha256sum LiveSplit.Core.dll UpdateManager.dll > SHA256SUMS
   ```

3. Update the version in the table above.
4. `make all` — the API surface these expose is compiled against, so a breaking
   change shows up as a compile error, not at runtime in a user's timer.

Commit the DLLs, `SHA256SUMS` and this file together. A hash update arriving
without a matching binary change, or the reverse, is exactly what the CI check
exists to catch.
