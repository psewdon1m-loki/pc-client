Основа под свой Windows-клиент “поверх v2rayN”

Здесь я бы не строил продукт “поверх старого UI v2rayN”, а строил поверх его архитектурного паттерна. В официальном репозитории v2rayN уже есть отдельная библиотека ServiceLib, отдельный WPF-проект v2rayN и отдельный Avalonia-проект v2rayN.Desktop. То есть сам проект уже разложен на сервисный слой и UI-слои. Это хороший ориентир для вашего собственного клиента.

11.1. Что выбрать как основу

Есть два разумных пути.

Вариант A. Самый быстрый Windows-only MVP
.NET 8
WPF
ReactiveUI
MaterialDesignThemes
H.NotifyIcon.Wpf

Это почти повторяет официальный Windows UI v2rayN: его v2rayN.csproj использует net8.0-windows10.0.19041.0, UseWPF, ReactiveUI.WPF, ReactiveUI.Fody, MaterialDesignThemes и H.NotifyIcon.Wpf.

Вариант B. Более стратегичный путь
.NET 8
Avalonia
ReactiveUI.Avalonia

Официальный v2rayN.Desktop уже использует Avalonia Desktop, DataGrid, AvaloniaEdit, DialogHost.Avalonia, ReactiveUI.Avalonia и compiled bindings. Это лучший выбор, если потом захотите вынести тот же клиент на Linux/macOS.

Для вас я бы выбрал так:

если цель — быстро сделать свой Windows-клиент: WPF;
если цель — строить уже продуктовую основу: Avalonia-shell + общий сервисный слой.
11.2. Технологии, которые стоит взять из архитектуры v2rayN

ServiceLib в v2rayN уже показывает очень здравый набор зависимостей для вашего клиента:

sqlite-net-pcl — локальное хранение профилей и кэша;
NLog — логирование;
CliWrap — запуск и контроль xray.exe;
Downloader — автообновление core/rules/client;
QRCoder и ZXing — QR import/export;
YamlDotNet — на будущее, если захотите экспорт в Clash/Sing-box;
ReactiveUI + Fody — реактивный UI и binding-слой.

Плюс в ServiceLib уже лежат sample-конфиги и шаблоны routing/TUN, что полезно как ориентир для собственной системы генерации Xray JSON.

11.3. Как я бы разложил решение
MyClient.sln
 ├─ MyClient.Shell.Win        # WPF или Avalonia UI
 ├─ MyClient.Core            # доменные модели, use-cases
 ├─ MyClient.Transport       # xray process runner, config renderer
 ├─ MyClient.PasarGuard      # работа с subscription/API panel
 ├─ MyClient.Storage         # SQLite, кеш, last-known-good config
 ├─ MyClient.Diagnostics     # health, logs, route tests
 ├─ MyClient.Updater         # обновление клиента, xray, geodata
 └─ MyClient.Shared
11.4. Что должно быть в первой версии клиента

Я бы не начинал с “супер-клиента”. Первый релиз должен уметь только это:

принять один subscription URL PasarGuard;
скачать и распарсить подписку;
показать пользователю один активный профиль RU Main;
сгенерировать локальный Xray config;
запустить/остановить xray.exe;
показать минимум диагностики: статус ядра, активный узел, время последнего refresh, последний лог;
уметь безопасно откатиться на last-known-good config.

Это идеально совпадает с вашей серверной архитектурой: вся сложная маршрутизация уже на RU-сервере, клиенту не нужно на первом этапе делать собственные geo-routing правила.

11.5. Какие экраны нужны

Для первой версии достаточно пяти экранов:

Dashboard — статус соединения, текущий профиль, кнопка Connect/Disconnect;
Accounts — subscription URL, refresh, import/export;
Connection — локальные режимы, системный proxy, автозапуск;
Diagnostics — лог Xray, тесты, last refresh, health;
Updates — обновление клиента, Xray core, rule assets.
11.6. Что оставить на вторую очередь

Во вторую очередь уже можно добавлять:

локальный TUN;
per-app режимы;
multi-account;
встроенную проверку маршрута;
self-care интеграцию с PasarGuard API;
собственный updater канала stable/beta.
11.7. Практический выбор для вас

Если цель — сделать именно свой брендированный Windows-клиент поверх идей v2rayN, я бы шёл так:

UI писать заново;
runtime/control-plane строить по образцу ServiceLib;
на первом этапе поддерживать только Xray и только subscription URL PasarGuard;
локальную маршрутизацию в клиенте не усложнять, потому что основная логика уже на RU-ingress.

Это даст вам клиент, который очень хорошо стыкуется с вашей новой серверной схемой: один URL, один видимый узел, серверный split-routing и минимум боли у пользователя.