import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";

const origin = "https://docs.elgato.com";
const rootPath = "/icue/widgets/";
const outputRoot = path.resolve("docs/vendor/elgato-icue-widgets");
const queue = [new URL(rootPath, origin)];
const seen = new Set();
const manifest = [];

function outputPath(url) {
  const relative = url.pathname.slice(rootPath.length).replace(/\/$/, "");
  return path.join(outputRoot, relative || "getting-started", "index.html");
}

while (queue.length > 0) {
  const url = queue.shift();
  const key = url.pathname.endsWith("/") ? url.pathname : `${url.pathname}/`;
  if (seen.has(key)) continue;
  seen.add(key);

  const response = await fetch(url, {
    headers: { "user-agent": "icue-widget-docs-sync/1.0" },
  });
  if (!response.ok) {
    throw new Error(`${response.status} ${response.statusText}: ${url}`);
  }

  const html = await response.text();
  const destination = outputPath(url);
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, html);
  manifest.push({
    source: url.href,
    file: path.relative(process.cwd(), destination).replaceAll("\\", "/"),
  });
  console.log(`Saved ${url.pathname}`);

  for (const match of html.matchAll(/href=["']([^"'#?]+)[^"']*["']/g)) {
    const linked = new URL(match[1], url);
    if (
      linked.origin === origin &&
      linked.pathname.startsWith(rootPath) &&
      !/\.[a-z0-9]+$/i.test(linked.pathname)
    ) {
      queue.push(linked);
    }
  }
}

manifest.sort((a, b) => a.source.localeCompare(b.source));
await writeFile(
  path.join(outputRoot, "manifest.json"),
  `${JSON.stringify(
    {
      source: new URL(rootPath, origin).href,
      syncedAt: new Date().toISOString(),
      pages: manifest,
    },
    null,
    2,
  )}\n`,
);
console.log(`Synced ${manifest.length} pages.`);
