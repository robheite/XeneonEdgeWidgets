import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../emby-edge/scripts/main.js", import.meta.url), "utf8");

assert.match(source, /items\/\$\{encodeURIComponent\(id\)\}\/image/);
assert.doesNotMatch(source, /button\.innerHTML/);
assert.match(source, /button\.append\(poster, name, detail\)/);

console.log("Emby item cards use encoded URLs and DOM construction.");
