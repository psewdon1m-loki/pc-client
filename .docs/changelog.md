# Changelog

## 2026-05-09

### Этап 0. Product specification checkpoint

Статус: выполнено.

Входные условия:

- `vless://` и subscription URL подтверждены владельцем продукта.
- Xray only подтвержден.
- Российская маршрутизация по умолчанию подтверждена.
- QR и TUN исключены из MVP.
- Installer с bundled .NET и license/diagnostics notice подтвержден.
- Open-source GitHub формат на первом этапе подтвержден.

Работы:

- Изучены `client/mvp.md` и `client/client.md`.
- Зафиксировано, что требования владельца продукта и `client/mvp.md` выше исходных рекомендаций `client/client.md`.
- Проверено окружение: системный .NET SDK отсутствовал, локально установлен .NET SDK 8.0.420 в `.dotnet` для обратимой сборки без изменения системной установки.

Выходные условия:

- Спецификация MVP принята как рабочий контракт.
- Противоречия с `client.md` учтены.
- По документу можно начинать реализацию.

Проверка:

- Check-in: требования не противоречат ответам владельца.
- Check-out: этап 1 может стартовать без дополнительных вводных.

### Этап 1. Repository backbone

Статус: выполнено.

Входные условия:

- `mvp.md` принят как рабочий контракт.
- UI toolkit выбран в пользу чистого WPF MVP без внешней UI-библиотеки, чтобы быстрее получить рабочий продукт; визуальный стиль сделан Fluent-like.
- Installer tool выбран: Inno Setup.

Работы:

- Создан `Client.sln`.
- Созданы проекты:
  - `Client.App.Win`;
  - `Client.Core`;
  - `Client.Profiles`;
  - `Client.Routing`;
  - `Client.Transport.Xray`;
  - `Client.Storage`;
  - `Client.Updater`;
  - `Client.Telemetry`;
  - `Client.Platform.Windows`;
  - `Client.Tests`;
  - `Client.Smoke`.
- Добавлены `README.md`, `.gitignore`, `Directory.Build.props`.
- Добавлены `LICENSE.txt` и `PRIVACY.md`.
- Добавлены scripts для publish, smoke и installer packaging.

Выходные условия:

- Solution собирается.
- Структура модулей соответствует STANDART и `mvp.md`.
- Пустой shell заменен на минимальный рабочий WPF UI.

Проверка:

- `dotnet build Client.sln -v minimal`: успешно, 0 warnings, 0 errors.

### Этап 2. Profile import

Статус: выполнено.

Входные условия:

- Есть `Client.Profiles`.
- Есть storage schema draft в SQLite.
- Есть fixture VLESS ссылок в тестах.

Работы:

- Реализован `VlessParser`.
- Реализован `SubscriptionParser` с поддержкой plain text и base64.
- Реализован `SubscriptionClient`.
- Добавлена нормализация и дедупликация subscription профилей.
- Добавлено SQLite-хранилище профилей.

Выходные условия:

- `vless://` ссылка импортируется.
- Subscription URL/контент со списком `vless://` импортируется.
- Неподдерживаемые строки игнорируются.
- Старые профили не удаляются при неудачном refresh.

Проверка:

- `VlessParserTests`: reality/tcp и grpc/tls проходят.
- `SubscriptionParserTests`: base64 subscription проходит.

### Этап 3. Xray runtime

Статус: выполнено.

Входные условия:

- Есть импортируемый активный профиль.
- `xray.exe` включен в assets приложения.
- Определены inbound ports: SOCKS `10808`, HTTP `10809`.

Работы:

- Реализован `XrayConfigRenderer`.
- Реализован `XrayConfigValidator`.
- Реализован `XrayProcessManager`.
- Добавлен `LastKnownGoodConfigStore`.
- Runtime bootstrap копирует `xray.exe`, `geoip.dat`, `geosite.dat` в одну runtime-папку пользователя.

Выходные условия:

- Генерируется валидный Xray config.
- Xray может проверить config через `xray run -test`.
- Есть last-known-good сохранение.

Проверка:

- `RoutingAndConfigTests`: config JSON валиден.
- `scripts/smoke.ps1`: `Configuration OK`.

### Этап 4. Russia routing preset

Статус: выполнено.

Входные условия:

- Xray runtime работает.
- Есть `geoip.dat` и `geosite.dat`.
- Есть `ozon.direct.json`.

Работы:

- Реализован `RussiaRoutingPreset`.
- Реализован `OzonDirectRuleProvider`.
- Добавлен `Assets/overrides/ozon.direct.json`.
- Настроен порядок правил:
  - ads block;
  - bittorrent block;
  - private direct;
  - Ozon direct;
  - `geoip:ru` direct;
  - fallback proxy.

Выходные условия:

- По умолчанию включен `Умный режим для России`.
- Ozon direct rule присутствует в config.
- Торренты и реклама уходят в block.
- Остальное уходит через proxy.

Проверка:

- `RoutingAndConfigTests.RussiaSmartRules_PreserveRequiredOrder`: успешно.
- `scripts/smoke.ps1`: Xray принимает routing config.

### Этап 5. Geo updater

Статус: выполнено для MVP.

Входные условия:

- Определены источники geo assets.
- Есть локальный каталог assets.
- Есть validation policy по минимальному размеру и atomic replace.

Работы:

- Реализован `GeoAssetUpdater`.
- Добавлены defaults для `geoip.dat` и `geosite.dat` из runetfreedom releases.
- Реализованы TTL, download-to-temp, size validation, atomic replace и сохранение старых assets при ошибке.
- Начальные assets включены в приложение из `v2rayN-windows-64/bin`.

Выходные условия:

- Geo assets обновляются автоматически при подключении.
- При ошибке остается старая рабочая версия.
- Версия/наличие assets доступна через файловое состояние и diagnostics.

Проверка:

- Build и smoke подтверждают, что bundled geo assets доступны Xray.

### Этап 6. System proxy

Статус: выполнено.

Входные условия:

- HTTP inbound задан на `127.0.0.1:10809`.
- Определен backup format предыдущего proxy state.

Работы:

- Добавлен модуль `Client.Platform.Windows`.
- Реализованы `SystemProxyState` и `SystemProxyService`.
- Перед включением proxy сохраняется предыдущее состояние.
- При отключении восстанавливается предыдущее состояние.
- После изменения registry вызывается Windows Internet Settings refresh.

Выходные условия:

- Connect включает system proxy.
- Disconnect восстанавливает прежний state.
- Логика отделена от UI и Xray runtime.

Проверка:

- Build проходит.
- Silent installer uninstall не запускал приложение и не менял system proxy.
- Полный ручной smoke connect/disconnect с реальным профилем остается для beta-проверки, потому что в репозитории нет рабочего пользовательского VLESS endpoint.

### Этап 7. Minimal UI

Статус: выполнено.

Входные условия:

- Работают import, runtime, routing, system proxy.
- Выбран WPF.
- Дизайн-направление зафиксировано.

Работы:

- Реализован один главный экран.
- Добавлены статусы `Отключено`, `Подключение`, `Защищено`, `Ошибка`.
- Добавлены кнопки:
  - `Подключить` / `Отключить`;
  - `Добавить из буфера`;
  - `Добавить вручную`;
  - `Диагностика`.
- Добавлен список профилей.
- Advanced настройки скрыты в `Expander`.
- Добавлен tray icon через Windows Forms NotifyIcon.

Выходные условия:

- Пользователь может импортировать профиль и подключиться с главного экрана.
- Главный экран не показывает raw JSON, inbound/outbound или routing table.
- Advanced DNS/routing/logs скрыты, но архитектурно предусмотрены.

Проверка:

- WPF проект собирается.
- Assets попадают в Debug/Release output.

### Этап 8. Diagnostics и telemetry

Статус: выполнено для локального MVP.

Входные условия:

- Есть logging pipeline.
- Есть privacy/consent draft.
- Серверный endpoint пока не задан, поэтому реализован export zip.

Работы:

- Добавлен `SimpleFileLogger`.
- Реализован `SecretRedactor`.
- Реализован `DiagnosticsBundleBuilder`.
- Добавлена кнопка `Диагностика`.
- Diagnostics bundle включает environment, redacted config, logs и consent marker.

Выходные условия:

- Пользователь может создать diagnostics zip.
- Proxy secrets редактируются.
- Архитектура готова к будущей отправке на сервер.

Проверка:

- `TelemetryTests.Redact_RemovesProxySecrets`: успешно.

### Этап 9. Installer

Статус: выполнено.

Входные условия:

- Приложение проходит build/test/smoke.
- Есть `LICENSE.txt` и `PRIVACY.md`.
- Выбран Inno Setup.

Работы:

- Добавлен `installer/LokiClient.iss`.
- Добавлен `scripts/publish.ps1`.
- Добавлен `scripts/package-inno.ps1`.
- Собран self-contained publish в `artifacts/publish/win-x64`.
- Через Chocolatey установлен Inno Setup 6.7.1.
- Собран installer `artifacts/installer/LokiClientSetup-0.1.0-win-x64.exe`.
- Проведен silent install в `artifacts/install-test`.
- Проверено наличие `Client.App.Win.exe`, `Assets/xray/xray.exe`, `Assets/geo/geosite.dat`.
- Проведен silent uninstall; тестовая директория удалена.

Выходные условия:

- Installer ставит приложение без отдельной установки .NET.
- Installer показывает license/privacy в обычном UI-режиме.
- Installer включает Xray core и начальные geo assets.
- Uninstall работает.

Проверка:

- `scripts/publish.ps1`: успешно.
- `scripts/package-inno.ps1`: успешно.

## 2026-05-09, hardening pass

### Реальный subscription smoke

Статус: выполнено.

Вход:

- Реальный subscription URL: `https://loki-panel.shmoza.net:8000/sub/SzlYMlI3TDVNNFE4WjFQQiwxNzc4MzMyNjQ3lGVgfDSRJi`.
- Внутри subscription обнаружены 2 VLESS профиля:
  - `secure sh` на `199.68.196.107:8021`;
  - `secure nd` на `31.172.78.81:8021`.

Работы:

- `scripts/smoke.ps1` переведен с fixture smoke на реальный integration smoke.
- `Client.Smoke` теперь:
  - скачивает subscription;
  - парсит 2 профиля;
  - генерирует Xray config для каждого;
  - прогоняет `xray run -test`;
  - запускает Xray;
  - проверяет HTTPS-запрос через локальный HTTP proxy;
  - опционально проверяет Windows system proxy enable/restore.
- Добавлен controlled compatibility режим `allowInvalidSubscriptionTls`, потому что Windows/PowerShell не доверяет TLS цепочке `loki-panel.shmoza.net:8000`.

Выход:

- Оба профиля проходят Xray config validation.
- Оба профиля реально поднимают Xray.
- Оба профиля успешно проводят HTTPS-запрос через proxy: `https://www.google.com/generate_204 -> 204`.
- System proxy enable/restore проходит.

Проверка:

- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -TestSystemProxy`: успешно.

Риск:

- Для публичной версии лучше исправить TLS сертификат subscription panel. Текущий compatibility bypass рабочий, но небезопасен как production default.

### Diagnostics upload endpoint

Статус: выполнено.

Работы:

- Добавлен `tools/Diagnostics.Endpoint`.
- Endpoint:
  - `GET /health`;
  - `POST /diagnostics/upload` с `multipart/form-data` полем `bundle`.
- Добавлен `DiagnosticsUploader`.
- Добавлена UI-кнопка `Отправить диагностику`.
- Добавлен `scripts/test-diagnostics-upload.ps1`.
- Добавлен integration test `DiagnosticsEndpointTests`.

Выход:

- Локальный endpoint принимает diagnostics zip и сохраняет файл.
- Upload возвращает `uploadId`.

Проверка:

- `dotnet test Client.sln -v minimal`: 7/7 passed.
- `scripts/test-diagnostics-upload.ps1`: успешно, получен `uploadId`.

### License и privacy

Статус: выполнено.

Работы:

- `LICENSE.txt` заменен с placeholder на MIT License для исходного кода Loki Client.
- `PRIVACY.md` заменен с placeholder на рабочую privacy policy.
- Добавлен `THIRD_PARTY_NOTICES.md`.
- Installer теперь включает `LICENSE.txt`, `PRIVACY.md`, `THIRD_PARTY_NOTICES.md`.

Выход:

- License/privacy больше не placeholders.
- Third-party notices отделяют собственный код от Xray, DAT assets, NuGet и Inno Setup.

Проверка:

- Installer install test подтвердил наличие `PRIVACY.md` и `THIRD_PARTY_NOTICES.md` в установленной папке.

### Code signing

Статус: отключено и удалено из MVP.

Работы:

- Удален `scripts/sign.ps1`.
- Удален каталог `artifacts/signing`.
- Удален локальный self-signed certificate из `Cert:\CurrentUser\My`.
- Code signing не выполняется при сборке MVP.

Выход:

- Artifacts остаются unsigned.
- Windows больше не должен показывать окна установки локального root certificate.
- MVP не делает локальный trust-store mutation.

Риск:

- Для публичного релиза unsigned installer будет вызывать SmartScreen/издатель неизвестен. Если понадобится релиз для внешних пользователей, надо покупать OV/EV code signing certificate и возвращать signing pipeline уже без self-signed CA.

Проверка:

- `Get-AuthenticodeSignature` на финальных артефактах должен показывать `NotSigned`.

### Финальный package/install smoke

Статус: выполнено.

Работы:

- Оптимизирован publish: `SatelliteResourceLanguages=ru;en`.
- Пересобран self-contained publish.
- Пересобран installer.
- Installer оставлен unsigned.
- Проведен silent install/uninstall через `Start-Process -Wait`.

Выход:

- Финальный installer: `artifacts/installer/LokiClientSetup-0.1.0-win-x64.exe`.
- Размер installer после оптимизации: около 74 MB.
- Установка содержит:
  - `Client.App.Win.exe`;
  - `Assets/xray/xray.exe`;
  - `Assets/geo/geosite.dat`;
  - `PRIVACY.md`;
  - `THIRD_PARTY_NOTICES.md`.
- Uninstall удаляет тестовую директорию.

Проверка:

- `scripts/publish.ps1`: успешно.
- `scripts/package-inno.ps1`: успешно.
- Silent install/uninstall: успешно, `existsAfterUninstall=False`.
- Текущее состояние Windows proxy после тестов: `ProxyEnable=0`.

## 2026-05-09, code signing removal

Статус: выполнено.

Причина:

- Локальный self-signed code signing мешал разработке и вызывал Windows Security Warning об установке root certificate.

Работы:

- Удален `scripts/sign.ps1`.
- Удален `artifacts/signing`.
- Удален сертификат `A3BB1BBF4C49DF7619829EB65F2075CC05B1490D` из `Cert:\CurrentUser\My`.
- В `CurrentUser\Root` и `CurrentUser\TrustedPublisher` этот сертификат не найден.
- Publish и installer пересобраны без подписи.

Выход:

- Code signing полностью выключен для текущего MVP.
- Новые artifacts unsigned.
- Certificate install prompts больше не должны появляться.

Проверка:

- Финальный test matrix после удаления signing проходит; детали ниже в итогах запуска.

Итоги финального запуска:

- `scripts/sign.ps1`: отсутствует.
- `artifacts/signing`: отсутствует.
- Сертификат `A3BB1BBF4C49DF7619829EB65F2075CC05B1490D` отсутствует в:
  - `Cert:\CurrentUser\Root`;
  - `Cert:\CurrentUser\TrustedPublisher`;
  - `Cert:\CurrentUser\My`.
- `scripts/publish.ps1`: успешно.
- `scripts/package-inno.ps1`: успешно.
- `dotnet test Client.sln -v minimal`: 7/7 passed.
- `scripts/smoke.ps1 -TestSystemProxy`: оба реальных VLESS профиля работают, Xray config OK, proxy HTTP check OK.
- `scripts/test-diagnostics-upload.ps1`: upload OK, получен `uploadId`.
- Silent install/uninstall unsigned installer: успешно, `existsAfterUninstall=False`.
- `Get-AuthenticodeSignature`:
  - `artifacts/publish/win-x64/Client.App.Win.exe`: `NotSigned`;
  - `artifacts/installer/LokiClientSetup-0.1.0-win-x64.exe`: `NotSigned`.
- Windows proxy после тестов: `ProxyEnable=0`.

## 2026-05-09, manual UI port conflict fix

Статус: выполнено.

Проблема:

- При ручном подключении UI показывал ошибку: `Xray запущен, но локальный HTTP inbound не ответил`.
- На машине параллельно был запущен Xray от v2rayN: `C:\tools\v2rayn\v2rayN-windows-64\bin\xray\xray.exe`.
- Этот процесс слушал `127.0.0.1:10808`.
- Старые defaults Loki Client тоже использовали SOCKS `10808`, из-за чего Xray падал на конфликте порта до поднятия HTTP inbound.

Работы:

- Defaults локальных портов изменены:
  - SOCKS: `18088`;
  - HTTP: `18089`.
- В `ClientController` добавлен автоматический выбор свободной пары портов, если сохраненные настройки заняты.
- В `XrayProcessManager` добавлен захват stdout/stderr Xray.
- Если Xray завершился при старте, UI теперь должен получить более конкретную ошибку с exit code и stderr.

Выход:

- Клиент больше не конфликтует с v2rayN на `10808`.
- Даже если в SQLite остались старые `10808/10809`, клиент на connect выберет свободные `18088/18089` или следующую свободную пару.

Проверка:

- `dotnet build Client.sln -v minimal`: успешно.
- `dotnet test Client.sln -v minimal`: 7/7 passed.
- `scripts/smoke.ps1 -TestSystemProxy`: оба реальных VLESS профиля работают, Xray config OK, proxy HTTP check OK.
- `scripts/test-diagnostics-upload.ps1`: upload OK.
- `scripts/publish.ps1`: успешно.
- `scripts/package-inno.ps1`: успешно.
- Silent install/uninstall: успешно.
- Installer остается unsigned: `NotSigned`.
- Windows proxy после тестов: `ProxyEnable=0`.
- Silent install/uninstall: успешно.

### Этап 10. Beta checkpoint

Статус: частично выполнено.

Входные условия:

- Installer готов.
- Diagnostics zip готов.
- Rollback для system proxy и last-known-good config реализован.

Работы:

- Проведены build/test/smoke.
- Проверена упаковка installer.
- Проверена установка/удаление в локальную тестовую директорию.
- Проверен Xray `run -test` на сгенерированном config.

Выходные условия:

- Beta artifact создан: `artifacts/installer/LokiClientSetup-0.1.0-win-x64.exe`.
- Changelog обновлен.
- Известные ограничения зафиксированы ниже.

Ограничения перед публичной beta:

- Нет code signing certificate.
- Нет реального VLESS endpoint в репозитории для полного connect/disconnect smoke.
- Нет серверного endpoint для diagnostics upload.
- Privacy/license тексты являются MVP placeholders и требуют юридической финализации.
- Не проводилась проверка на отдельной чистой Windows 10/11 VM, только локальный install/uninstall.

Проверка:

- `dotnet build Client.sln -v minimal`: успешно.
- `dotnet test Client.sln --no-build -v minimal`: 6/6 passed.
- `scripts/smoke.ps1`: Xray `Configuration OK`.
- `scripts/publish.ps1`: успешно.
- `scripts/package-inno.ps1`: успешно.
