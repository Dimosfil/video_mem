from __future__ import annotations

import os
import re
from dataclasses import dataclass
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parent.parent
TOKEN_PATTERN = re.compile(r"^\d{6,}:[A-Za-z0-9_-]{20,}$")


class ConfigError(ValueError):
    pass


def load_dotenv(path: Path) -> None:
    if not path.is_file():
        return
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        if key and key not in os.environ:
            os.environ[key] = value.strip().strip('"').strip("'")


def _boolean(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    normalized = value.strip().lower()
    if normalized in {"1", "true", "yes", "on"}:
        return True
    if normalized in {"0", "false", "no", "off"}:
        return False
    raise ConfigError(f"{name} must be true or false.")


def _positive_int(name: str, default: int) -> int:
    raw = os.getenv(name, str(default)).strip()
    try:
        value = int(raw)
    except ValueError as exc:
        raise ConfigError(f"{name} must be an integer.") from exc
    if value < 1:
        raise ConfigError(f"{name} must be positive.")
    return value


@dataclass(frozen=True)
class BotConfig:
    telegram_token: str | None
    telegram_polling_enabled: bool
    data_dir: Path
    max_clip_seconds: int
    max_upload_bytes: int
    workers: int
    job_ttl_hours: int
    media_timeout_seconds: int
    cookie_file: Path | None
    ffmpeg_executable: str

    @classmethod
    def from_environment(cls, env_file: Path | None = None) -> "BotConfig":
        load_dotenv(env_file or PROJECT_ROOT / ".env")
        token = os.getenv("TELEGRAM_BOT_TOKEN", "").strip() or None
        polling = _boolean("TELEGRAM_POLLING_ENABLED", False)
        if polling and not token:
            raise ConfigError("TELEGRAM_BOT_TOKEN is required when polling is enabled.")
        if token and not TOKEN_PATTERN.fullmatch(token):
            raise ConfigError("TELEGRAM_BOT_TOKEN has an invalid format.")

        raw_data_dir = Path(os.getenv("VIDEO_BOT_DATA_DIR", "data/video-bot"))
        data_dir = raw_data_dir if raw_data_dir.is_absolute() else PROJECT_ROOT / raw_data_dir
        raw_cookie = os.getenv("YTDLP_COOKIE_FILE", "").strip()
        cookie_file = Path(raw_cookie).expanduser().resolve() if raw_cookie else None
        if cookie_file and not cookie_file.is_file():
            raise ConfigError("YTDLP_COOKIE_FILE does not exist.")

        return cls(
            telegram_token=token,
            telegram_polling_enabled=polling,
            data_dir=data_dir.resolve(),
            max_clip_seconds=_positive_int("VIDEO_BOT_MAX_CLIP_SECONDS", 180),
            max_upload_bytes=_positive_int("VIDEO_BOT_MAX_UPLOAD_BYTES", 50_000_000),
            workers=_positive_int("VIDEO_BOT_WORKERS", 2),
            job_ttl_hours=_positive_int("VIDEO_BOT_JOB_TTL_HOURS", 24),
            media_timeout_seconds=_positive_int("VIDEO_BOT_MEDIA_TIMEOUT_SECONDS", 1800),
            cookie_file=cookie_file,
            ffmpeg_executable=os.getenv("FFMPEG_EXECUTABLE", "").strip() or "ffmpeg",
        )
