# VMS — Корпоративная система видеонаблюдения

Enterprise-грейд Video Management System на C#/.NET: ONVIF/RTSP-захват с GPU-декодированием,
неизменяемый (write-protected) архив с автоматическим retention, офлайн ИИ-детекция объектов
(YOLO) с поиском через Elasticsearch, REST API с ролевой моделью доступа (RBAC) и WPF-клиент
с живой сеткой камер.

Спроектирована с расчётом на 500+ камер, но текущая реализация — рабочий MVP,
проверенный на **одной реальной ONVIF-камере**.

## Статус

Все 6 этапов реализованы и проверены вживую (не моками) на реальной камере, реальном
Elasticsearch/Postgres и реальном WPF-клиенте:

| Этап | Модуль | Что делает |
|------|--------|------------|
| M1 | `VMS.Core` | Домен, RBAC (`AccessControlManager`), аутентификация (BCrypt), append-only аудит-лог, EF Core / PostgreSQL |
| M2 | `VMS.MediaEngine` | ONVIF discovery (WS-Security), запись RTSP в сегменты через ffmpeg, авто-восстановление при обрыве связи |
| M3 | `VMS.StorageEngine` | Retention (автоудаление просроченных записей), защита от удаления через Windows ACL, вырезание клипов |
| M4 | `VMS.MetadataIndexer` | Офлайн-детекция объектов (YOLOv8 / ONNX Runtime), индексация и поиск в Elasticsearch |
| M5 | `VMS.Backend.Server` | ASP.NET Core Web API (JWT), оркестрация камер из БД, REST-эндпоинты |
| M6 | `VMS.Frontend.WPF` | Desktop-клиент: логин, живая сетка камер, ИИ-поиск, попап с клипом |

## Архитектура

```
VMS.Core/              — домен, RBAC, аутентификация, EF Core DbContext
VMS.MediaEngine/        — ONVIF-клиент, запись потоков, снапшоты для ИИ
VMS.StorageEngine/      — retention, immutable-архив, вырезание клипов
VMS.MetadataIndexer/    — YOLO-детекция, индексация/поиск в Elasticsearch
VMS.Backend.Server/     — Worker Service + REST API (ASP.NET Core Minimal API)
VMS.Frontend.WPF/       — WPF-клиент (MVVM, LibVLCSharp)
VMS.Core.Tests/         — юнит-тесты (RBAC, аутентификация)
docker-compose.yml      — PostgreSQL + Elasticsearch для разработки
scripts/setup-tools.ps1 — разовая загрузка ffmpeg и экспорт YOLO-модели в ONNX
```

### Ключевые архитектурные решения

- **Захват видео**: супервизируемый процесс `ffmpeg` на камеру (не `FFmpeg.AutoGen`) — изоляция
  процессов даёт «zero-crash»: падение одной камеры не влияет на другие.
- **Живой просмотр**: `LibVLCSharp` — аппаратное декодирование «из коробки» (в т.ч. NVDEC/D3D11VA).
- **ONVIF**: собственный SOAP-клиент (WS-Security UsernameToken), т.к. готовые NuGet-пакеты
  по большей части заброшены.
- **Формат архива**: fragmented MP4 (`-movflags +frag_keyframe+empty_moov+...`) — без этого
  активно пишущийся сегмент нельзя прочитать (`moov atom not found`).
- **ИИ**: YOLOv8 через `YoloDotNet` (ONNX Runtime), полностью офлайн в момент инференса.
  Цвет автомобиля («red car») определяется отдельно — усреднением пикселей внутри bbox,
  т.к. в COCO нет классов цвета.
- **Аутентификация клипов**: LibVLC не умеет передавать `Authorization`-заголовок, поэтому
  эндпоинт `/api/clips/{file}` дополнительно принимает JWT через `?access_token=`
  (тот же паттерн, что и SignalR для WebSocket).

## Требования

- .NET SDK 10
- Docker (для PostgreSQL и Elasticsearch)
- Python 3.11+ с `pip` (для разового экспорта YOLO-модели в ONNX)
- Windows (WPF-клиент, Windows ACL для immutable-архива)
- NVIDIA GPU — опционально, для аппаратного декодирования/CUDA

## Быстрый старт

```powershell
# 1. Поднять инфраструктуру
docker compose up -d

# 2. Один раз скачать ffmpeg и экспортировать YOLO-модель
.\scripts\setup-tools.ps1

# 3. Применить миграции БД
dotnet ef database update --project VMS.Core --startup-project VMS.Core

# 4. Настроить тестовую камеру (гитигнорится, не коммитится)
#    Создать VMS.Backend.Server/appsettings.local.json:
#    {
#      "Vms": {
#        "TestCamera": {
#          "CameraId": "cam-01",
#          "IpAddress": "192.168.1.50",
#          "OnvifPort": 80,
#          "Username": "admin",
#          "Password": "..."
#        }
#      }
#    }

# 5. Запустить сервер
dotnet run --project VMS.Backend.Server --urls http://localhost:5080

# 6. Запустить WPF-клиент
dotnet run --project VMS.Frontend.WPF
```

При первом запуске сервер сам создаст пользователя `superadmin` и выведет
одноразовый пароль в лог — им и логинимся в WPF-клиенте.

## REST API

Все эндпоинты (кроме `/api/auth/login`) требуют JWT (`Authorization: Bearer ...`).

| Метод | Путь | Права | Описание |
|---|---|---|---|
| POST | `/api/auth/login` | — | Логин, выдаёт JWT |
| GET | `/api/cameras` | Viewer+ | Список камер |
| POST | `/api/cameras` | Admin+ | Добавить камеру (сразу подключает) |
| DELETE | `/api/cameras/{code}` | Admin+ | Удалить камеру |
| GET | `/api/cameras/{code}/status` | Viewer+ | Статус пайплайна |
| GET | `/api/search?q=...` | Viewer+ | Поиск по детекциям («red car», «person») |
| POST | `/api/clips` | Operator+ | Вырезать клип по детекции |
| GET | `/api/clips/{file}` | Operator+ | Скачать/проиграть клип |
| POST | `/api/archive/protect` | Admin+ | Защитить файл от удаления |
| DELETE | `/api/archive` | Admin+ / SuperAdmin (для защищённых) | Удалить файл архива |

## Роли (RBAC)

`Viewer < Operator < Admin < SuperAdmin` — единая логика (`AccessControlManager`) используется
и на сервере, и в WPF-клиенте (для скрытия недоступных элементов UI), так что поведение
никогда не расходится.

- **Viewer** — просмотр, поиск
- **Operator** — + вырезание клипов
- **Admin** — + управление камерами, удаление обычных файлов архива
- **SuperAdmin** — + удаление защищённых (immutable) файлов архива

## Известные ограничения текущего MVP

- CUDA execution provider для YOLO не установлен (сеть не позволила докачать ~600 МБ
  NVIDIA-рантаймов) — детекция работает на CPU. Код готов переключиться на GPU одной строкой.
- Нет отдельного API для управления пользователями — создание идёт напрямую в БД.
- Пароли ONVIF-камер хранятся в БД в открытом виде (требует DPAPI/secret-store перед продакшеном).
- Нет thumbnail-изображений в результатах поиска.
- Проверено на 1 камере — компоненты спроектированы для многокамерности
  (process-per-camera, конфигурируемые retention-политики на камеру), но нагрузочного
  тестирования на десятках/сотнях камер не проводилось.

## Тесты

```powershell
dotnet test VMS.Core.Tests
```
