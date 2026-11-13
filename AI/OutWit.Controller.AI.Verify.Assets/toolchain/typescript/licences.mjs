// The licences of the packages a bundle carries, from esbuild's metafile: one section a package
// (name, version, licence id, its licence file as it is), in name order, so the file is the same
// on every build. Usage: node licences.mjs <meta.json> <out.txt>
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";

const [metaPath, outPath] = process.argv.slice(2);
const meta = JSON.parse(readFileSync(metaPath, "utf8"));

const packages = new Set();
for (const input of Object.keys(meta.inputs)) {
  const parts = input.split("/");
  const at = parts.lastIndexOf("node_modules");
  if (at < 0) continue;
  const name = parts[at + 1].startsWith("@") ? parts[at + 1] + "/" + parts[at + 2] : parts[at + 1];
  packages.add(name);
}

const sections = [];
for (const name of [...packages].sort()) {
  const dir = join("node_modules", ...name.split("/"));
  const manifest = JSON.parse(readFileSync(join(dir, "package.json"), "utf8"));
  const file = ["LICENSE", "LICENSE.md", "LICENSE.txt", "license", "LICENCE"].map((f) => join(dir, f)).find((f) => existsSync(f));
  if (!file) throw new Error(`${name}: no licence file`);
  const text = readFileSync(file, "utf8").replace(/\r\n/g, "\n").trimEnd();
  sections.push(`${name} ${manifest.version} (${manifest.license})\n${"=".repeat(72)}\n${text}\n`);
}

writeFileSync(outPath, "The TypeScript runtime's transpiler (sucrase.js) bundles these packages:\n\n" + sections.join("\n"), "utf8");
console.log(`${packages.size} package(s): ${[...packages].sort().join(", ")}`);
