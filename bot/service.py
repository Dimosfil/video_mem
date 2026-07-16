from __future__ import annotations

import logging
import threading
from concurrent.futures import ThreadPoolExecutor

from .config import BotConfig
from .media import MediaError, YtDlpMediaService, extract_youtube_video_id
from .models import VideoJob
from .repository import JobOwnershipError, JobRepository
from .telegram import TelegramClient


logger = logging.getLogger(__name__)
CONFIRM_PREFIX = "clip:confirm:"
CANCEL_PREFIX = "clip:cancel:"


class ClipValidationError(ValueError):
    pass


def parse_timestamp(value: str) -> float:
    normalized = value.strip().replace(",", ".")
    if not normalized:
        raise ClipValidationError("Укажите таймкод, например 01:25 или 00:01:25.")
    parts = normalized.split(":")
    if len(parts) not in {1, 2, 3}:
        raise ClipValidationError("Формат таймкода: секунды, ММ:СС или ЧЧ:ММ:СС.")
    try:
        numbers = [float(part) for part in parts]
    except ValueError as exc:
        raise ClipValidationError("Таймкод должен содержать только числа и двоеточия.") from exc
    if any(number < 0 for number in numbers):
        raise ClipValidationError("Таймкод не может быть отрицательным.")
    if len(parts) >= 2 and numbers[-1] >= 60:
        raise ClipValidationError("Секунды в таймкоде должны быть меньше 60.")
    if len(parts) == 3 and numbers[-2] >= 60:
        raise ClipValidationError("Минуты в формате ЧЧ:ММ:СС должны быть меньше 60.")
    if len(parts) == 1:
        return numbers[0]
    if len(parts) == 2:
        return numbers[0] * 60 + numbers[1]
    return numbers[0] * 3600 + numbers[1] * 60 + numbers[2]


def validate_clip_range(start: float, end: float, duration: float, maximum: int) -> None:
    if start < 0 or end <= start:
        raise ClipValidationError("Конец фрагмента должен быть позже начала.")
    if end > duration + 0.25:
        raise ClipValidationError("Конец фрагмента выходит за длительность видео.")
    if end - start > maximum:
        raise ClipValidationError(f"Максимальная длина фрагмента — {maximum} секунд.")


class VideoClipService:
    def __init__(
        self,
        config: BotConfig,
        repository: JobRepository,
        media: YtDlpMediaService,
        telegram: TelegramClient | None,
        executor: ThreadPoolExecutor | None = None,
    ):
        self.config = config
        self.repository = repository
        self.media = media
        self.telegram = telegram
        self._request_lock = threading.Lock()
        self.executor = executor or ThreadPoolExecutor(
            max_workers=config.workers, thread_name_prefix="video-clip"
        )

    def handle_telegram_message(
        self, chat_id: str, user_id: str, text: str, username: str | None = None
    ) -> None:
        del username
        if not self.telegram:
            return
        normalized = text.strip()
        lowered = normalized.lower()
        if lowered.startswith("/start"):
            self.telegram.send_message(
                chat_id,
                "Пришлите ссылку на отдельное видео YouTube. Затем я попрошу "
                "таймкоды начала и конца и отправлю готовый MP4.",
            )
            return
        if lowered.startswith("/cancel"):
            self._cancel_active(chat_id, user_id)
            return
        if normalized.startswith(CONFIRM_PREFIX):
            self._confirm_job(chat_id, user_id, normalized.removeprefix(CONFIRM_PREFIX))
            return
        if normalized.startswith(CANCEL_PREFIX):
            self._cancel_job(chat_id, user_id, normalized.removeprefix(CANCEL_PREFIX))
            return

        try:
            extract_youtube_video_id(normalized)
        except ValueError:
            self._handle_selection_input(chat_id, user_id, normalized)
            return
        self._create_job(chat_id, user_id, normalized)

    def _create_job(self, chat_id: str, user_id: str, url: str) -> None:
        assert self.telegram is not None
        active = self.repository.find_active(chat_id, user_id)
        if active:
            active.status = "cancelled"
            self.repository.save(active)
        self.telegram.send_message(chat_id, "Получаю данные видео…")
        try:
            media_info = self.media.inspect(url)
            job = self.repository.create(
                chat_id=chat_id,
                user_id=user_id,
                url=url,
                media=media_info,
            )
            self.telegram.send_message(
                chat_id,
                f"{job.title}\nДлительность: {format_duration(job.duration)}\n\n"
                "Откуда вырезать? Пришлите начало в формате ММ:СС, например 01:20.",
            )
        except Exception as exc:
            logger.exception("Failed to prepare Telegram video job")
            self.telegram.send_message(chat_id, safe_user_error(exc))

    def _handle_selection_input(self, chat_id: str, user_id: str, text: str) -> None:
        assert self.telegram is not None
        job = self.repository.find_active(chat_id, user_id)
        if not job:
            self.telegram.send_message(chat_id, "Пришлите корректную ссылку на видео YouTube.")
            return
        try:
            timestamp = parse_timestamp(text)
            if timestamp > job.duration:
                raise ClipValidationError("Таймкод выходит за длительность видео.")
            if job.status == "awaiting_start":
                job.start = round(timestamp, 3)
                job.status = "awaiting_end"
                self.repository.save(job)
                self.telegram.send_message(
                    chat_id,
                    f"Начало: {format_duration(timestamp)}.\nТеперь пришлите конец фрагмента.",
                )
                return
            if job.status == "awaiting_end" and job.start is not None:
                validate_clip_range(
                    job.start, timestamp, job.duration, self.config.max_clip_seconds
                )
                job.end = round(timestamp, 3)
                job.status = "confirming"
                self.repository.save(job)
                self.telegram.send_message(
                    chat_id,
                    f"Вырезать {format_duration(job.start)}–{format_duration(job.end)} "
                    f"({format_duration(job.end - job.start)})?",
                    reply_markup={
                        "inline_keyboard": [
                            [
                                {
                                    "text": "Получить видео",
                                    "callback_data": f"{CONFIRM_PREFIX}{job.id}",
                                },
                                {
                                    "text": "Отмена",
                                    "callback_data": f"{CANCEL_PREFIX}{job.id}",
                                },
                            ]
                        ]
                    },
                )
                return
            self.telegram.send_message(chat_id, "Используйте кнопки подтверждения под сообщением.")
        except ClipValidationError as exc:
            self.telegram.send_message(chat_id, str(exc))

    def _confirm_job(self, chat_id: str, user_id: str, job_id: str) -> None:
        assert self.telegram is not None
        try:
            with self._request_lock:
                job = self.repository.load_owned(job_id, chat_id, user_id)
                if job.status != "confirming" or job.start is None or job.end is None:
                    raise ClipValidationError("Это задание уже изменилось или было запущено.")
                validate_clip_range(
                    job.start, job.end, job.duration, self.config.max_clip_seconds
                )
                job.status = "queued"
                job.error = None
                self.repository.save(job)
            self.telegram.send_message(chat_id, "Задание принято в обработку.")
            self.executor.submit(self._process_job, job.id)
        except (JobOwnershipError, ValueError, ClipValidationError) as exc:
            self.telegram.send_message(chat_id, safe_user_error(exc))

    def _cancel_job(self, chat_id: str, user_id: str, job_id: str) -> None:
        assert self.telegram is not None
        try:
            job = self.repository.load_owned(job_id, chat_id, user_id)
            if job.status in {"queued", "processing", "sending", "done"}:
                raise ClipValidationError("Обработку этого задания уже нельзя отменить.")
            job.status = "cancelled"
            self.repository.save(job)
            self.telegram.send_message(chat_id, "Отменено. Пришлите новую ссылку, когда будете готовы.")
        except (JobOwnershipError, ValueError, ClipValidationError) as exc:
            self.telegram.send_message(chat_id, safe_user_error(exc))

    def _cancel_active(self, chat_id: str, user_id: str) -> None:
        assert self.telegram is not None
        job = self.repository.find_active(chat_id, user_id)
        if not job:
            self.telegram.send_message(chat_id, "Нет активного выбора фрагмента.")
            return
        job.status = "cancelled"
        self.repository.save(job)
        self.telegram.send_message(chat_id, "Выбор фрагмента отменён.")

    def _process_job(self, job_id: str) -> None:
        job = self.repository.load(job_id)
        if job.start is None or job.end is None:
            return
        try:
            job.status = "processing"
            self.repository.save(job)
            if self.telegram:
                self.telegram.send_message(
                    job.chat_id, "Вырезаю фрагмент. Это может занять несколько минут…"
                )
            output = self.media.create_clip(
                job.url, job.start, job.end, self.repository.job_dir(job.id)
            )
            size = output.stat().st_size
            if size > self.config.max_upload_bytes:
                raise MediaError("Готовый ролик превышает лимит Telegram.")
            job.output_path = str(output)
            job.output_size = size
            job.status = "sending"
            self.repository.save(job)
            if self.telegram:
                self.telegram.send_video(
                    job.chat_id,
                    output,
                    f"{job.title}\n{format_duration(job.start)}–{format_duration(job.end)}",
                )
            job.status = "done"
            self.repository.save(job)
        except Exception as exc:
            logger.exception("Video clip job failed: %s", job.id)
            job.status = "failed"
            job.error = safe_user_error(exc)
            self.repository.save(job)
            if self.telegram:
                try:
                    self.telegram.send_message(job.chat_id, job.error)
                except Exception:
                    logger.exception("Failed to deliver Telegram job error")


def safe_user_error(error: Exception) -> str:
    if isinstance(error, (ClipValidationError, MediaError, ValueError)):
        return str(error)
    return "Не удалось обработать видео. Попробуйте другую ссылку или повторите позже."


def format_duration(seconds: float) -> str:
    total = max(0, int(round(seconds)))
    hours, remainder = divmod(total, 3600)
    minutes, secs = divmod(remainder, 60)
    return f"{hours:02d}:{minutes:02d}:{secs:02d}" if hours else f"{minutes:02d}:{secs:02d}"
