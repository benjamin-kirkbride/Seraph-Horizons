// The real migration steps, oldest first. Version 1 is the only export version so
// far, so there are none yet. When the schema goes to version N+1, add
// vN-to-vN+1.ts here exporting a MigrationStep and list it below; see
// docs/recipe-browser/deploy.md for the rest of the bump.

import type { MigrationStep } from "../migrate.js";

export const steps: readonly MigrationStep[] = [];
