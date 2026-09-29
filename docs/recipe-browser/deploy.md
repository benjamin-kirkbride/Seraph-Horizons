# Recipe browser: data and deploy

How the recipe exports get from CI to GitHub Pages. The layout and the file formats
are in [README.md](README.md).

## From CI to a release

CI's smoke job dumps `recipes.json` from the server and uploads it as the
`recipe-export` artifact. `tools/site-data validate` checks it in the same CI run,
so a pull request that breaks the export fails CI.

Both release workflows attach it as `<pack id>_<version>_recipes.json`, next to the
other `dist/` files, and add its hash to `SHA256SUMS`:

- `release.yml` (tag `vX.Y.Z`) downloads the artifact from the CI run it calls.
- `next.yml` (every push to main that passes CI) downloads it from the CI run it
  republishes, together with `dist`.

Nothing generated is committed.

## From releases to the site

`.github/workflows/pages.yml` builds and deploys the site. It runs:

- after `Release` or `Next release` completes successfully. A release created with
  the workflow token triggers no workflow itself, so Pages waits on the workflow
  that published it, not on the tag or the release;
- by hand (Actions > Pages > Run workflow, from `main`).

It never runs for pull requests, and the job checks the repository name so a fork
does not deploy. One deploy runs at a time; a deploy queued behind another one
picks up every release published meanwhile.

The build:

1. `site-data fetch` lists the published releases with `gh`, and downloads the
   `*_recipes.json` asset of each `vX.Y.Z` tag and of `next`. Other tags are
   ignored. It writes the downloads and `releases.json` (tag, commit, file) to a
   scratch directory.
2. `site-data assemble` validates each export, migrates it to the current
   `schemaVersion`, and writes `versions.json` and `<id>/export.json`. `next` becomes
   the id `main`; a release keeps its tag as its id. A release without an export,
   such as every release made before the browser existed, is skipped with a note.
   An invalid export fails the deploy, and the site already live stays up.
   `default` is the newest release by semver (0.10.0 is newer than 0.9.0, and
   1.0.0-rc.1 is older than 1.0.0), or `main` when no release has an export.
3. For each version, `npm --prefix site run prepare-data` turns the export into the
   files the app loads, under `site/public/data/<id>/`. `versions.json` and `icons/`
   (fetched from Git LFS, only in this workflow) are copied next to them.
4. `npm --prefix site run build`, the size check, then upload and deploy with the
   official Pages actions.

The same steps locally, given `gh` access:

```sh
npm ci --prefix tools/site-data
npm --prefix tools/site-data run build
node tools/site-data/dist/bin.js fetch --repo benjamin-kirkbride/Seraph-Horizons --out /tmp/releases
node tools/site-data/dist/bin.js assemble --releases /tmp/releases --out /tmp/data
node tools/site-data/dist/bin.js validate path/to/recipes.json   # one file, as CI does
npm --prefix tools/site-data test
```

## Changing the export format

The export carries `schemaVersion`. The site keeps every release, so it reads exports
of every version ever published. `site-data` migrates each to the current version
before the app sees it, and the app only reads the current format.

A change that old readers can ignore needs no bump. That covers a new optional
property, or anything under `extra`. Anything else is a bump from N to N+1:

1. Copy `schema/recipe-export.schema.json` as it is now to
   `schema/archive/recipe-export.vN.schema.json`. Exports of version N are still
   validated against it on the way in. Archived schemas are never edited.
2. Change `schema/recipe-export.schema.json` and set `properties.schemaVersion.const`
   to N+1. That constant is what `site-data` takes as the current version.
3. Add `tools/site-data/src/migrations/vN-to-vN+1.ts` exporting a `MigrationStep`
   (`from: N`, `to: N+1`, a one-line `description`, and `up`), and list it in
   `src/migrations/index.ts`. `up` is a pure function: it gets a frozen document and
   returns a new one with `schemaVersion: N+1`. The result is validated against the
   new schema. The step only has to handle version N, because the steps chain:
   version 1 goes 1 to 2 to 3.
4. Add a test with a hand-written version N document and the expected output.
5. Update the exporter (`tools/recipe-export`), the app (`site/`), `schema.md` and
   `schema/examples/minimal.json`. If the cross-reference checks in
   `src/checks.ts` depend on the changed parts, update them too. They run on the
   current version only.

The test "has a step and an archived schema for every version before the current
one" fails if step 1 or 3 is missing. A release made after the bump can only be read
by a `site-data` that knows the new version. Older `site-data` builds refuse it
instead of guessing.

## Size

GitHub Pages refuses sites larger than about 1 GB, and the site grows with every
release. The size step prints the size of each version, the icons and the rest to the
job summary. It warns above 70% of 1 GB and fails the deploy above 95%. The site
already live stays up when it fails. Change the thresholds with `--limit-bytes`,
`--warn-at` and `--fail-at` in `pages.yml`.

When the warning appears, look at the summary table and pick one:

- Drop old versions from the site. Filter tags in `fetch` or `assemble`, for
  example keeping only the newest release of each minor version. The releases
  themselves keep their assets.
- Make the per-version data smaller in `prepare-data`. Versions often share most
  of their content, which leaves room for deduplication.
- Shrink the icons (`tools/icons.py`).

## One-time repository setting

Settings > Pages > Build and deployment > Source: **GitHub Actions**. The
`github-pages` environment that the deploy job uses is created on the first deploy.
If it has deployment branch rules, `main` must be allowed. `workflow_run` runs on the
default branch.

Until a release has an export attached, `assemble` fails with "no release has a
recipe export" and nothing is deployed. The first push to main after the export
lands in CI fixes that through `next`.
