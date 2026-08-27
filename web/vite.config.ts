import { defineConfig } from "vite";
import type { ViteDevServer } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import ui from "@nuxt/ui/vite";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

// Sync docs/ folder to web/public/docs/
function syncDocs() {
  const repoRoot = path.resolve(fileURLToPath(new URL("..", import.meta.url)));
  const docsSource = path.join(repoRoot, "docs");
  const docsDest = path.resolve("public", "docs");

  function copyMdFiles() {
    if (!fs.existsSync(docsDest)) {
      fs.mkdirSync(docsDest, { recursive: true });
    }
    // Clear existing .md files in dest
    const existing = fs.readdirSync(docsDest).filter((f) => f.endsWith(".md"));
    for (const file of existing) {
      fs.unlinkSync(path.join(docsDest, file));
    }
    // Copy all .md files from source
    const sourceFiles = fs
      .readdirSync(docsSource)
      .filter((f) => f.endsWith(".md"));
    for (const file of sourceFiles) {
      fs.copyFileSync(path.join(docsSource, file), path.join(docsDest, file));
    }
  }

  return {
    name: "sync-docs",
    buildStart() {
      copyMdFiles();
    },
    configureServer(server: ViteDevServer) {
      server.watcher.add(docsSource);
      server.watcher.on("add", (file: string) => {
        if (file.startsWith(docsSource) && file.endsWith(".md")) {
          const fileName = path.basename(file);
          fs.copyFileSync(file, path.join(docsDest, fileName));
          console.log(`[sync-docs] Added: ${fileName}`);
        }
      });
      server.watcher.on("change", (file: string) => {
        if (file.startsWith(docsSource) && file.endsWith(".md")) {
          const fileName = path.basename(file);
          fs.copyFileSync(file, path.join(docsDest, fileName));
          console.log(`[sync-docs] Updated: ${fileName}`);
        }
      });
      server.watcher.on("unlink", (file: string) => {
        if (file.startsWith(docsSource) && file.endsWith(".md")) {
          const fileName = path.basename(file);
          const destPath = path.join(docsDest, fileName);
          if (fs.existsSync(destPath)) {
            fs.unlinkSync(destPath);
            console.log(`[sync-docs] Removed: ${fileName}`);
          }
        }
      });
    },
  };
}

// https://vite.dev/config/
export default defineConfig({
  resolve: {
    alias: {
      // Shared JSON data (relic blacklist, custom pools) lives at the repo root.
      "@shared": path.resolve(
        path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "shared",
      ),
    },
  },
  server: {
    fs: {
      // Allow dev-server reads of the repo root so @shared JSON imports resolve.
      allow: [path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..")],
    },
  },
  plugins: [
    syncDocs(),
    vue(),
    tailwindcss(),
    ui({
      ui: {
        colors: {
          primary: "amber",
        },
        colorMode: {
          preference: "dark",
          fallback: "dark",
        },
      },
      icon: {
        clientBundle: {
          scan: true,
        },
      },
    }),
  ],
});
