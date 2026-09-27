/** Build entry: bundles the WebView asset, copies static files, and runs bundled Node tests. */
import { context, build } from "esbuild";
import { cp, mkdir, readdir, rm } from "node:fs/promises";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(fileURLToPath(import.meta.url));
const mode = process.argv[2] ?? "build";

const copyStaticAssets = async () => {
  await mkdir(path.join(root, "dist", "styles"), { recursive: true });
  await cp(path.join(root, "src", "index.html"), path.join(root, "dist", "index.html"));
  await cp(path.join(root, "src", "styles"), path.join(root, "dist", "styles"), { recursive: true });
};

const appOptions = {
  entryPoints: [path.join(root, "src", "main.tsx")],
  bundle: true,
  outfile: path.join(root, "dist", "assets", "app.js"),
  format: "iife",
  platform: "browser",
  target: ["es2022"],
  logLevel: "info"
};

if (mode === "build") {
  await rm(path.join(root, "dist"), { recursive: true, force: true });
  await copyStaticAssets();
  await build({ ...appOptions, minify: true });
} else if (mode === "dev") {
  await copyStaticAssets();
  const appContext = await context({ ...appOptions, sourcemap: true });
  await appContext.watch();
  console.log("Watching Web assets; load dist/index.html through the Host.");
} else if (mode === "test") {
  const testDir = path.join(root, ".test-dist");
  await rm(testDir, { recursive: true, force: true });
  const testFiles = (await readdir(path.join(root, "tests")))
    .filter((name) => name.endsWith(".test.ts"))
    .map((name) => path.join(root, "tests", name));
  await build({ entryPoints: testFiles, bundle: true, outdir: testDir, format: "esm", platform: "node", target: ["node22"] });
  // node --test はディレクトリを受け取れないため、バンドル済みファイルを列挙して渡す
  const bundled = (await readdir(testDir)).filter((name) => name.endsWith(".js")).map((name) => path.join(testDir, name));
  const child = spawn(process.execPath, ["--test", ...bundled], { stdio: "inherit" });
  child.once("exit", async (code) => {
    await rm(testDir, { recursive: true, force: true });
    process.exitCode = code ?? 1;
  });
} else {
  throw new Error(`Unknown mode: ${mode}`);
}
