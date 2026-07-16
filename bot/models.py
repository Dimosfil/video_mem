from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Literal


JobStatus = Literal[
    "awaiting_start",
    "awaiting_end",
    "confirming",
    "queued",
    "processing",
    "sending",
    "done",
    "failed",
    "cancelled",
]


@dataclass
class VideoJob:
    id: str
    chat_id: str
    user_id: str
    url: str
    video_id: str
    title: str
    duration: float
    thumbnail: str | None
    status: JobStatus
    created_at: str
    updated_at: str
    start: float | None = None
    end: float | None = None
    output_path: str | None = None
    output_size: int | None = None
    error: str | None = None

    def to_record(self) -> dict[str, object]:
        return asdict(self)

    @classmethod
    def from_record(cls, record: dict[str, object]) -> "VideoJob":
        return cls(**record)  # type: ignore[arg-type]

    def to_public_dict(self) -> dict[str, object]:
        return {
            "id": self.id,
            "title": self.title,
            "duration": self.duration,
            "thumbnail": self.thumbnail,
            "status": self.status,
            "start": self.start,
            "end": self.end,
            "outputSize": self.output_size,
            "error": self.error,
            "createdAt": self.created_at,
            "updatedAt": self.updated_at,
        }
