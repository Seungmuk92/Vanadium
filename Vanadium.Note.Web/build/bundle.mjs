// Self-hosted editor dependency bundler (issue #307).
//
// Produces the local ESM assets under wwwroot/js/vendor/ that replace the
// former esm.sh / jsdelivr runtime imports. Run with `npm run build` from the
// Vanadium.Note.Web directory after `npm ci`.
//
// The whole stack is bundled with esbuild code-SPLITTING on purpose: every
// entry shares a single set of chunks, so ProseMirror (@tiptap/pm) exists as
// exactly ONE instance across all entries. Bundling each entry standalone would
// inline a private ProseMirror copy per entry and break the editor at runtime
// ("Adding different instances of a keyed plugin").
//
// Mermaid is bundled the same way: esbuild statically resolves its lazy
// per-diagram `import()` calls into local, same-origin chunks under
// wwwroot/js/vendor/chunks/, so on-demand diagram loading keeps working with no
// CDN. (Its published dist tree is 24 MB of mixed build variants + sourcemaps,
// so vendoring that verbatim is deliberately avoided.)

import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import {
  mkdirSync, rmSync, writeFileSync,
} from 'node:fs';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const webRoot = join(here, '..');
const vendorDir = join(webRoot, 'wwwroot', 'js', 'vendor');
const entriesDir = join(here, '.entries');

// specifier (what app code imports) -> { out: output basename, reexport: entry body }
const ENTRIES = {
  '@tiptap/core': { out: 'tiptap-core', reexport: star('@tiptap/core') },
  '@tiptap/pm/state': { out: 'tiptap-pm-state', reexport: star('@tiptap/pm/state') },
  '@tiptap/pm/view': { out: 'tiptap-pm-view', reexport: star('@tiptap/pm/view') },
  '@tiptap/starter-kit': { out: 'tiptap-starter-kit', reexport: def('@tiptap/starter-kit') },
  '@tiptap/suggestion': { out: 'tiptap-suggestion', reexport: def('@tiptap/suggestion') },
  '@tiptap/extension-bubble-menu': { out: 'tiptap-extension-bubble-menu', reexport: def('@tiptap/extension-bubble-menu') },
  '@tiptap/extension-placeholder': { out: 'tiptap-extension-placeholder', reexport: def('@tiptap/extension-placeholder') },
  '@tiptap/extension-link': { out: 'tiptap-extension-link', reexport: def('@tiptap/extension-link') },
  '@tiptap/extension-image': { out: 'tiptap-extension-image', reexport: def('@tiptap/extension-image') },
  '@tiptap/extension-task-list': { out: 'tiptap-extension-task-list', reexport: def('@tiptap/extension-task-list') },
  '@tiptap/extension-task-item': { out: 'tiptap-extension-task-item', reexport: def('@tiptap/extension-task-item') },
  '@tiptap/extension-table': { out: 'tiptap-extension-table', reexport: def('@tiptap/extension-table') },
  '@tiptap/extension-table-row': { out: 'tiptap-extension-table-row', reexport: def('@tiptap/extension-table-row') },
  '@tiptap/extension-table-header': { out: 'tiptap-extension-table-header', reexport: def('@tiptap/extension-table-header') },
  '@tiptap/extension-table-cell': { out: 'tiptap-extension-table-cell', reexport: def('@tiptap/extension-table-cell') },
  '@tiptap/extension-code-block-lowlight': { out: 'tiptap-extension-code-block-lowlight', reexport: def('@tiptap/extension-code-block-lowlight') },
  '@tiptap/extension-heading': { out: 'tiptap-extension-heading', reexport: def('@tiptap/extension-heading') },
  'tiptap-markdown': { out: 'tiptap-markdown', reexport: named('tiptap-markdown', ['Markdown']) },
  lowlight: { out: 'lowlight', reexport: named('lowlight', ['createLowlight', 'common']) },
  mermaid: { out: 'mermaid', reexport: def('mermaid') },
};

function star(spec) { return `export * from '${spec}';\n`; }
function def(spec) { return `export { default } from '${spec}';\n`; }
function named(spec, names) { return `export { ${names.join(', ')} } from '${spec}';\n`; }

async function main() {
  // Fresh output tree.
  rmSync(vendorDir, { recursive: true, force: true });
  mkdirSync(vendorDir, { recursive: true });
  rmSync(entriesDir, { recursive: true, force: true });
  mkdirSync(entriesDir, { recursive: true });

  // Write one physical entry file per specifier so esbuild can code-split them.
  const entryPoints = {};
  for (const [, cfg] of Object.entries(ENTRIES)) {
    const file = join(entriesDir, `${cfg.out}.js`);
    writeFileSync(file, cfg.reexport, 'utf8');
    entryPoints[cfg.out] = file;
  }

  await build({
    entryPoints,
    bundle: true,
    splitting: true,
    format: 'esm',
    outdir: vendorDir,
    minify: true,
    sourcemap: false,
    target: ['es2020'],
    chunkNames: 'chunks/[name]-[hash]',
    legalComments: 'none',
    logLevel: 'info',
    absWorkingDir: webRoot,
  });

  rmSync(entriesDir, { recursive: true, force: true });

  // Emit the import map that index.html mirrors, so the two never drift.
  // Addresses MUST be root-absolute (leading "/"): the import maps spec only
  // resolves values that start with "/", "./" or "../" (or are absolute URLs)
  // and silently DROPS any bare "js/vendor/..." value, which would break every
  // mapping. The app is served from the site root (<base href="/">), so
  // "/js/vendor/..." is the safe, unambiguous form.
  const imports = {};
  for (const [spec, cfg] of Object.entries(ENTRIES)) {
    imports[spec] = `/js/vendor/${cfg.out}.js`;
  }
  const importmap = { imports };
  writeFileSync(join(vendorDir, 'importmap.json'), `${JSON.stringify(importmap, null, 2)}\n`, 'utf8');

  console.log('\nimportmap (mirror into wwwroot/index.html):');
  console.log(JSON.stringify(importmap, null, 2));
  console.log('\nDone. Vendor assets written to wwwroot/js/vendor/');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
