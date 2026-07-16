from __future__ import annotations

import logging
import threading

from .config import BotConfig, ConfigError
from .media import YtDlpMediaService
from .repository import JobRepository
from .service import VideoClipService
from .telegram import TelegramClient, TelegramPollingGateway


def main() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        config = BotConfig.from_environment()
    except ConfigError as exc:
        raise SystemExit(str(exc)) from exc

    repository = JobRepository(config.data_dir / "jobs")
    repository.cleanup_expired(config.job_ttl_hours)
    cleanup_stop = threading.Event()

    def cleanup_loop() -> None:
        while not cleanup_stop.wait(3600):
            repository.cleanup_expired(config.job_ttl_hours)

    cleanup_thread = threading.Thread(
        target=cleanup_loop, name="video-job-cleanup", daemon=True
    )
    cleanup_thread.start()
    media = YtDlpMediaService(
        ffmpeg_executable=config.ffmpeg_executable,
        cookie_file=config.cookie_file,
        timeout_seconds=config.media_timeout_seconds,
        max_upload_bytes=config.max_upload_bytes,
    )
    telegram = TelegramClient(config.telegram_token) if config.telegram_token else None
    service = VideoClipService(config, repository, media, telegram)
    polling = (
        TelegramPollingGateway(telegram, service.handle_telegram_message)
        if telegram and config.telegram_polling_enabled
        else None
    )
    if not polling:
        cleanup_stop.set()
        logging.info(
            "Telegram polling is disabled. Set TELEGRAM_POLLING_ENABLED=true and TELEGRAM_BOT_TOKEN."
        )
        return
    polling.start()
    logging.info("Telegram video clip bot polling started.")
    run_stop = threading.Event()
    try:
        while not run_stop.wait(1):
            pass
    except KeyboardInterrupt:
        pass
    finally:
        cleanup_stop.set()
        polling.stop()


if __name__ == "__main__":
    main()
