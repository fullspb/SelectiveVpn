# Политика конфиденциальности SelectiveVpn / SelectiveVpn Privacy Policy

**Дата обновления / Last updated:** 10 ноября 2026 г. / November 10, 2026  
**Разработчик / Developer:** fullspb ([github.com/fullspb/SelectiveVpn](https://github.com/fullspb/SelectiveVpn))  
**Контакт / Contact:** [GitHub Issues](https://github.com/fullspb/SelectiveVpn/issues)

---

## Русский

### 1. Кратко

SelectiveVpn — локальный клиент VPN для Windows. У нас **нет** собственного сервера аккаунтов, рекламы и встроенной аналитики/телеметрии. Мы **не собираем** и **не продаём** персональные данные пользователей через бэкенд приложения, потому что такого бэкенда у приложения нет.

Приложение обрабатывает данные **на вашем устройстве** и отправляет сетевые запросы только туда, куда это нужно для работы VPN, проверки обновлений и ваших настроек.

### 2. Какие данные обрабатываются на устройстве

На компьютере (в основном в `%LocalAppData%\SelectiveVpn`) могут храниться:

- настройки приложения (тема, язык, автозапуск, список профилей и правил маршрутизации);
- файлы профилей AmneziaWG/WireGuard, включая **криптографические ключи**, если вы их импортировали;
- ссылки подписок и кэш списка серверов;
- локальный стабильный идентификатор устройства (`device-id.txt`) для панелей с лимитом устройств;
- временные рабочие файлы движка Mihomo на время сессии;
- загруженные установщики обновлений.

Журнал в интерфейсе хранится в памяти сессии и нами никуда не выгружается.

Удаление приложения через установщик удаляет каталог `%LocalAppData%\SelectiveVpn`. Ручное удаление файлов профиля также возможно.

### 3. Какие данные уходят в сеть

#### 3.1. Ваш VPN-провайдер и подписка

При импорте/обновлении подписки и при работе туннеля трафик и служебные запросы идут на **указанные вами** URL и серверы.

К запросам подписки приложение может добавлять заголовки: `x-hwid`, сведения об ОС Windows, модель клиента `SelectiveVpn` и `User-Agent`. Это нужно многим панелям с ограничением числа устройств. Мы не контролируем, как ваш провайдер хранит и использует эти данные — смотрите его политику.

При активном подключении сетевой трафик выбранных приложений/адресов (или весь трафик, в зависимости от правил) проходит через ваш VPN-сервер. SelectiveVpn не является оператором этого сервера.

#### 3.2. Обновления

При открытии главного окна приложение может проверить наличие новой версии через GitHub API (`api.github.com`, репозиторий `fullspb/SelectiveVpn`) с `User-Agent`, содержащим версию приложения. Идентификатор устройства (`x-hwid`) при проверке обновлений **не отправляется**. Загрузка установщика начинается только после вашего действия.

#### 3.3. Движок Mihomo и служебные проверки

При необходимости приложение может загрузить бинарник Mihomo с GitHub (MetaCubeX).

Во время работы туннеля движок может обращаться к служебным URL проверки доступности (например, health-check), использовать указанные в конфигурации DNS и, для отображения внешнего IP, запрос к сервису определения IP (через прокси туннеля). Эти запросы нужны для работы клиента, а не для маркетинговой аналитики SelectiveVpn.

#### 3.4. Локальный API

Управление Mihomo идёт через localhost на вашем ПК и во внешнюю сеть не передаётся.

### 4. Данные, которые мы не собираем

Приложение **не** ведёт учётные записи SelectiveVpn, **не** показывает рекламу, **не** содержит SDK аналитики/краш-репортинга со стороны разработчика и **не** продаёт данные пользователей.

GitHub как площадка релизов и Issues обрабатывает обращения по своим правилам: [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

### 5. Разрешения и доступ на устройстве

Для работы могут использоваться:

- права администратора / UAC — для создания TUN-адаптера;
- перечисление процессов — для выборочной маршрутизации приложений и дочерних процессов;
- чтение списка установленных программ (реестр / меню «Пуск») — для удобного выбора приложений;
- автозапуск (запись в реестр текущего пользователя) — только если вы включите опцию.

Правила брандмауэра Windows приложение само не прописывает.

### 6. Дети

Приложение не предназначено для детей младше 13 лет и не ориентировано на сбор данных о детях.

### 7. Изменения

Мы можем обновлять эту политику. Актуальная версия публикуется по тому же URL; дата обновления указывается в начале документа.

### 8. Контакты

Вопросы о конфиденциальности: [Issues](https://github.com/fullspb/SelectiveVpn/issues) в репозитории SelectiveVpn.

---

## English

### 1. Summary

SelectiveVpn is a local Windows VPN client. We do **not** run an account backend, ads, or first-party analytics/telemetry. We do **not** collect or sell personal data through an app backend, because the app has none.

Data is processed **on your device**. Network requests are made only as needed for VPN operation, updates, and your configuration.

### 2. Data stored on your device

Under `%LocalAppData%\SelectiveVpn` the app may store:

- settings (theme, language, autostart, profiles, routing rules);
- AmneziaWG/WireGuard profile files, including **private keys** if you imported them;
- subscription URLs and cached server lists;
- a stable local device id (`device-id.txt`) for providers with device limits;
- temporary Mihomo runtime files during a session;
- downloaded update installers.

UI logs stay in session memory and are not uploaded by us.

Uninstalling via the installer removes `%LocalAppData%\SelectiveVpn`.

### 3. Data sent over the network

#### 3.1. Your VPN provider / subscription

Subscription import/refresh and tunnel traffic go to **URLs and servers you configure**.

Subscription requests may include headers such as `x-hwid`, Windows OS info, client model `SelectiveVpn`, and a `User-Agent`. Many device-limit panels require this. We do not control how your provider uses that data.

While connected, traffic for selected apps/addresses (or all traffic, depending on rules) goes through **your** VPN server. SelectiveVpn does not operate that server.

#### 3.2. Updates

On main-window open, the app may check GitHub Releases (`api.github.com` / `fullspb/SelectiveVpn`) with a versioned `User-Agent`. The device id (`x-hwid`) is **not** sent for update checks. Installer download starts only after you choose to update.

#### 3.3. Mihomo and health checks

The app may download the Mihomo binary from GitHub (MetaCubeX) if needed.

While the tunnel runs, Mihomo may use health-check URLs, configured DNS, and (for the external-IP display) an IP-lookup service via the tunnel proxy. These support client operation, not SelectiveVpn marketing analytics.

#### 3.4. Local API

Mihomo is controlled via localhost on your PC and is not exposed by us as a cloud service.

### 4. What we do not collect

No SelectiveVpn accounts, no ads, no developer analytics/crash SDKs, no sale of user data.

GitHub (releases/issues) is governed by [GitHub’s own policies](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

### 5. Device permissions

The app may use:

- administrator / UAC elevation to create a TUN adapter;
- process enumeration for selective app routing (including child processes);
- installed-app discovery (registry / Start Menu) for the app picker;
- current-user Run key for optional autostart.

The app does not write Windows Firewall rules itself.

### 6. Children

Not directed at children under 13 and not intended to collect children’s data.

### 7. Changes

We may update this policy; the current version will be posted at the same URL with an updated date.

### 8. Contact

Privacy questions: [GitHub Issues](https://github.com/fullspb/SelectiveVpn/issues).
