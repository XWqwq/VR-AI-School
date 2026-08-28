"""Small on-demand Edge TTS service for the PICO virtual-campus client."""
import asyncio
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import edge_tts

HOST = os.environ.get("EDGE_TTS_HOST", "0.0.0.0")
PORT = int(os.environ.get("EDGE_TTS_PORT", "8766"))
DEFAULT_VOICE = os.environ.get("EDGE_TTS_VOICE", "zh-CN-XiaoxiaoNeural")
MAX_TEXT_LENGTH = 500


async def synthesize(text: str, voice: str) -> bytes:
    audio = bytearray()
    communicate = edge_tts.Communicate(text, voice)
    async for part in communicate.stream():
        if part["type"] == "audio":
            audio.extend(part["data"])
    return bytes(audio)


class TtsHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        request = urlparse(self.path)
        if request.path != "/synthesize":
            self.send_error(404, "Use /synthesize?text=...")
            return
        values = parse_qs(request.query)
        text = values.get("text", [""])[0].strip()
        voice = values.get("voice", [DEFAULT_VOICE])[0].strip() or DEFAULT_VOICE
        if not text:
            self.send_error(400, "text is required")
            return
        if len(text) > MAX_TEXT_LENGTH:
            self.send_error(400, f"text must be at most {MAX_TEXT_LENGTH} characters")
            return
        try:
            audio = asyncio.run(synthesize(text, voice))
            if not audio:
                raise RuntimeError("Edge TTS returned no audio")
            self.send_response(200)
            self.send_header("Content-Type", "audio/mpeg")
            self.send_header("Content-Length", str(len(audio)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(audio)
        except Exception as exc:
            self.send_response(502)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.end_headers()
            self.wfile.write(json.dumps({"error": str(exc)}, ensure_ascii=False).encode("utf-8"))

    def log_message(self, format, *args):
        print("[EdgeTTS] " + format % args)


if __name__ == "__main__":
    print(f"Edge TTS service listening on http://{HOST}:{PORT}/synthesize")
    ThreadingHTTPServer((HOST, PORT), TtsHandler).serve_forever()
