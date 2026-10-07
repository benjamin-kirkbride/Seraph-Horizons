# Recipe browser

A static site for looking up the pack's recipes outside the game (epic #167). The data is
dumped from a running dedicated server, so it matches what the pinned pack loads.

## Layout

```
schema/recipe-export.schema.json   the export format (JSON Schema, draft 2020-12)
schema/examples/minimal.json       a small hand-written export that validates
tools/recipe-export/               CI-only server mod that writes the export (C#)
tools/site-data/                   validates, migrates and assembles exports for the site (TypeScript)
tools/icon-export/                 local client mod that renders the icons (C#; never shipped)
tools/icons.py                     turns an icon export into content-addressed icons
icons/                             icon files (Git LFS) and icons/index.json
site/                              the app: Vite + Svelte + TypeScript
site/models.json                   the model viewer's models (models.md)
tests/PackTests/                   Atlas scenarios, the exporter's included
.github/workflows/pages.yml        builds and deploys the site
```

## Data flow

1. CI's smoke job boots the server with the pack (its own mod built from `mods-src/seraphhorizons`)
   plus the export mod, which writes
   `recipes.json`. The job uploads it as the `recipe-export` artifact.
2. Releases (versioned and `next`) attach it as `<pack id>_<version>_recipes.json`.
3. The Pages workflow downloads the export of every release, and `tools/site-data`
   validates each, migrates it to the current `schemaVersion` and writes:

   ```
   <out>/versions.json
   <out>/<version id>/export.json
   ```

4. For each version the workflow runs `npm --prefix site run prepare-data -- --export
   <out>/<id>/export.json --out site/public/data/<id>`, which writes the files the app
   loads, taking each mod's ModDB asset id from `pack/lock.json`. `versions.json` is copied to `site/public/data/versions.json` and `icons/` to
   `site/public/icons/`.

## versions.json

```json
{
  "default": "v0.1.0",
  "versions": [
    { "id": "main", "label": "main (b0ec8cd)", "packVersion": "0.1.0", "gameVersion": "1.22.7", "commit": "b0ec8cd…" },
    { "id": "v0.1.0", "label": "0.1.0", "packVersion": "0.1.0", "gameVersion": "1.22.7", "commit": "…" }
  ]
}
```

`id` is a directory name under `data/`. `default` is the newest versioned release, or
`main` when there is none.

## icons/index.json

```json
{ "schemaVersion": 1, "size": 64, "icons": { "game:stick": "<sha256 of the png>" } }
```

The image for a hash is `icons/<first two hex digits>/<hash>.png`.
