import unittest

from app import (
    NO_COOKIES_LABEL,
    PROJECT_ROOT,
    build_quality_options,
    build_ydl_options,
    cookiefile_from_selection,
    cookies_from_browser,
    default_download_directory,
    filename_quality_label,
    is_compatible_video_format,
    javascript_runtime_options,
)


class QualityOptionsTest(unittest.TestCase):
    def test_ffmpeg_mode_lists_video_only_heights(self):
        info = {
            "formats": [
                {"height": 1080, "vcodec": "av01", "acodec": "none"},
                {"height": 720, "ext": "mp4", "vcodec": "avc1", "acodec": "mp4a"},
                {"height": None, "vcodec": "none", "acodec": "mp4a"},
            ]
        }

        options = build_quality_options(info, ffmpeg_available=True)

        self.assertEqual([option.label for option in options], ["Лучшее доступное", "720p"])
        self.assertIn("vcodec^=avc1", options[1].selector)
        self.assertIn("ba[ext=m4a]", options[1].selector)
        self.assertIn("+", options[0].selector)

    def test_without_ffmpeg_lists_progressive_heights_only(self):
        info = {
            "formats": [
                {"height": 1080, "vcodec": "av01", "acodec": "none"},
                {"height": 720, "ext": "mp4", "vcodec": "avc1", "acodec": "mp4a"},
            ]
        }

        options = build_quality_options(info, ffmpeg_available=False)

        self.assertEqual([option.label for option in options], ["Лучшее доступное", "720p"])
        self.assertIn("best[ext=mp4]", options[0].selector)
        self.assertNotIn("+", options[0].selector)
        self.assertNotIn("+", options[1].selector)

    def test_compatible_video_format_requires_windows_friendly_codecs(self):
        self.assertTrue(
            is_compatible_video_format(
                {"ext": "mp4", "vcodec": "avc1.640028", "acodec": "mp4a.40.2"},
                require_audio=True,
            )
        )
        self.assertFalse(
            is_compatible_video_format(
                {"ext": "mp4", "vcodec": "av01.0.08M.08", "acodec": "mp4a.40.2"},
                require_audio=True,
            )
        )
        self.assertFalse(
            is_compatible_video_format(
                {"ext": "mp4", "vcodec": "avc1.640028", "acodec": "opus"},
                require_audio=True,
            )
        )

    def test_cookie_browser_selection(self):
        self.assertEqual(cookies_from_browser("Firefox (рекомендуется)"), ("firefox", None, None, None))
        self.assertEqual(cookies_from_browser("Edge"), ("edge", None, None, None))
        self.assertEqual(cookies_from_browser("Chrome", "Profile 1"), ("chrome", "Profile 1", None, None))
        self.assertIsNone(cookies_from_browser("Без cookies"))
        self.assertIsNone(cookies_from_browser("Файл cookies.txt"))
        self.assertIsNone(cookies_from_browser("Unknown"))

    def test_cookie_file_selection(self):
        self.assertEqual(cookiefile_from_selection("Файл cookies.txt", "C:/tmp/cookies.txt"), "C:/tmp/cookies.txt")
        self.assertIsNone(cookiefile_from_selection("Файл cookies.txt", ""))
        self.assertIsNone(cookiefile_from_selection("Firefox", "C:/tmp/cookies.txt"))

    def test_ydl_options_default_to_public_video_without_cookies(self):
        options = build_ydl_options(NO_COOKIES_LABEL)

        self.assertTrue(options["noplaylist"])
        self.assertNotIn("cookiesfrombrowser", options)
        self.assertNotIn("cookiefile", options)

    def test_ydl_options_snapshot_contains_selected_cookie_source(self):
        browser_options = build_ydl_options("Chrome", "Profile 1")
        file_options = build_ydl_options("Файл cookies.txt", cookie_file="C:/tmp/cookies.txt")

        self.assertEqual(
            browser_options["cookiesfrombrowser"],
            ("chrome", "Profile 1", None, None),
        )
        self.assertEqual(file_options["cookiefile"], "C:/tmp/cookies.txt")

    def test_ydl_options_requires_selected_cookie_file(self):
        with self.assertRaisesRegex(ValueError, "cookies.txt"):
            build_ydl_options("Файл cookies.txt")

    def test_project_download_directory_is_stable(self):
        self.assertEqual(default_download_directory().parent, PROJECT_ROOT)
        self.assertEqual(default_download_directory().name, "downloads")

    def test_javascript_runtime_options_shape(self):
        options = javascript_runtime_options()
        self.assertIsInstance(options, dict)
        if options:
            self.assertIn("node", options)
            self.assertIn("path", options["node"])

    def test_filename_quality_label(self):
        self.assertEqual(filename_quality_label("Лучшее доступное"), "best")
        self.assertEqual(filename_quality_label("1080p"), "1080p")
        self.assertEqual(filename_quality_label("720/p"), "720-p")


if __name__ == "__main__":
    unittest.main()
