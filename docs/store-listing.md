# Microsoft Store listing copy (SelectiveVpn)

Готовые тексты для Partner Center. Политика конфиденциальности: [privacy.md](privacy.md)  
Публичный URL после пуша: https://github.com/fullspb/SelectiveVpn/blob/main/docs/privacy.md  
(для Store лучше GitHub Pages / raw HTTPS на этот файл)

---

## Short description / Краткое описание

### RU

SelectiveVpn — клиент выборочного VPN для Windows: через туннель идут только выбранные приложения, папки или адреса (или всё, кроме списка исключений). Работает с вашими AmneziaWG-конфигами и ссылками подписок через движок Mihomo. Не является VPN-сервисом: сервер и ключи — ваши.

### EN

SelectiveVpn is a Windows selective-VPN client: only chosen apps, folders, or addresses go through the tunnel (or everything except an exclude list). It uses your AmneziaWG configs or subscription links via Mihomo. Not a VPN service — you bring your own server and credentials.

---

## Full description / Полное описание

### RU

SelectiveVpn — десктоп-клиент для Windows 10/11 (x64), который помогает направлять в VPN только нужный трафик.

Что умеет:

- профили AmneziaWG / WireGuard (файлы `.conf` / `.awg`) и HTTP(S)-подписки (vless, vmess, shadowsocks, trojan, hysteria и др.);
- выборочная маршрутизация по приложениям (`.exe`), папкам и спискам адресов;
- режим «всё через VPN, кроме списка»;
- выбор сервера из подписки и проверка задержки, когда это доступно;
- темы оформления и языки: русский, английский, испанский, португальский, китайский;
- трей, автозапуск и опциональное подключение при старте;
- проверка обновлений с GitHub Releases.

Важно понимать:

- SelectiveVpn **не продаёт и не предоставляет** VPN-серверы. Нужен свой провайдер, конфиг или ссылка подписки.
- Для создания TUN Windows может запросить права администратора.
- При подключении приложение использует встроенный/загружаемый движок **Mihomo** и может перечислять процессы, чтобы применять правила к выбранным программам и их дочерним процессам.
- Вы отвечаете за соблюдение законов вашей страны и условий вашего VPN-провайдера.

Исходный код и релизы: https://github.com/fullspb/SelectiveVpn

Политика конфиденциальности: https://github.com/fullspb/SelectiveVpn/blob/main/docs/privacy.md

### EN

SelectiveVpn is a Windows 10/11 (x64) desktop client for selective VPN routing.

Features:

- AmneziaWG / WireGuard profiles (`.conf` / `.awg`) and HTTP(S) subscription links (vless, vmess, shadowsocks, trojan, hysteria, and more);
- route by apps (`.exe`), folders, or address lists;
- “everything via VPN except this list” mode;
- subscription server picker with delay checks when available;
- themes and languages: Russian, English, Spanish, Portuguese, Chinese;
- system tray, launch at startup, optional connect-on-launch;
- update checks from GitHub Releases.

Please note:

- SelectiveVpn does **not** sell or operate VPN servers. You must provide your own provider, config, or subscription URL.
- Windows may ask for administrator rights to create the TUN adapter.
- While connected, the app uses the **Mihomo** engine and may enumerate processes so routing rules apply to selected programs and their child processes.
- You are responsible for complying with local law and your VPN provider’s terms.

Source and releases: https://github.com/fullspb/SelectiveVpn

Privacy policy: https://github.com/fullspb/SelectiveVpn/blob/main/docs/privacy.md

---

## Partner Center notes (honest defaults)

- **Collects personal info?** Yes locally (VPN keys/URLs/settings); network to user-chosen VPN/subscription hosts; GitHub for updates; optional HWID headers only to subscription URLs.
- **Publisher analytics backend?** No.
- **Account required?** No.
- **Internet required?** Yes for subscriptions, updates, and VPN; UI can open offline.
- **Accesses:** network, local filesystem (configs), process list (routing), optional startup registry.
