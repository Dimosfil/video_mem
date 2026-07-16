from __future__ import annotations

import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from yt_dlp import YoutubeDL


YOUTUBE_HOSTS = {
    "youtube.com",
    "www.youtube.com",
    "m.youtube.com",
    "music.youtube.com",
    "youtu.be",
    "www.youtube-nocookie.com",
}


class MediaError(RuntimeError):
    pass


@dataclass(frozen=True)
class MediaInfo:
    video_id: str
    title: str
    duration: float
    thumbnail: str | None


def extract_youtube_video_id(url: str) -> str:
    parsed = urlparse(url.strip())
    if parsed.scheme not in {"http", "https"} or parsed.hostname not in YOUTUBE_HOSTS:
        raise ValueError("Поддерживаются только ссылки на отдельные видео YouTube.")
    if parsed.hostname == "youtu.be":
        video_id = parsed.path.strip("/").split("/", 1)[0]
    elif parsed.path == "/watch":
        video_id = parse_qs(parsed.query).get("v", [""])[0]
    elif parsed.path.startswith(("/shorts/", "/embed/", "/live/")):
        parts = parsed.path.strip("/").split("/")
        video_id = parts[1] if len(parts) > 1 else ""
    else:
        video_id = ""
    if not video_id or len(video_id) > 32 or not all(
        char.isalnum() or char in {"-", "_"} for char in video_id
    ):
        raise ValueError("Не удалось определить идентификатор видео YouTube.")
    return video_id


class YtDlpMediaService:
    def __init__(
        self,
        *,
        ffmpeg_executable: str = "ffmpeg",
        cookie_file: Path | None = None,
        timeout_seconds: int = 1800,
        max_upload_bytes: int = 50_000_000,
    ):
        self.ffmpeg_executable = ffmpeg_executable
        self.cookie_file = cookie_file
        self.timeout_seconds = timeout_seconds
        self.max_upload_bytes = max_upload_bytes

    def inspect(self, url: str) -> MediaInfo:
        expected_id = extract_youtube_video_id(url)
        options = self._base_options()
        options.update({"skip_download": True})
        try:
            with YoutubeDL(options) as ydl:
                info = ydl.extract_info(url, download=False)
        except Exception as exc:
            raise MediaError(f"Не удалось получить данные видео: {exc}") from exc
        if info.get("_type") == "playlist":
            raise MediaError("Плейлисты в MVP не поддерживаются.")
        duration = info.get("duration")
        if not isinstance(duration, (int, float)) or duration <= 0:
            raise MediaError("YouTube не вернул длительность видео.")
        return MediaInfo(
            video_id=str(info.get("id") or expected_id),
            title=str(info.get("title") or "YouTube video"),
            duration=float(duration),
            thumbnail=str(info["thumbnail"]) if info.get("thumbnail") else None,
        )

    def create_clip(self, url: str, start: float, end: float, job_dir: Path) -> Path:
        extract_youtube_video_id(url)
        ffmpeg_path = shutil.which(self.ffmpeg_executable) or (
            self.ffmpeg_executable if Path(self.ffmpeg_executable).is_file() else None
        )
        if not ffmpeg_path:
            raise MediaError("FFmpeg не найден. Укажите FFMPEG_EXECUTABLE или добавьте его в PATH.")

        job_dir.mkdir(parents=True, exist_ok=True)
        source_template = str(job_dir / "source.%(ext)s")
        options = self._base_options()
        options.update(
            {
                "format": (
                    "b[height<=720][ext=mp4][vcodec^=avc1][acodec^=mp4a]/"
                    "b[height<=720][ext=mp4]/"
                    "bv*[height<=720][ext=mp4]+ba[ext=m4a]/best[height<=720]/best"
                ),
                "outtmpl": source_template,
                "merge_output_format": "mp4",
                "download_ranges": lambda _info, _ydl: [
                    {"start_time": start, "end_time": end}
                ],
            }
        )
        try:
            with YoutubeDL(options) as ydl:
                ydl.download([url])
        except Exception as exc:
            raise MediaError(f"Не удалось скачать выбранный фрагмент: {exc}") from exc

        candidates = sorted(
            path
            for path in job_dir.glob("source.*")
            if path.is_file() and path.suffix.lower() in {".mp4", ".mkv", ".webm"}
        )
        if not candidates:
            raise MediaError("yt-dlp не создал файл выбранного фрагмента.")
        source_path = candidates[0]
        output_path = job_dir / "clip.mp4"
        duration = end - start
        video_bitrate = self._target_video_bitrate_kbps(duration)
        command = [
            ffmpeg_path,
            "-y",
            "-i",
            str(source_path),
            "-vf",
            "scale=-2:720",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-b:v",
            f"{video_bitrate}k",
            "-maxrate",
            f"{max(video_bitrate, 400) + 300}k",
            "-bufsize",
            f"{max(video_bitrate * 2, 800)}k",
            "-c:a",
            "aac",
            "-b:a",
            "128k",
            "-movflags",
            "+faststart",
            str(output_path),
        ]
        try:
            result = subprocess.run(
                command,
                check=False,
                capture_output=True,
                text=True,
                timeout=self.timeout_seconds,
            )
        except subprocess.TimeoutExpired as exc:
            raise MediaError("FFmpeg превысил допустимое время обработки.") from exc
        if result.returncode != 0 or not output_path.is_file():
            error_tail = "\n".join(result.stderr.splitlines()[-12:])
            raise MediaError(f"FFmpeg не смог подготовить ролик. {error_tail}".strip())
        if output_path.stat().st_size > self.max_upload_bytes:
            raise MediaError("Готовый ролик превышает лимит отправки Telegram.")
        if source_path != output_path:
            source_path.unlink(missing_ok=True)
        return output_path

    def _base_options(self) -> dict[str, object]:
        options: dict[str, object] = {
            "quiet": True,
            "no_warnings": True,
            "noplaylist": True,
            "socket_timeout": 30,
            "retries": 3,
            "fragment_retries": 3,
        }
        node_path = shutil.which("node")
        if node_path:
            options["js_runtimes"] = {"node": {"path": node_path}}
        if self.cookie_file:
            options["cookiefile"] = str(self.cookie_file)
        return options

    def _target_video_bitrate_kbps(self, duration: float) -> int:
        usable_bits = self.max_upload_bytes * 8 * 0.88
        total_kbps = int(usable_bits / max(duration, 1) / 1000)
        return max(300, min(1500, total_kbps - 128))
