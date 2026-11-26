const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");

const root = path.resolve(__dirname, "../public");
const mime = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png", ".ico": "image/x-icon" };
const server = http.createServer((req, res) => {
  let pathname;
  try {
    pathname = decodeURIComponent(new URL(req.url, "http://localhost").pathname);
  } catch {
    res.writeHead(400).end("Bad request");
    return;
  }
  const requested = path.resolve(root, `.${pathname === "/" ? "/index.html" : pathname}`);
  if (requested !== root && !requested.startsWith(root + path.sep)) {
    res.writeHead(403).end("Forbidden");
    return;
  }
  fs.readFile(requested, (error, body) => {
    if (error) {
      res.writeHead(404).end("Not found");
      return;
    }
    res.writeHead(200, {
      "Content-Type": mime[path.extname(requested)] || "application/octet-stream",
      "Content-Security-Policy": "default-src 'self'; connect-src 'self' https://api.venice.ai; img-src 'self' data:; style-src 'self'; script-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'",
      "X-Content-Type-Options": "nosniff",
    }).end(body);
  });
});

function stop() {
  server.close();
}

server.listen(1420, "127.0.0.1", () => {
  console.log("Aether desktop frontend listening on http://localhost:1420");
  if (process.argv.includes("--dev")) {
    const tauri = path.resolve(__dirname, "../node_modules/.bin", process.platform === "win32" ? "tauri.cmd" : "tauri");
    const child = spawn(tauri, ["dev"], {
      cwd: path.resolve(__dirname, ".."),
      stdio: "inherit",
      shell: process.platform === "win32",
    });
    child.once("error", (error) => {
      console.error("Could not start the Tauri dev process:", error.message);
      stop();
      process.exitCode = 1;
    });
    child.once("exit", (code, signal) => {
      stop();
      process.exitCode = code ?? (signal ? 1 : 0);
    });
  }
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
