# MVP Windows-клиента для proxy-подключений

## 1. Статус документа

Этот документ фиксирует MVP собственного Windows-клиента для proxy-подключений поверх готовых core-решений.

Приоритет требований:

1. Явные решения владельца продукта из диалога.
2. Этот документ.
3. `client.md`.
4. Поведение и архитектурные идеи v2rayN как ориентир, но не как обязательный контракт.

Ключевое отличие от `client.md`: MVP не ограничивается одним PasarGuard subscription и не выносит маршрутизацию полностью на сервер. Для первой версии нужна локальная российская маршрутизация по умолчанию.

## 2. Источники и методология

### 2.1. STANDART

Методологическая база проекта: https://github.com/psewdon1m/STANDART.git

Используемые принципы STANDART:

- Structural Backbone: у проекта должна быть явная структура модулей, экранов, конфигов и этапов.
- Trace-Reversible Non-Destructive Workflow: изменения должны быть обратимыми; конфиги, правила и last-known-good состояния сохраняются.
- Absolute Transparency: решения, логи, статусы и причины ошибок должны быть видимы разработчику и поддержке.
- Non-Monolithic Modularity: UI, runtime, routing, storage, updater, installer и telemetry не смешиваются в один слой.
- Defined Terms First: термины фиксируются до реализации.
- Automate Everything: сборка, тесты, упаковка, обновление geo-файлов и проверка конфигов автоматизируются.
- Roadmap Checkpoints: работа идет этапами с условиями входа и выхода.
- Two-Way Verification: у каждого этапа есть check-in и check-out проверка.

### 2.2. v2rayN как ориентир

v2rayN используется как пример зрелого клиента и набора практик:

- работа с Xray core;
- импорт proxy-ссылок;
- локальный системный proxy;
- генерация Xray JSON;
- geoip/geosite assets;
- routing presets;
- диагностические логи;
- tray-поведение.

Код v2rayN не копируется без отдельного лицензионного решения. На момент подготовки документа v2rayN распространяется под GPL-3.0, поэтому для возможного будущего закрытого продукта безопаснее писать собственную реализацию и использовать v2rayN только как архитектурный ориентир.

## 3. Термины

MVP: первая рабочая версия клиента, которую можно установить обычному пользователю и использовать без ручной настройки Xray.

Core: внешний исполняемый сетевой движок. В MVP используется только Xray.

Profile: один proxy-профиль, полученный из `vless://` ссылки или subscription URL.

Subscription URL: URL, который возвращает список proxy-ссылок.

System proxy: настройки Windows Internet Settings, через которые приложения отправляют HTTP/HTTPS-трафик на локальный inbound клиента.

Local inbound: локальный порт Xray, например SOCKS/HTTP на `127.0.0.1`.

Routing preset: набор правил маршрутизации, скрытый от обычного пользователя за понятным режимом.

Russia routing: режим по умолчанию, где российские сервисы идут напрямую, реклама и торренты блокируются, остальное идет через proxy.

Rule assets: `geoip.dat`, `geosite.dat` и дополнительные локальные JSON-правила.

Ozon direct rule: отдельное правило для прямого доступа к Ozon, потому что общий geosite/geoip набор может быть недостаточным.

Last-known-good config: последняя успешно запущенная конфигурация Xray, к которой клиент может откатиться.

Telemetry: диагностические данные, которые пользователь разрешил отправлять на сервер поддержки.

## 4. Цель MVP

Создать минималистичный Windows-клиент, который закрывает основные сценарии обычного пользователя:

- установить приложение без отдельной установки .NET;
- добавить proxy через `vless://` ссылку или subscription URL;
- включить подключение одной большой кнопкой;
- автоматически включить корректную маршрутизацию для российского рынка;
- автоматически управлять системным proxy;
- показывать только понятный статус, а сложные настройки скрыть;
- дать поддержке диагностические данные без просьб присылать скриншоты логов.

## 5. Не входит в MVP

В первую версию не входят:

- QR import;
- TUN mode;
- sing-box;
- mihomo/clash;
- `vmess://`, `trojan://`, `ss://`;
- per-app routing;
- встроенная камера;
- сложный multi-account кабинет;
- полноценный self-care portal;
- автоматическое лечение всех сетевых проблем;
- мобильные и macOS/Linux версии.

Эти функции можно добавить после стабилизации Windows MVP.

## 6. Поддерживаемые сценарии

### 6.1. Первый запуск

1. Пользователь устанавливает приложение через installer.
2. При установке принимает лицензию и согласие на диагностические логи.
3. Открывается один главный экран.
4. Если в clipboard есть поддерживаемая `vless://` ссылка или subscription URL, клиент предлагает добавить ее.
5. Пользователь нажимает `Подключить`.
6. Клиент генерирует Xray config, запускает Xray, включает system proxy и показывает статус.

### 6.2. Добавление профиля

Поддерживаемые способы:

- вставить `vless://` ссылку из clipboard;
- вставить subscription URL;
- вставить текст вручную.

Subscription URL может содержать несколько `vless://` ссылок. Клиент должен распарсить список, показать понятные названия и выбрать рекомендуемый профиль.

### 6.3. Подключение

При подключении клиент:

1. Проверяет наличие активного профиля.
2. Обновляет rule assets, если они устарели.
3. Собирает Xray JSON.
4. Валидирует Xray JSON через dry-run или тестовый запуск.
5. Сохраняет candidate config.
6. Запускает `xray.exe`.
7. Проверяет локальный порт.
8. Включает system proxy.
9. Сохраняет config как last-known-good.
10. Показывает статус `Подключено`.

### 6.4. Отключение

При отключении клиент:

1. Выключает system proxy.
2. Останавливает Xray.
3. Сохраняет состояние UI.
4. Показывает статус `Отключено`.

Если Xray аварийно завершился, system proxy должен быть снят или переведен в безопасное состояние.

### 6.5. Диагностика

Обычный пользователь видит:

- статус подключения;
- текущий профиль;
- последнюю ошибку простым языком;
- кнопку `Отправить диагностику`.

Расширенные логи скрыты в настройках.

## 7. UX и дизайн

### 7.1. Дизайн-направление

Подход: спокойный utility-продукт для массового Windows-пользователя, а не техническая панель администратора.

Визуальная идея:

- светлая тема по умолчанию;
- нейтральная база Windows 11;
- акцентный цвет: спокойный синий или зеленый для состояния подключения;
- красный используется только для ошибок;
- без перегруженных таблиц на главном экране;
- без терминологии `inbound`, `outbound`, `domainStrategy`, `sniffing` на первом уровне UI;
- сложные настройки доступны, но не видны сразу.

Рекомендуемый стиль: Fluent-like WPF UI.

Возможные UI-библиотеки:

- `WPF-UI` для Windows 11-like интерфейса;
- либо `MaterialDesignThemes` если нужен быстрый и зрелый WPF стек.

Для этого продукта предпочтительнее `WPF-UI`: он ближе к ожиданиям Windows-пользователя и меньше напоминает инженерную панель.

### 7.2. Главный экран

На главном экране должны быть:

- крупный статус: `Защищено`, `Отключено`, `Ошибка`;
- одна основная кнопка: `Подключить` / `Отключить`;
- выбранный профиль;
- режим: `Умный режим для России`;
- компактная скорость/трафик за сессию;
- кнопка `Добавить профиль`;
- кнопка `Настройки`;
- tray-status.

Не должно быть:

- таблицы всех технических полей;
- десятков кнопок;
- raw JSON;
- списка routing rules;
- логов Xray.

### 7.3. Настройки

Настройки делятся на уровни:

- Основные:
  - system proxy on/off;
  - запускать вместе с Windows;
  - auto-connect;
  - обновлять правила автоматически.
- Профили:
  - список профилей;
  - добавить ссылку;
  - обновить subscription;
  - удалить;
  - переименовать.
- Маршрутизация:
  - `Умный режим для России`;
  - `Весь трафик через proxy`;
  - `Только локальный proxy без system proxy`;
  - advanced editor правил.
- DNS:
  - использовать настройки по умолчанию;
  - advanced DNS editor.
- Диагностика:
  - последний лог;
  - отправить диагностику;
  - экспорт zip с логами.
- Обновления:
  - core;
  - geo assets;
  - клиент.

## 8. Технологии

### 8.1. Платформа

- ОС: Windows 10/11.
- Runtime: .NET 8.
- UI: WPF.
- Язык: C#.
- Core: Xray only.
- Упаковка: installer с self-contained .NET runtime.

### 8.2. Рекомендуемые зависимости

UI:

- `WPF-UI` или `MaterialDesignThemes`;
- `H.NotifyIcon.Wpf` для tray;
- `CommunityToolkit.Mvvm` или `ReactiveUI`.

Runtime:

- `CliWrap` для запуска Xray;
- `System.Text.Json` для генерации Xray JSON;
- `Microsoft.Extensions.Hosting`;
- `Microsoft.Extensions.Logging`;
- `NLog` или `Serilog`.

Storage:

- SQLite;
- `sqlite-net-pcl` или `Microsoft.Data.Sqlite`.

Networking:

- `HttpClientFactory`;
- retry policy через `Polly`.

Installer:

- WiX Toolset или Velopack/Inno Setup.

Для MVP предпочтительно:

- `CommunityToolkit.Mvvm` вместо ReactiveUI, если команда хочет меньше магии;
- `Serilog` для структурированных логов и будущей отправки диагностики;
- `Microsoft.Data.Sqlite` для явного контроля схемы;
- WiX или Inno Setup для принятия лицензии и self-contained установки.

## 9. Архитектура

Рекомендуемая структура solution:

```text
Client.sln
├─ Client.App.Win              # WPF UI, tray, окна, view models
├─ Client.Core                 # доменные модели и use cases
├─ Client.Transport.Xray       # запуск xray.exe, генерация config, health checks
├─ Client.Routing              # routing presets, geo assets, ozon direct rules
├─ Client.Profiles             # vless parser, subscription parser
├─ Client.Storage              # SQLite, settings, last-known-good config
├─ Client.Telemetry            # локальная диагностика и отправка логов
├─ Client.Updater              # обновление клиента, core и rule assets
├─ Client.Installer            # installer scripts
└─ Client.Tests                # unit/integration tests
```

Правило модульности: UI не должен напрямую собирать Xray JSON, менять registry system proxy или парсить `vless://`. UI вызывает use cases.

## 10. Профили и импорт

### 10.1. MVP форматы

Поддерживаются:

- `vless://`;
- subscription URL со списком `vless://` ссылок.

Не поддерживаются в MVP:

- QR;
- Clash YAML;
- sing-box JSON;
- vmess/trojan/shadowsocks.

### 10.2. Парсер VLESS

Парсер должен извлекать:

- id/uuid;
- host;
- port;
- encryption;
- security;
- type/network;
- sni;
- fp/fingerprint;
- pbk/public key;
- sid/short id;
- flow;
- path;
- serviceName;
- remarks/name.

Парсер должен быть устойчив к:

- URL encoding;
- отсутствующим optional параметрам;
- русским именам;
- нескольким одинаковым именам;
- лишним query параметрам.

### 10.3. Subscription

Subscription parser должен:

- скачать URL;
- определить plain text или base64;
- разбить на строки;
- выбрать только поддерживаемые `vless://`;
- сохранить source URL;
- сохранять last refresh time;
- не удалять старые рабочие профили, если новый refresh сломался.

## 11. Routing MVP

### 11.1. Режим по умолчанию

По умолчанию включен режим `Умный режим для России`.

Логика:

1. Российские сервисы идут напрямую.
2. Приватные сети идут напрямую.
3. Ozon идет напрямую через отдельное JSON-правило.
4. Реклама блокируется.
5. Торренты блокируются.
6. Остальной трафик идет через proxy.

Это отличается от китайско-ориентированных preset v2rayN. Клиент должен быть ориентирован на российский рынок.

### 11.2. Базовые правила Xray

Порядок правил важен:

1. block ads;
2. block bittorrent;
3. direct private IP/domains;
4. direct Ozon;
5. direct Russian geoip/geosite;
6. proxy all remaining traffic.

Пример логической модели:

```json
[
  {
    "type": "field",
    "outboundTag": "block",
    "domain": ["geosite:category-ads-all"]
  },
  {
    "type": "field",
    "outboundTag": "block",
    "protocol": ["bittorrent"]
  },
  {
    "type": "field",
    "outboundTag": "direct",
    "ip": ["geoip:private"]
  },
  {
    "type": "field",
    "outboundTag": "direct",
    "domain": ["geosite:private"]
  },
  {
    "type": "field",
    "outboundTag": "direct",
    "domain": [
      "domain:ozon.ru",
      "domain:ozone.ru",
      "domain:ozonusercontent.com"
    ]
  },
  {
    "type": "field",
    "outboundTag": "direct",
    "ip": ["geoip:ru"]
  },
  {
    "type": "field",
    "outboundTag": "proxy",
    "port": "0-65535"
  }
]
```

Перед реализацией нужно проверить, какие `geosite` категории доступны в используемых `geosite.dat`. Если есть надежный `geosite:ru`, его можно добавить как direct-правило для российских доменов. Если нет, MVP должен использовать `geoip:ru` и локальный доменный JSON-override для критичных российских сервисов.

### 11.3. Ozon override

Ozon rule хранится отдельно от geo assets:

```text
rules/overrides/ozon.direct.json
```

Начальный набор:

```json
{
  "id": "ozon-direct",
  "name": "Ozon напрямую",
  "outboundTag": "direct",
  "domains": [
    "domain:ozon.ru",
    "domain:ozone.ru",
    "domain:ozonusercontent.com"
  ],
  "enabled": true
}
```

Правило должно быть обновляемым отдельно от клиента, потому что проблемы российских сервисов могут требовать быстрых hotfix-изменений.

### 11.4. Geo asset update

Geo files должны обновляться автоматически.

Assets:

- `geoip.dat`;
- `geosite.dat`;
- optional: `geoip-only-cn-private.dat` не обязателен для российского MVP;
- локальные JSON overrides.

Поведение updater:

1. При первом запуске проверяет наличие geo assets.
2. При подключении проверяет TTL assets.
3. По умолчанию TTL: 24 часа.
4. Скачивает новые assets во временную папку.
5. Проверяет размер, hash и возможность чтения.
6. Атомарно заменяет старые assets.
7. При ошибке оставляет старую рабочую версию.
8. Пишет событие в diagnostics.

Источники должны быть конфигурируемыми. Начальный ориентир можно взять из `v2rayN-windows-64/guiConfigs/guiNConfig.json`:

- `https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/latest/download/{0}.dat`
- `https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/main/v2rayN/template.json`

Перед релизом эти источники нужно зафиксировать продуктово: либо использовать внешний источник с явным доверием, либо сделать собственный mirror.

## 12. Xray config generation

### 12.1. Inbounds

MVP использует локальные inbounds:

- SOCKS: `127.0.0.1:10808`;
- HTTP: `127.0.0.1:10809`.

System proxy должен указывать на HTTP inbound.

SOCKS inbound нужен для диагностики и совместимости.

### 12.2. Outbounds

Обязательные outbounds:

- `proxy`: активный VLESS профиль;
- `direct`: freedom;
- `block`: blackhole.

### 12.3. DNS

В MVP DNS настройки скрыты в advanced settings.

Поведение по умолчанию:

- использовать безопасные defaults Xray;
- не заставлять пользователя выбирать DNS;
- сохранить возможность future override через advanced UI.

DNS editor нельзя вырезать архитектурно. Его нужно скрыть, но оставить модель и место в настройках.

## 13. System proxy

### 13.1. Поведение

При `Подключить`:

- включить Windows system proxy;
- задать HTTP proxy `127.0.0.1:10809`;
- задать исключения для локальных сетей.

При `Отключить`:

- вернуть предыдущее состояние system proxy;
- не затирать пользовательские настройки без backup.

### 13.2. Reversible workflow

Перед изменением system proxy клиент сохраняет:

- было ли proxy включено;
- server;
- bypass list;
- auto-config URL, если был;
- timestamp.

Если приложение падает, при следующем запуске оно должно предложить восстановить предыдущее состояние.

## 14. Logging и telemetry

### 14.1. Локальные логи

Собираются локально:

- события UI;
- start/stop Xray;
- stderr/stdout Xray;
- ошибки генерации config;
- ошибки subscription refresh;
- ошибки geo update;
- system proxy changes;
- basic health checks.

Логи должны быть структурированными и пригодными для отправки.

### 14.2. Диагностическая отправка

Так как продукт планирует сбор логов, installer должен содержать:

- лицензионное соглашение;
- согласие на сбор диагностики;
- ссылку на privacy policy;
- понятное описание собираемых данных;
- настройку отключения необязательной telemetry.

Рекомендуемое разделение:

- Required operational logs: минимум, нужный для работы и безопасности клиента.
- Optional diagnostics: расширенные данные для поддержки и карты проблем.

### 14.3. Какие данные собирать

Можно собирать после явного согласия:

- версия клиента;
- версия Xray;
- версия geo assets;
- Windows version/build;
- тип устройства;
- архитектура CPU;
- регион по публичному IP;
- публичный IP или его анонимизированный/хэшированный вариант;
- ASN/провайдер;
- примерная скорость сети;
- объем трафика через клиента;
- активный routing preset;
- тип ошибки;
- timestamp событий;
- health check results;
- crash reports.

Нельзя отправлять без отдельного решения:

- полный proxy URL с UUID;
- private key, short id, user id;
- raw subscription URL, если он содержит token;
- список посещенных сайтов;
- содержимое DNS-запросов;
- персональные файлы пользователя;
- clipboard.

Для поддержки лучше отправлять sanitized bundle:

```text
diagnostics.zip
├─ app.log
├─ xray.redacted.log
├─ config.redacted.json
├─ environment.json
├─ route-health.json
└─ consent.json
```

### 14.4. Карта проблем по России

Для карты проблем достаточно агрегированных данных:

- регион;
- провайдер/ASN;
- тип ошибки;
- успешность подключения;
- latency bucket;
- speed bucket;
- версия клиента;
- версия правил.

Не нужно хранить raw IP дольше, чем требуется для определения региона/ASN.

## 15. Installer и распространение

### 15.1. Требования

Installer должен:

- ставить приложение без отдельной установки .NET;
- включать Xray core;
- включать начальные geo assets;
- показывать license agreement;
- показывать privacy/diagnostics notice;
- создавать shortcut;
- опционально включать автозапуск;
- корректно uninstall делать cleanup;
- не оставлять system proxy включенным после uninstall.

### 15.2. Формат

Для MVP:

- self-contained Windows installer;
- x64 first;
- arm64 optional later.

Рекомендуемый путь:

- Inno Setup для быстрого MVP installer;
- WiX Toolset, если нужен более строгий enterprise MSI-подход.

### 15.3. Code signing

Для публичного распространения нужен code signing certificate. Без подписи Windows SmartScreen будет пугать пользователя, что конфликтует с целью "не пугающий клиент".

Для внутреннего MVP подпись можно отложить, но для beta лучше запланировать.

## 16. Безопасность

Минимальные правила:

- не логировать секреты;
- redact proxy credentials;
- хранить subscription URL с token в защищенном storage;
- не отправлять diagnostics без consent;
- проверять hash загруженных бинарников и rule assets, если есть контрольные суммы;
- не запускать произвольные бинарники из пользовательских путей;
- обновления core делать из доверенного источника;
- сохранять last-known-good config.

## 17. Автоматизация

Обязательные automation tasks:

- build solution;
- run unit tests;
- run integration tests for VLESS parser;
- generate Xray config from fixtures;
- validate Xray config;
- package installer;
- update geo assets;
- create diagnostics redaction test;
- smoke test connect/disconnect на локальном test profile, если доступен.

CI должен публиковать artifacts:

- installer;
- portable debug build;
- test report;
- dependency/license report;
- generated sample config.

## 18. План реализации

### Этап 0. Product specification checkpoint

Цель: зафиксировать требования и границы MVP.

Вход:

- подтверждены форматы: `vless://` и subscription URL;
- подтвержден core: Xray only;
- подтвержден routing preset для России;
- подтвержден отказ от QR и TUN в MVP;
- подтвержден installer с bundled .NET;
- подтвержден open-source GitHub формат на первом этапе.

Работы:

- оформить `mvp.md`;
- завести glossary;
- определить repo structure;
- определить initial backlog;
- зафиксировать privacy assumptions.

Выход:

- документ принят владельцем продукта;
- противоречия с `client.md` помечены;
- известны функции MVP и non-goals.

Проверка:

- check-in: требования не противоречат ответам владельца;
- check-out: по документу можно начать проектирование без дополнительных устных вводных.

### Этап 1. Repository backbone

Цель: создать структурную основу solution.

Вход:

- принят `mvp.md`;
- выбран UI toolkit;
- выбран installer tool.

Работы:

- создать solution;
- создать проекты по модулям;
- настроить shared build props;
- настроить logging abstraction;
- настроить CI build;
- добавить license и privacy draft;
- добавить README с STANDART workflow.

Выход:

- solution собирается;
- пустое приложение запускается;
- CI проходит;
- структура модулей соответствует документу.

Проверка:

- check-in: выбранные технологии совместимы с Windows 10/11 и .NET 8;
- check-out: любой разработчик может открыть repo, собрать и запустить shell.

### Этап 2. Profile import

Цель: добавить импорт VLESS и subscription.

Вход:

- есть `Client.Profiles`;
- есть storage schema draft;
- есть fixture-набор VLESS ссылок.

Работы:

- реализовать VLESS parser;
- реализовать subscription downloader/parser;
- реализовать profile normalization;
- реализовать duplicate handling;
- реализовать storage profiles;
- добавить unit tests.

Выход:

- `vless://` ссылка импортируется;
- subscription URL импортирует список VLESS;
- неподдерживаемые строки игнорируются с понятной ошибкой;
- старые профили не удаляются при неудачном refresh.

Проверка:

- check-in: fixture покрывает reality/tls/grpc/tcp варианты;
- check-out: parser tests проходят, secrets не попадают в обычные логи.

### Этап 3. Xray runtime

Цель: научиться генерировать config и управлять Xray.

Вход:

- есть импортированный активный профиль;
- `xray.exe` включен в dev assets;
- определены inbound ports.

Работы:

- реализовать config renderer;
- реализовать process runner через CliWrap;
- реализовать stdout/stderr capture;
- реализовать start/stop/restart;
- реализовать last-known-good config;
- реализовать config validation.

Выход:

- Xray запускается из приложения;
- генерируется валидный config;
- процесс корректно останавливается;
- аварийный запуск откатывается на last-known-good.

Проверка:

- check-in: sample profiles валидны;
- check-out: integration test подтверждает, что Xray поднял local inbound.

### Этап 4. Russia routing preset

Цель: встроить маршрутизацию для российского рынка.

Вход:

- Xray runtime работает;
- есть `geoip.dat` и `geosite.dat`;
- есть начальный `ozon.direct.json`.

Работы:

- реализовать routing preset model;
- реализовать order-sensitive rules;
- добавить ads block;
- добавить bittorrent block;
- добавить private direct;
- добавить Ozon direct;
- добавить Russian direct;
- добавить proxy fallback;
- добавить advanced routing settings placeholder.

Выход:

- по умолчанию включен `Умный режим для России`;
- Ozon direct rule присутствует в config;
- torrents и ads уходят в block;
- fallback идет через proxy;
- правила можно будет расширить без переписывания UI.

Проверка:

- check-in: подтверждены доступные geo categories;
- check-out: generated config содержит правила в правильном порядке.

### Этап 5. Geo updater

Цель: автоматизировать обновление geo assets и overrides.

Вход:

- определены источники geo assets;
- есть локальный каталог assets;
- есть hash/size validation policy.

Работы:

- реализовать update manifest;
- реализовать download to temp;
- реализовать validation;
- реализовать atomic replace;
- реализовать rollback;
- реализовать TTL;
- добавить UI status для версии правил.

Выход:

- geo assets обновляются автоматически;
- при ошибке остается старая рабочая версия;
- пользователь не видит техническую сложность;
- поддержка видит версию правил в diagnostics.

Проверка:

- check-in: источники доступны и доверены;
- check-out: тест имитирует failed download и подтверждает сохранность старых assets.

### Этап 6. System proxy

Цель: безопасно управлять Windows system proxy.

Вход:

- Xray local HTTP inbound работает;
- определен backup format для предыдущего proxy state.

Работы:

- реализовать чтение текущего proxy state;
- реализовать backup;
- реализовать enable proxy;
- реализовать disable/restore;
- реализовать crash recovery prompt;
- добавить tests на serialization state.

Выход:

- connect включает system proxy;
- disconnect восстанавливает предыдущее состояние;
- uninstall не оставляет proxy включенным;
- ошибки registry/API понятны пользователю.

Проверка:

- check-in: есть тестовая Windows машина/VM;
- check-out: ручной smoke test подтверждает restore после normal и abnormal stop.

### Этап 7. Minimal UI

Цель: дать пользователю простой главный экран.

Вход:

- работают import, runtime, routing, system proxy;
- выбран UI toolkit;
- есть дизайн-направление.

Работы:

- реализовать main window;
- реализовать connect/disconnect state machine;
- реализовать add profile flow;
- реализовать profile list;
- реализовать settings shell;
- реализовать tray behavior;
- скрыть advanced logs/routing/DNS.

Выход:

- пользователь может добавить профиль и подключиться без технических настроек;
- главный экран не перегружен;
- все advanced функции доступны в настройках;
- приложение работает из tray.

Проверка:

- check-in: UX макет согласован;
- check-out: новый пользователь проходит сценарий подключения без инструкции.

### Этап 8. Diagnostics и telemetry

Цель: заменить просьбы о скриншотах логов нормальной диагностикой.

Вход:

- есть logging pipeline;
- есть privacy/consent draft;
- определен серверный endpoint или временный export zip.

Работы:

- реализовать structured logs;
- реализовать redaction;
- реализовать diagnostics zip;
- реализовать кнопку `Отправить диагностику`;
- реализовать environment collection;
- реализовать network/provider detection;
- реализовать traffic counters;
- добавить consent state.

Выход:

- пользователь может отправить диагностику одной кнопкой;
- secrets не попадают в bundle;
- можно строить агрегированную карту проблем по регионам/провайдерам;
- telemetry можно отключить в настройках, если она необязательная.

Проверка:

- check-in: privacy policy согласована;
- check-out: automated redaction test не пропускает UUID/token/private fields.

### Этап 9. Installer

Цель: собрать устанавливаемый продукт.

Вход:

- приложение стабильно проходит smoke tests;
- есть тексты license/privacy;
- определен installer tool.

Работы:

- собрать self-contained publish;
- включить Xray core;
- включить начальные geo assets;
- добавить license agreement;
- добавить diagnostics notice;
- добавить shortcuts;
- добавить uninstall cleanup;
- добавить optional autostart;
- подготовить signing pipeline.

Выход:

- пользователь устанавливает приложение без отдельного .NET;
- installer показывает license и diagnostics notice;
- uninstall чистит приложение и не ломает system proxy;
- build artifact готов для beta.

Проверка:

- check-in: чистая Windows VM без .NET;
- check-out: install/connect/disconnect/uninstall проходит без ручных правок.

### Этап 10. Beta checkpoint

Цель: выпустить первую тестовую версию.

Вход:

- installer готов;
- telemetry работает или diagnostics zip готов;
- известен rollback plan.

Работы:

- провести smoke test на Windows 10 и 11;
- проверить несколько провайдеров/регионов;
- проверить Ozon direct;
- проверить blocked ads/torrent behavior;
- проверить обновление geo assets;
- проверить восстановление system proxy;
- оформить known issues.

Выход:

- beta build опубликован;
- есть changelog;
- есть инструкция поддержки;
- есть канал сбора проблем.

Проверка:

- check-in: release checklist заполнен;
- check-out: beta пользователь может установить, добавить VLESS/subscription, подключиться и отправить диагностику.

## 19. Definition of Done для MVP

MVP считается готовым, если:

- installer ставится на чистую Windows 10/11 без отдельной установки .NET;
- пользователь может добавить `vless://`;
- пользователь может добавить subscription URL со списком `vless://`;
- Xray запускается и останавливается из клиента;
- system proxy включается и восстанавливается;
- по умолчанию включен `Умный режим для России`;
- Ozon direct rule встроен;
- реклама и bittorrent блокируются;
- остальной трафик уходит через proxy;
- geo assets обновляются автоматически и обратимо;
- advanced DNS/routing/logs скрыты, но доступны;
- diagnostics bundle собирается с redaction;
- пользователь принял license/diagnostics notice;
- приложение не оставляет system proxy включенным после отключения или uninstall;
- есть базовые unit/integration tests;
- есть beta release artifact.

## 20. Открытые решения

Нужно дополнительно решить:

- точный UI toolkit: `WPF-UI` или `MaterialDesignThemes`;
- installer: Inno Setup или WiX;
- источник geo assets: внешний runetfreedom или собственный mirror;
- нужно ли хранить raw public IP или сразу анонимизировать;
- нужна ли обязательная telemetry или только opt-in diagnostics;
- будет ли code signing уже на beta;
- название продукта и визуальная айдентика;
- серверный endpoint для diagnostics upload.

## 21. Рекомендация по контексту для разработки

Для этого проекта нужен режим контекста `high`.

Причина: проект не просто UI, а связка Windows UI, Xray runtime, маршрутизации, installer, обновлений, privacy/telemetry и тестов. `medium` хватит для отдельных задач, но будет теряться архитектурная целостность. `extreme` имеет смысл включать позже для больших проходов по всей кодовой базе, аудита безопасности, комплексного refactor или подготовки релиза.
