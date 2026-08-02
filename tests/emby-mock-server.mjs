import { createServer } from "node:http";
import { createReadStream, statSync } from "node:fs";

const mediaPath = process.env.EMBY_EDGE_TEST_MEDIA;
const port = Number(process.env.EMBY_EDGE_TEST_PORT || 8097);
if (!mediaPath) throw new Error("Set EMBY_EDGE_TEST_MEDIA to a WebM test file");

createServer((request, response) => {
  const url = new URL(request.url, `http://127.0.0.1:${port}`);
  if (!url.pathname.endsWith("/stream.webm")) {
    response.writeHead(404).end();
    return;
  }

  const required = { VideoCodec: "vpx", AudioCodec: "vorbis", MaxAudioChannels: "2" };
  for (const [name, value] of Object.entries(required)) {
    if (url.searchParams.get(name) !== value) {
      response.writeHead(400, { "Content-Type": "text/plain" }).end(`Missing ${name}=${value}`);
      return;
    }
  }

  const size = statSync(mediaPath).size;
  const range = request.headers.range?.match(/^bytes=(\d+)-(\d*)$/);
  const start = range ? Number(range[1]) : 0;
  const end = range?.[2] ? Number(range[2]) : size - 1;
  const headers = {
    "Accept-Ranges": "bytes",
    "Content-Length": end - start + 1,
    "Content-Type": "video/webm",
  };
  if (range) headers["Content-Range"] = `bytes ${start}-${end}/${size}`;
  response.writeHead(range ? 206 : 200, headers);
  createReadStream(mediaPath, { start, end }).pipe(response);
}).listen(port, "127.0.0.1", () => console.log(`Mock Emby listening on ${port}`));
