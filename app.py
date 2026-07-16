from __future__ import annotations

import queue
import shutil
import threading
import tkinter as tk
from dataclasses import dataclass
from pathlib import Path
from tkinter import filedialog, messagebox, scrolledtext, ttk

from yt_dlp import YoutubeDL


APP_TITLE = "YouTube Downloader"
PROJECT_ROOT = Path(__file__).resolve().parent
BEST_QUALITY_LABEL = "Лучшее доступное"
NO_COOKIES_LABEL = "Без cookies"
COOKIE_BROWSER_OPTIONS = {
    NO_COOKIES_LABEL: None,
    "Firefox (рекомендуется)": "firefox",
    "Файл cookies.txt": "file",
    "Edge": "edge",
    "Chrome": "chrome",
    "Brave": "brave",
}
CHROMIUM_BROWSERS = {"edge", "chrome", "brave"}


@dataclass(frozen=True)
class QualityOption:
    label: str
    selector: str


def is_compatible_video_format(fmt: dict, require_audio: bool) -> bool:
    if fmt.get("ext") != "mp4":
        return False
    vcodec = fmt.get("vcodec") or ""
    if not vcodec.startswith("avc1"):
        return False
    if require_audio:
        acodec = fmt.get("acodec") or ""
        return acodec.startswith("mp4a")
    return True


def compatible_selector(
    height: int | None = None,
    ffmpeg_available: bool = True,
) -> str:
    height_filter = f"[height<={height}]" if height else ""
    if not ffmpeg_available:
        return (
            f"b[ext=mp4][vcodec^=avc1][acodec^=mp4a]{height_filter}/"
            f"best[ext=mp4]{height_filter}/best{height_filter}"
        )
    return (
        f"bv*[ext=mp4][vcodec^=avc1]{height_filter}+ba[ext=m4a]/"
        f"b[ext=mp4][vcodec^=avc1][acodec^=mp4a]{height_filter}/"
        f"b[ext=mp4][vcodec^=avc1]{height_filter}/"
        f"best[ext=mp4]{height_filter}/best{height_filter}"
    )


def build_quality_options(info: dict, ffmpeg_available: bool) -> list[QualityOption]:
    """Build yt-dlp format selectors from discovered video formats."""
    options = [
        QualityOption(
            BEST_QUALITY_LABEL,
            compatible_selector(ffmpeg_available=ffmpeg_available),
        )
    ]

    heights: set[int] = set()
    for fmt in info.get("formats") or []:
        height = fmt.get("height")
        if not isinstance(height, int) or height <= 0:
            continue
        if not is_compatible_video_format(fmt, require_audio=not ffmpeg_available):
            continue
        heights.add(height)

    for height in sorted(heights, reverse=True):
        options.append(
            QualityOption(
                f"{height}p",
                compatible_selector(height, ffmpeg_available=ffmpeg_available),
            )
        )

    return options


def cookies_from_browser(
    browser_label: str,
    profile: str = "",
) -> tuple[str, str | None, None, None] | None:
    browser = COOKIE_BROWSER_OPTIONS.get(browser_label)
    if browser in (None, "file"):
        return None
    profile = profile.strip() or None
    return (browser, profile, None, None)


def cookiefile_from_selection(browser_label: str, cookie_file: str) -> str | None:
    if COOKIE_BROWSER_OPTIONS.get(browser_label) != "file":
        return None
    cookie_file = cookie_file.strip()
    return cookie_file or None


def filename_quality_label(option_label: str) -> str:
    if option_label == BEST_QUALITY_LABEL:
        return "best"
    return option_label.replace("/", "-").replace("\\", "-").strip() or "quality"


def javascript_runtime_options() -> dict:
    node = shutil.which("node")
    if node:
        return {"node": {"path": node}}
    return {}


def default_download_directory() -> Path:
    return PROJECT_ROOT / "downloads"


def build_ydl_options(
    browser_label: str,
    profile: str = "",
    cookie_file: str = "",
) -> dict:
    """Build a plain yt-dlp options snapshot without accessing Tkinter state."""
    options: dict = {"quiet": True, "no_warnings": True, "noplaylist": True}
    js_runtimes = javascript_runtime_options()
    if js_runtimes:
        options["js_runtimes"] = js_runtimes

    cookies = cookies_from_browser(browser_label, profile)
    if cookies:
        options["cookiesfrombrowser"] = cookies

    cookiefile = cookiefile_from_selection(browser_label, cookie_file)
    if cookiefile:
        options["cookiefile"] = cookiefile
    elif COOKIE_BROWSER_OPTIONS.get(browser_label) == "file":
        raise ValueError("Выберите файл cookies.txt кнопкой `Файл...`.")
    return options


class DownloaderApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title(APP_TITLE)
        self.root.minsize(560, 390)

        self.messages: queue.Queue[tuple[str, object]] = queue.Queue()
        self.quality_options: list[QualityOption] = []
        self.formats_url: str | None = None
        self.formats_revision: int | None = None
        self.formats_ydl_options: dict | None = None
        self.formats_cookie_browser: str | None = None
        self.request_revision = 0
        self.download_dir = tk.StringVar(value=str(default_download_directory()))
        self.url_var = tk.StringVar()
        self.cookie_browser_var = tk.StringVar(value=NO_COOKIES_LABEL)
        self.cookie_file_var = tk.StringVar()
        self.cookie_profile_var = tk.StringVar()
        self.ffmpeg_available = shutil.which("ffmpeg") is not None

        self._build_ui()
        self.url_var.trace_add("write", self._on_request_settings_changed)
        self.cookie_browser_var.trace_add("write", self._on_request_settings_changed)
        self.cookie_file_var.trace_add("write", self._on_request_settings_changed)
        self.cookie_profile_var.trace_add("write", self._on_request_settings_changed)
        self._poll_messages()
        if not self.ffmpeg_available:
            self._append_status(
                "FFmpeg не найден. Приложение покажет качества, доступные одним файлом."
            )

    def _build_ui(self) -> None:
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)

        frame = ttk.Frame(self.root, padding=18)
        frame.grid(row=0, column=0, sticky="nsew")
        frame.columnconfigure(0, weight=1)

        title = ttk.Label(frame, text=APP_TITLE, font=("Segoe UI", 18, "bold"))
        title.grid(row=0, column=0, columnspan=3, sticky="w", pady=(0, 14))

        ttk.Label(frame, text="Ссылка").grid(row=1, column=0, columnspan=3, sticky="w")
        url_entry = ttk.Entry(frame, textvariable=self.url_var)
        url_entry.grid(row=2, column=0, columnspan=2, sticky="ew", pady=(4, 10))
        url_entry.focus()

        self.fetch_button = ttk.Button(
            frame, text="Показать качества", command=self.on_fetch_formats
        )
        self.fetch_button.grid(row=2, column=2, sticky="ew", padx=(10, 0), pady=(4, 10))

        ttk.Label(frame, text="Качество").grid(row=3, column=0, sticky="w")
        self.quality_box = ttk.Combobox(frame, state="disabled", values=[])
        self.quality_box.grid(row=4, column=0, sticky="ew", pady=(4, 10))

        ttk.Label(frame, text="Cookies").grid(row=3, column=1, sticky="w", padx=(10, 0))
        self.cookie_box = ttk.Combobox(
            frame,
            state="readonly",
            textvariable=self.cookie_browser_var,
            values=list(COOKIE_BROWSER_OPTIONS.keys()),
        )
        self.cookie_box.grid(row=4, column=1, sticky="ew", padx=(10, 0), pady=(4, 10))
        self.cookie_file_button = ttk.Button(frame, text="Файл...", command=self.on_choose_cookie_file)
        self.cookie_file_button.grid(row=4, column=2, sticky="ew", padx=(10, 0), pady=(4, 10))

        ttk.Label(frame, text="Профиль Chrome/браузера").grid(
            row=5, column=0, columnspan=3, sticky="w"
        )
        self.cookie_profile_entry = ttk.Entry(frame, textvariable=self.cookie_profile_var)
        self.cookie_profile_entry.grid(row=6, column=0, columnspan=3, sticky="ew", pady=(4, 10))

        ttk.Label(frame, text="Папка").grid(row=7, column=0, columnspan=3, sticky="w")
        folder_entry = ttk.Entry(frame, textvariable=self.download_dir)
        folder_entry.grid(row=8, column=0, columnspan=2, sticky="ew", pady=(4, 10))
        ttk.Button(frame, text="Выбрать", command=self.on_choose_folder).grid(
            row=8, column=2, sticky="ew", padx=(10, 0), pady=(4, 10)
        )

        self.download_button = ttk.Button(
            frame, text="Скачать", command=self.on_download, state="disabled"
        )
        self.download_button.grid(row=9, column=0, columnspan=3, sticky="ew", pady=(6, 12))

        self.progress = ttk.Progressbar(frame, mode="determinate", maximum=100)
        self.progress.grid(row=10, column=0, columnspan=3, sticky="ew")

        self.log = scrolledtext.ScrolledText(frame, height=7, wrap="word", state="disabled")
        self.log.grid(row=11, column=0, columnspan=3, sticky="nsew", pady=(12, 0))
        frame.rowconfigure(11, weight=1)

    def on_choose_folder(self) -> None:
        folder = filedialog.askdirectory(initialdir=self.download_dir.get())
        if folder:
            self.download_dir.set(folder)

    def on_choose_cookie_file(self) -> None:
        filename = filedialog.askopenfilename(
            title="Выберите cookies.txt",
            filetypes=(("Cookies files", "*.txt"), ("All files", "*.*")),
        )
        if filename:
            self.cookie_browser_var.set("Файл cookies.txt")
            self.cookie_file_var.set(filename)
            self._append_status(f"Cookies file: {filename}")

    def on_fetch_formats(self) -> None:
        url = self.url_var.get().strip()
        if not self._validate_url(url):
            return

        try:
            ydl_options, selected_browser = self._request_options_snapshot()
        except ValueError as exc:
            messagebox.showwarning(APP_TITLE, str(exc))
            return

        self._clear_quality_state()
        request_revision = self.request_revision
        self._set_busy(True)
        self.progress["value"] = 0
        self._append_status("Получаю список доступных качеств...")
        threading.Thread(
            target=self._fetch_formats_worker,
            args=(url, request_revision, ydl_options, selected_browser),
            daemon=True,
        ).start()

    def on_download(self) -> None:
        url = self.url_var.get().strip()
        if not self._validate_url(url):
            return
        if (
            self.formats_url != url
            or self.formats_revision != self.request_revision
            or self.formats_ydl_options is None
        ):
            messagebox.showwarning(
                APP_TITLE,
                "Ссылка или настройки cookies изменились. Снова покажите доступные качества.",
            )
            return

        option = self._selected_quality()
        if option is None:
            messagebox.showwarning(APP_TITLE, "Сначала выберите качество.")
            return

        folder = Path(self.download_dir.get()).expanduser()
        self._set_busy(True)
        self.progress["value"] = 0
        self._append_status(f"Начинаю скачивание: {option.label}")
        threading.Thread(
            target=self._download_worker,
            args=(
                url,
                option,
                folder,
                dict(self.formats_ydl_options),
                self.formats_cookie_browser,
                self.ffmpeg_available,
            ),
            daemon=True,
        ).start()

    def _fetch_formats_worker(
        self,
        url: str,
        request_revision: int,
        ydl_options: dict,
        selected_browser: str | None,
    ) -> None:
        try:
            with YoutubeDL(ydl_options) as ydl:
                info = ydl.extract_info(url, download=False)

            if info.get("is_live"):
                raise RuntimeError("Прямые трансляции сейчас не поддерживаются.")

            options = build_quality_options(info, self.ffmpeg_available)
            title = info.get("title") or "Видео"
            self.messages.put(
                (
                    "formats",
                    (url, request_revision, title, options, ydl_options, selected_browser),
                )
            )
        except Exception as exc:  # yt-dlp raises several user-facing exception types.
            self.messages.put(
                (
                    "error",
                    f"Не удалось получить качества: {self._friendly_error(exc, selected_browser)}",
                )
            )

    def _download_worker(
        self,
        url: str,
        option: QualityOption,
        folder: Path,
        ydl_options: dict,
        selected_browser: str | None,
        ffmpeg_available: bool,
    ) -> None:
        try:
            folder.mkdir(parents=True, exist_ok=True)

            def progress_hook(data: dict) -> None:
                status = data.get("status")
                if status == "downloading":
                    downloaded = data.get("downloaded_bytes") or 0
                    total = data.get("total_bytes") or data.get("total_bytes_estimate")
                    if total:
                        percent = max(0, min(100, downloaded / total * 100))
                        self.messages.put(("progress", percent))
                        self.messages.put(("status", f"Скачиваю... {percent:.1f}%"))
                    else:
                        self.messages.put(("status", "Скачиваю..."))
                elif status == "finished":
                    self.messages.put(("status", "Файл загружен, завершаю обработку..."))

            ydl_options.update(
                {
                    "format": option.selector,
                    "outtmpl": str(
                        folder / f"%(title).200B [{filename_quality_label(option.label)}].%(ext)s"
                    ),
                    "progress_hooks": [progress_hook],
                    "windowsfilenames": True,
                }
            )
            if ffmpeg_available:
                ydl_options["merge_output_format"] = "mp4"

            with YoutubeDL(ydl_options) as ydl:
                ydl.download([url])

            self.messages.put(("done", f"Готово. Файл сохранён в: {folder}"))
        except Exception as exc:
            self.messages.put(
                (
                    "error",
                    f"Не удалось скачать видео: {self._friendly_error(exc, selected_browser)}",
                )
            )

    def _selected_quality(self) -> QualityOption | None:
        index = self.quality_box.current()
        if index < 0 or index >= len(self.quality_options):
            return None
        return self.quality_options[index]

    def _validate_url(self, url: str) -> bool:
        if not url:
            messagebox.showwarning(APP_TITLE, "Вставьте ссылку на YouTube.")
            return False
        if not url.startswith(("http://", "https://")):
            messagebox.showwarning(APP_TITLE, "Ссылка должна начинаться с http:// или https://.")
            return False
        return True

    def _request_options_snapshot(self) -> tuple[dict, str | None]:
        browser_label = self.cookie_browser_var.get()
        return (
            build_ydl_options(
                browser_label,
                self.cookie_profile_var.get(),
                self.cookie_file_var.get(),
            ),
            COOKIE_BROWSER_OPTIONS.get(browser_label),
        )

    def _friendly_error(self, exc: Exception, selected_browser: str | None) -> str:
        text = str(exc)
        if "Could not copy" in text and "cookie database" in text:
            return (
                f"{text}\n"
                "Chrome/Edge/Brave держит файл cookies или не даёт его скопировать. "
                "Закройте выбранный браузер полностью и повторите. Если вход в YouTube сделан не в основном профиле, "
                "укажите профиль, например `Default` или `Profile 1`. Если не поможет, нажмите `Файл...` "
                "и выберите экспортированный cookies.txt."
            )
        if "Failed to decrypt with DPAPI" in text:
            return (
                f"{text}\n"
                "Chromium/Edge не отдал cookies из-за защиты Windows. "
                "Попробуйте Firefox или выберите `Файл cookies.txt` и укажите экспортированный cookies-файл."
            )
        if "Sign in to confirm" in text:
            if selected_browser in CHROMIUM_BROWSERS:
                return (
                    f"{text}\n"
                    "Для Chrome/Edge/Brave на Windows чаще всего нужен закрытый браузер или файл cookies.txt. "
                    "Нажмите `Файл...` для выбора cookies-файла или попробуйте Firefox."
                )
            return (
                f"{text}\n"
                "Выберите Firefox, браузер с активным входом в YouTube, или `Файл cookies.txt`."
            )
        return text

    def _poll_messages(self) -> None:
        try:
            while True:
                kind, payload = self.messages.get_nowait()
                if kind == "formats":
                    (
                        requested_url,
                        request_revision,
                        title,
                        options,
                        ydl_options,
                        selected_browser,
                    ) = payload  # type: ignore[misc]
                    if (
                        requested_url != self.url_var.get().strip()
                        or request_revision != self.request_revision
                    ):
                        self._append_status(
                            "Ссылка или настройки cookies изменились во время запроса. "
                            "Запросите качества снова."
                        )
                        self._set_busy(False)
                        continue
                    self.quality_options = list(options)
                    self.formats_url = requested_url
                    self.formats_revision = request_revision
                    self.formats_ydl_options = dict(ydl_options)
                    self.formats_cookie_browser = selected_browser
                    self.quality_box.config(
                        state="readonly", values=[option.label for option in self.quality_options]
                    )
                    self.quality_box.current(0)
                    self.download_button.config(state="normal")
                    self._append_status(f"Найдено видео: {title}")
                    self._set_busy(False)
                elif kind == "progress":
                    self.progress["value"] = float(payload)
                elif kind == "status":
                    self._append_status(str(payload))
                elif kind == "done":
                    self.progress["value"] = 100
                    self._append_status(str(payload))
                    self._set_busy(False)
                elif kind == "error":
                    self._append_status(str(payload))
                    self._set_busy(False)
        except queue.Empty:
            pass
        self.root.after(150, self._poll_messages)

    def _append_status(self, text: str) -> None:
        self.log.config(state="normal")
        self.log.insert("end", text + "\n")
        self.log.see("end")
        self.log.config(state="disabled")

    def _set_busy(self, busy: bool) -> None:
        state = "disabled" if busy else "normal"
        self.fetch_button.config(state=state)
        if busy:
            self.download_button.config(state="disabled")
        elif self.quality_options and self.formats_url == self.url_var.get().strip():
            self.download_button.config(state="normal")

    def _on_request_settings_changed(self, *_args: object) -> None:
        self.request_revision += 1
        self._clear_quality_state()

    def _clear_quality_state(self) -> None:
        self.quality_options = []
        self.formats_url = None
        self.formats_revision = None
        self.formats_ydl_options = None
        self.formats_cookie_browser = None
        self.quality_box.config(state="disabled", values=[])
        self.quality_box.set("")
        self.download_button.config(state="disabled")


def main() -> None:
    root = tk.Tk()
    style = ttk.Style(root)
    if "vista" in style.theme_names():
        style.theme_use("vista")
    DownloaderApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
