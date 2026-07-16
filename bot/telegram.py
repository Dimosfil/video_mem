from __future__ import annotations

import threading
from pathlib import Path
from typing import Callable

import requests


class TelegramApiError(RuntimeError):
    pass


class TelegramClient:
    def __init__(self, token: str, session: requests.Session | None = None):
        self._base_url = f"https://api.telegram.org/bot{token}"
        self._session = session or requests.Session()

    def call(self, method: str, payload: dict[str, object], timeout: int = 30):
        response = self._session.post(
            f"{self._base_url}/{method}", json=payload, timeout=timeout
        )
        body = response.json()
        if not response.ok or not body.get("ok"):
            raise TelegramApiError(body.get("description") or f"Telegram {method} failed.")
        return body.get("result")

    def send_message(
        self,
        chat_id: str,
        text: str,
        *,
        reply_markup: dict[str, object] | None = None,
    ) -> None:
        payload: dict[str, object] = {
            "chat_id": chat_id,
            "text": text,
            "disable_web_page_preview": True,
        }
        if reply_markup:
            payload["reply_markup"] = reply_markup
        self.call("sendMessage", payload)

    def send_video(self, chat_id: str, video_path: Path, caption: str) -> None:
        with video_path.open("rb") as video:
            response = self._session.post(
                f"{self._base_url}/sendVideo",
                data={
                    "chat_id": chat_id,
                    "caption": caption,
                    "supports_streaming": "true",
                },
                files={"video": (video_path.name, video, "video/mp4")},
                timeout=300,
            )
        body = response.json()
        if not response.ok or not body.get("ok"):
            raise TelegramApiError(body.get("description") or "Telegram sendVideo failed.")

    def get_updates(self, offset: int, stop_event: threading.Event) -> list[dict]:
        if stop_event.is_set():
            return []
        result = self.call(
            "getUpdates",
            {
                "offset": offset,
                "timeout": 20,
                "allowed_updates": ["message", "callback_query"],
            },
            timeout=25,
        )
        return result if isinstance(result, list) else []

    def answer_callback(self, callback_query_id: str) -> None:
        self.call("answerCallbackQuery", {"callback_query_id": callback_query_id})


class TelegramPollingGateway:
    def __init__(
        self,
        client: TelegramClient,
        handler: Callable[[str, str, str, str | None], None],
        *,
        retry_seconds: float = 3.0,
    ):
        self.client = client
        self.handler = handler
        self.retry_seconds = retry_seconds
        self._offset = 0
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self.last_error: str | None = None

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._thread = threading.Thread(target=self._loop, name="telegram-polling", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=3)

    def _loop(self) -> None:
        while not self._stop.is_set():
            try:
                for update in self.client.get_updates(self._offset, self._stop):
                    update_id = int(update.get("update_id", 0))
                    self._offset = max(self._offset, update_id + 1)
                    self._handle_update(update)
                self.last_error = None
            except Exception as exc:
                self.last_error = str(exc)
                self._stop.wait(self.retry_seconds)

    def _handle_update(self, update: dict) -> None:
        callback = update.get("callback_query")
        if isinstance(callback, dict):
            self._handle_callback(callback)
            return
        message = update.get("message")
        if not isinstance(message, dict) or not isinstance(message.get("text"), str):
            return
        chat = message.get("chat") or {}
        if chat.get("type") not in {None, "private"}:
            return
        sender = message.get("from") or {}
        chat_id = str(chat.get("id", ""))
        user_id = str(sender.get("id") or chat_id)
        username = sender.get("username") or sender.get("first_name")
        if chat_id:
            self.handler(chat_id, user_id, message["text"], str(username) if username else None)

    def _handle_callback(self, callback: dict) -> None:
        callback_id = str(callback.get("id") or "")
        message = callback.get("message") or {}
        chat = message.get("chat") or {}
        sender = callback.get("from") or {}
        data = callback.get("data")
        if not callback_id or not isinstance(data, str) or chat.get("type") not in {None, "private"}:
            return
        try:
            chat_id = str(chat.get("id", ""))
            user_id = str(sender.get("id") or chat_id)
            username = sender.get("username") or sender.get("first_name")
            if chat_id:
                self.handler(chat_id, user_id, data, str(username) if username else None)
        finally:
            self.client.answer_callback(callback_id)
