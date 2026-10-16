# The editor's guide

The guide is made from the editor's tours (`tours/*.tour.json` in the workspace): a page per tour with a picture
of each step, and an index. The pictures show the game's terrain and models, so they are made on a computer that has
the game's assets and committed; CI only checks that the tours still find their targets.

## Making it

1. Build the editor and the tool: `dotnet build src/FAForever.MapEditor.App` and `dotnet build src/FAForever.MapEditor.Cli`.
2. Make a scratch workspace (never your own: the guide plays the tours in it and changes its map):

   ```sh
   faf-map workspace scratch D:/guide-ws --from <your workspace> --settings D:/guide-ws-settings.json
   ```

3. Make the guide (the editor starts and stops by itself; pick any free port but 5371):

   ```sh
   faf-map guide build --out docs/guide --port 5399 --settings D:/guide-ws-settings.json
   ```

   `--tours window,terrain` plays only those, `--map <folder>` opens another map than the sandbox,
   `--size 1280x800` sets the window's size. The pictures are WebP in `images/<tour>/`, the pages are
   `index.md`, `index.html` and one pair per tour, `guide.json` holds everything they are written from.

4. Look at the pictures, then commit the folder.

## Keeping it true

- `faf-map guide check --out docs/guide --port 5399 --settings D:/guide-ws-settings.json` plays the targets
  without pictures. A target not found fails; a target that moved by more than 8 pixels, and steps or tours
  added or removed, are listed.
- `faf-map guide render --out docs/guide` writes the pages again from `guide.json`, without an editor: after a text
  in `guide.json` was edited or the page layout changed. A text changed in a tour's file reaches the guide with `build`.
- The pictures carry no text but the numbers of the overview: the words are on the page.
- The pictures are dark theme at one window size.
