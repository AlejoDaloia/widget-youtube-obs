import express from "express";
import cors from "cors";
import { execFile } from "child_process";
import { fileURLToPath } from "url";
import path from "path";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const READER_PATH = path.join(
  __dirname,
  "smtc-reader",
  "bin",
  "Release",
  "net8.0-windows10.0.19041.0",
  "SmtcReader.exe"
);

const app = express();
app.use(cors());
app.use(express.json({ limit: "10mb" }));

const PORT = process.env.PORT || 3000;

let cachedNowPlaying = { playing: false };
let currentSignature = null;
let cachedImage = null;

let isPolling = false;
let isFetchingImage = false;

function runReader(includeImage) {
  return new Promise((resolve) => {
    const args = includeImage ? ["--image"] : [];

    execFile(
      READER_PATH,
      args,
      { windowsHide: true, timeout: 8000, maxBuffer: 20 * 1024 * 1024 },
      (err, stdout) => {
        if (err) {
          console.error("SMTC read error:", err.message);
          return resolve(null);
        }

        try {
          resolve(JSON.parse(stdout.trim()));
        } catch {
          resolve(null);
        }
      }
    );
  });
}

async function fetchImageFor(signature) {
  if (isFetchingImage) return;
  isFetchingImage = true;

  const data = await runReader(true);

  isFetchingImage = false;

  if (data?.image && signature === currentSignature) {
    cachedImage = data.image;
    cachedNowPlaying = { ...cachedNowPlaying, image: cachedImage };
  }
}

async function poll() {
  if (isPolling) return;
  isPolling = true;

  const data = await runReader(false);

  isPolling = false;

  if (!data || !data.playing) {
    cachedNowPlaying = { playing: false };
    currentSignature = null;
    cachedImage = null;
    return;
  }

  const signature = `${data.title}::${data.artist}`;

  if (signature !== currentSignature) {
    currentSignature = signature;
    cachedImage = null;
    fetchImageFor(signature);
  }

  cachedNowPlaying = { ...data, image: cachedImage };
}

app.get("/now-playing", (req, res) => {
  res.json(cachedNowPlaying);
});

setInterval(poll, 1000);
poll();

app.listen(PORT, () => {
  console.log(`Running on http://localhost:${PORT}`);
});
