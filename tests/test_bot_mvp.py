from __future__ import annotations

import tempfile
import unittest
from concurrent.futures import Future
from pathlib import Path

from bot.config import BotConfig
from bot.media import MediaInfo, extract_youtube_video_id
from bot.repository import JobOwnershipError, JobRepository
from bot.service import (
    CONFIRM_PREFIX,
    ClipValidationError,
    VideoClipService,
    parse_timestamp,
    validate_clip_range,
)
from bot.telegram import TelegramClient, TelegramPollingGateway


class RecordingExecutor:
    def __init__(self):
        self.calls: list[tuple] = []

    def submit(self, fn, *args):
        self.calls.append((fn, args))
        future = Future()
        future.set_result(None)
        return future


class FakeMedia:
    def inspect(self, url: str) -> MediaInfo:
        return MediaInfo("dQw4w9WgXcQ", "Demo video", 240.0, None)

    def create_clip(self, url: str, start: float, end: float, job_dir: Path) -> Path:
        output = job_dir / "clip.mp4"
        output.write_bytes(b"video")
        return output


class FakeTelegram:
    def __init__(self):
        self.messages: list[tuple[str, str, dict | None]] = []
        self.videos: list[tuple[str, Path, str]] = []

    def send_message(self, chat_id: str, text: str, *, reply_markup=None) -> None:
        self.messages.append((chat_id, text, reply_markup))

    def send_video(self, chat_id: str, video_path: Path, caption: str) -> None:
        self.videos.append((chat_id, video_path, caption))


class FakeResponse:
    ok = True

    @staticmethod
    def json():
        return {"ok": True, "result": {"message_id": 1}}


class FakeSession:
    def __init__(self):
        self.calls: list[tuple[str, dict]] = []

    def post(self, url: str, **kwargs):
        self.calls.append((url, kwargs))
        return FakeResponse()


class CallbackClient:
    def __init__(self):
        self.answered: list[str] = []

    def answer_callback(self, callback_id: str) -> None:
        self.answered.append(callback_id)


def make_config(root: Path) -> BotConfig:
    return BotConfig(
        telegram_token=None,
        telegram_polling_enabled=False,
        data_dir=root,
        max_clip_seconds=180,
        max_upload_bytes=50_000_000,
        workers=1,
        job_ttl_hours=24,
        media_timeout_seconds=60,
        cookie_file=None,
        ffmpeg_executable="ffmpeg",
    )


class YoutubeUrlTests(unittest.TestCase):
    def test_supported_youtube_url_shapes(self):
        expected = "dQw4w9WgXcQ"
        self.assertEqual(extract_youtube_video_id(f"https://youtu.be/{expected}"), expected)
        self.assertEqual(
            extract_youtube_video_id(f"https://www.youtube.com/watch?v={expected}"), expected
        )
        self.assertEqual(
            extract_youtube_video_id(f"https://www.youtube.com/shorts/{expected}"), expected
        )

    def test_non_youtube_url_is_rejected(self):
        with self.assertRaises(ValueError):
            extract_youtube_video_id("https://example.com/watch?v=dQw4w9WgXcQ")


class ClipRangeTests(unittest.TestCase):
    def test_timestamp_formats(self):
        self.assertEqual(parse_timestamp("75"), 75)
        self.assertEqual(parse_timestamp("01:15"), 75)
        self.assertEqual(parse_timestamp("1:01:15.5"), 3675.5)

    def test_invalid_timestamp_formats(self):
        for value in ["", "abc", "1:61", "1:60:00", "-1"]:
            with self.subTest(value=value):
                with self.assertRaises(ClipValidationError):
                    parse_timestamp(value)

    def test_valid_range(self):
        validate_clip_range(10, 30, 60, 180)

    def test_invalid_and_oversized_ranges(self):
        for start, end, duration in [(-1, 10, 300), (10, 10, 300), (20, 10, 300), (0, 181, 300), (0, 61, 60)]:
            with self.subTest(start=start, end=end):
                with self.assertRaises(ClipValidationError):
                    validate_clip_range(start, end, duration, 180)


class RepositoryTests(unittest.TestCase):
    def test_active_job_and_ownership(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            repository = JobRepository(Path(temp_dir))
            job = repository.create(
                chat_id="1",
                user_id="2",
                url="https://youtu.be/dQw4w9WgXcQ",
                media=MediaInfo("dQw4w9WgXcQ", "Demo", 60, None),
            )
            self.assertEqual(repository.find_active("1", "2").id, job.id)
            self.assertEqual(repository.load_owned(job.id, "1", "2").id, job.id)
            with self.assertRaises(JobOwnershipError):
                repository.load_owned(job.id, "1", "different-user")


class TelegramPayloadTests(unittest.TestCase):
    def test_callback_button_and_streaming_video_payloads(self):
        session = FakeSession()
        client = TelegramClient("fake-test-token", session=session)  # type: ignore[arg-type]
        markup = {
            "inline_keyboard": [
                [{"text": "Получить видео", "callback_data": "clip:confirm:job-id"}]
            ]
        }
        client.send_message("10", "Подтвердите", reply_markup=markup)
        client.answer_callback("callback-id")
        with tempfile.TemporaryDirectory() as temp_dir:
            video = Path(temp_dir) / "clip.mp4"
            video.write_bytes(b"video")
            client.send_video("10", video, "Готово")

        self.assertEqual(session.calls[0][1]["json"]["reply_markup"], markup)
        self.assertEqual(session.calls[1][1]["json"]["callback_query_id"], "callback-id")
        video_call = session.calls[2][1]
        self.assertEqual(video_call["data"]["supports_streaming"], "true")
        self.assertEqual(video_call["files"]["video"][2], "video/mp4")

    def test_polling_gateway_routes_and_answers_callback(self):
        client = CallbackClient()
        handled: list[tuple[str, str, str, str | None]] = []
        gateway = TelegramPollingGateway(client, lambda *args: handled.append(args))  # type: ignore[arg-type]
        gateway._handle_update(
            {
                "callback_query": {
                    "id": "callback-id",
                    "data": "clip:confirm:job-id",
                    "from": {"id": 20, "username": "demo"},
                    "message": {"chat": {"id": 10, "type": "private"}},
                }
            }
        )
        self.assertEqual(handled[0][:3], ("10", "20", "clip:confirm:job-id"))
        self.assertEqual(client.answered, ["callback-id"])


class TelegramWorkflowTests(unittest.TestCase):
    def test_chat_flow_queues_confirmed_clip(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            repository = JobRepository(root / "jobs")
            telegram = FakeTelegram()
            executor = RecordingExecutor()
            service = VideoClipService(
                make_config(root),
                repository,
                FakeMedia(),  # type: ignore[arg-type]
                telegram,  # type: ignore[arg-type]
                executor=executor,
            )
            service.handle_telegram_message("10", "20", "https://youtu.be/dQw4w9WgXcQ")
            job = repository.find_active("10", "20")
            self.assertEqual(job.status, "awaiting_start")

            service.handle_telegram_message("10", "20", "00:05")
            self.assertEqual(repository.load(job.id).status, "awaiting_end")
            service.handle_telegram_message("10", "20", "00:10")
            confirming = repository.load(job.id)
            self.assertEqual(confirming.status, "confirming")
            markup = telegram.messages[-1][2]
            callback_data = markup["inline_keyboard"][0][0]["callback_data"]
            self.assertEqual(callback_data, f"{CONFIRM_PREFIX}{job.id}")

            service.handle_telegram_message("10", "20", callback_data)
            self.assertEqual(repository.load(job.id).status, "queued")
            self.assertEqual(len(executor.calls), 1)

    def test_queued_job_is_processed_and_delivered(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            repository = JobRepository(root / "jobs")
            job = repository.create(
                chat_id="10",
                user_id="20",
                url="https://youtu.be/dQw4w9WgXcQ",
                media=MediaInfo("dQw4w9WgXcQ", "Demo", 60, None),
            )
            job.start = 5
            job.end = 10
            job.status = "queued"
            repository.save(job)
            telegram = FakeTelegram()
            service = VideoClipService(
                make_config(root),
                repository,
                FakeMedia(),  # type: ignore[arg-type]
                telegram,  # type: ignore[arg-type]
                executor=RecordingExecutor(),
            )
            service._process_job(job.id)
            completed = repository.load(job.id)
            self.assertEqual(completed.status, "done")
            self.assertEqual(completed.output_size, 5)
            self.assertEqual(len(telegram.videos), 1)


if __name__ == "__main__":
    unittest.main()
