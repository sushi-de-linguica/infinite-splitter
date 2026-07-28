InfiniteSplitter — auto splitter and in-game time for Infinitevania
===================================================================
https://github.com/sushi-de-linguica/infinite-splitter
===================================================================

A LiveSplit component. It reads the game's memory from outside the process:
nothing is installed into the game folder and no game file is touched.

INSTALL
-------
1. Close LiveSplit (it locks component DLLs while running).
2. Copy InfiniteSplitter.dll into LiveSplit's Components folder, next to
   LiveSplit.exe.
3. Start LiveSplit, right-click > Edit Layout > + > Control > InfiniteSplitter.
4. Right-click > Edit Layout > Layout Settings > InfiniteSplitter to configure.

Steam must be running for the game to resolve its save folder.

One file, nothing else to copy. Later versions can be installed the same way, or
picked up by LiveSplit's own updater under Help > Check for Updates.

If a split does not fire, open Layout Settings > InfiniteSplitter > Debug: it
reports what the component found and what it is reading right now.