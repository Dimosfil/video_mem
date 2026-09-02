# YouTube Viewer

Отдельное нативное Windows-приложение на .NET 8, WPF и Microsoft Edge WebView2 для просмотра YouTube.

В нём нет `yt-dlp`, выбора качества или функции скачивания. Загрузки в WebView явно отключены.

Весь WebView2-трафик принудительно направляется через локальный HTTP-прокси Happ `http://127.0.0.1:10809`. QUIC отключён, чтобы браузерный движок не создавал прямой UDP-маршрут. Если Happ не запущен или порт недоступен, просмотрщик показывает ошибку и не открывает YouTube напрямую.

## Возможности

- Настоящие вкладки с добавлением, закрытием и восстановлением.
- Кнопки «Назад», «Вперёд», «Обновить» и «Домой».
- Адресная строка: URL открываются напрямую, обычный текст ищется на YouTube.
- Ссылки, запрашивающие новое окно, открываются в новой вкладке.
- Индикатор загрузки и полноэкранный режим.
- Контекстное меню и масштабирование WebView2 сохранены; скачивания отключены.

Горячие клавиши:

| Клавиши | Действие |
| --- | --- |
| `F5`, `Ctrl+R` | Обновить страницу |
| `Ctrl+Shift+R` | Очистить браузерный кэш и обновить |
| `Alt+←`, `Alt+→` | Назад и вперёд |
| `Ctrl+T`, `Ctrl+W` | Открыть и закрыть вкладку |
| `Ctrl+Shift+T` | Вернуть закрытую вкладку |
| `Ctrl+Tab`, `Ctrl+Shift+Tab` | Переключить вкладку |
| `Ctrl+L` | Перейти в адресную строку |
| `Esc` | Остановить загрузку |
| `F11` | Полноэкранный режим |

## Запуск

Из корня проекта:

```powershell
.\youtube_viewer\start.ps1
```

Первый запуск соберёт отдельный `YouTube Viewer.exe`. Для работы нужны .NET 8 Desktop Runtime и Microsoft Edge WebView2 Runtime. Обычный запуск не использует Python и не открывает консоль.

Сессия WebView хранится в постоянном каталоге `%LOCALAPPDATA%\VideoMem\YouTubeViewer`, вне `dist`. Пересборка, замена и повторная установка EXE этот каталог не удаляют, поэтому cookies и авторизация YouTube сохраняются. Для выхода из аккаунта или полного сброса профиля этот каталог нужно удалить отдельно вручную.

Если локальный HTTP-порт Happ изменён вручную, его можно переопределить перед запуском:

```powershell
$env:YOUTUBE_VIEWER_PROXY = "http://127.0.0.1:18080"
.\youtube_viewer\start.ps1
```

## Сборка

```powershell
.\youtube_viewer\build.ps1
```

Артефакт: `youtube_viewer/dist/YouTube Viewer/YouTube Viewer.exe`. Сборка имеет собственные имя процесса, Windows version resource, AppUserModelID и иконку. Восстановление NuGet выполняется по `packages.lock.json`. Папки `bin/`, `obj/`, `build/`, `dist/` и генерируемые иконки не попадают в Git.

## Проверка

```powershell
dotnet run --project .\youtube_viewer\tests\YouTubeViewer.Tests.csproj --configuration Release
dotnet build .\youtube_viewer\YouTubeViewer.csproj --configuration Release
```
