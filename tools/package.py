#!/usr/bin/env python3
"""Build the release zip a user unzips into LiveSplit's Components folder.

usage: package.py <target.zip> <Components dir>
"""

import pathlib
import sys
import zipfile

SHIPPED = ["InfiniteSplitter.dll", "readme.txt"]


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    target, components = sys.argv[1], pathlib.Path(sys.argv[2])

    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in SHIPPED:
            path = components / name
            if not path.exists():
                print(f"missing from {components}: {name}", file=sys.stderr)
                return 1
            archive.write(path, name)

    print(f"   {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
