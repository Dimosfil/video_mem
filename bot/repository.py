from __future__ import annotations

import json
import shutil
import threading
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

from .media import MediaInfo
from .models import VideoJob


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


class JobNotFoundError(LookupError):
    pass


class JobOwnershipError(PermissionError):
    pass


class JobRepository:
    def __init__(self, root: Path):
        self.root = root.resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()

    def create(
        self,
        *,
        chat_id: str,
        user_id: str,
        url: str,
        media: MediaInfo,
    ) -> VideoJob:
        job_id = str(uuid.uuid4())
        now = utc_now()
        job = VideoJob(
            id=job_id,
            chat_id=chat_id,
            user_id=user_id,
            url=url,
            video_id=media.video_id,
            title=media.title,
            duration=media.duration,
            thumbnail=media.thumbnail,
            status="awaiting_start",
            created_at=now,
            updated_at=now,
        )
        with self._lock:
            self.job_dir(job_id).mkdir(parents=True, exist_ok=False)
            self.save(job)
        return job

    def load(self, job_id: str) -> VideoJob:
        path = self._record_path(job_id)
        with self._lock:
            if not path.is_file():
                raise JobNotFoundError("Job not found.")
            return VideoJob.from_record(json.loads(path.read_text(encoding="utf-8")))

    def load_owned(self, job_id: str, chat_id: str, user_id: str) -> VideoJob:
        job = self.load(job_id)
        if job.chat_id != chat_id or job.user_id != user_id:
            raise JobOwnershipError("Job belongs to another Telegram user.")
        return job

    def find_active(self, chat_id: str, user_id: str) -> VideoJob | None:
        active_statuses = {"awaiting_start", "awaiting_end", "confirming"}
        matches: list[VideoJob] = []
        for directory in self.root.iterdir():
            if not directory.is_dir():
                continue
            try:
                job = self.load(directory.name)
            except (ValueError, JobNotFoundError, json.JSONDecodeError):
                continue
            if job.chat_id == chat_id and job.user_id == user_id and job.status in active_statuses:
                matches.append(job)
        return max(matches, key=lambda item: item.updated_at) if matches else None

    def save(self, job: VideoJob) -> None:
        job.updated_at = utc_now()
        path = self._record_path(job.id)
        temp_path = path.with_suffix(".json.tmp")
        payload = json.dumps(job.to_record(), ensure_ascii=False, indent=2)
        with self._lock:
            path.parent.mkdir(parents=True, exist_ok=True)
            temp_path.write_text(payload, encoding="utf-8")
            temp_path.replace(path)

    def job_dir(self, job_id: str) -> Path:
        normalized = str(uuid.UUID(job_id))
        path = (self.root / normalized).resolve()
        if self.root not in path.parents:
            raise JobNotFoundError("Invalid job path.")
        return path

    def cleanup_expired(self, ttl_hours: int) -> int:
        cutoff = datetime.now(timezone.utc) - timedelta(hours=ttl_hours)
        removed = 0
        for directory in self.root.iterdir():
            if not directory.is_dir():
                continue
            try:
                job = self.load(directory.name)
                created = datetime.fromisoformat(job.created_at)
            except (ValueError, TypeError, JobNotFoundError, json.JSONDecodeError):
                continue
            if created < cutoff:
                resolved = directory.resolve()
                if self.root in resolved.parents:
                    shutil.rmtree(resolved)
                    removed += 1
        return removed

    def _record_path(self, job_id: str) -> Path:
        return self.job_dir(job_id) / "job.json"
