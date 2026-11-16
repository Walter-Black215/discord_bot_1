#!/bin/bash
# Установка / обновление бота /анализ на сервере Ubuntu.
# Запускается от root: bash /opt/discord-analyzer/server/setup.sh
set -e
APP=/opt/discord-analyzer
export DEBIAN_FRONTEND=noninteractive
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# 1. .NET 8 (нужен, чтобы собрать и запустить бота)
if ! command -v dotnet >/dev/null 2>&1; then
  echo "== Устанавливаю .NET 8 =="
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0
fi

# 2. Отдельный пользователь без права входа — бот работает не под root
id analyzer >/dev/null 2>&1 || useradd --system --home-dir "$APP" --shell /usr/sbin/nologin analyzer

# 3. Сборка
echo "== Собираю бота =="
mkdir -p "$APP/app/data"
cd "$APP"
find src server -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.service' \) -exec sed -i 's/\r$//' {} +
dotnet publish server/DiscordAnalyzerBot.csproj -c Release -o app --nologo -v q

# 4. Права: всё принадлежит пользователю analyzer, папка с токеном закрыта для остальных
chown -R analyzer:analyzer "$APP"
chmod 700 "$APP/app/data"

# 5. Автозапуск
install -m 644 server/discord-analyzer.service /etc/systemd/system/discord-analyzer.service
systemctl daemon-reload
systemctl enable discord-analyzer >/dev/null 2>&1
if [ -f "$APP/app/data/token.txt" ]; then
  systemctl restart discord-analyzer
  echo "== Бот перезапущен =="
else
  echo "== Токена ещё нет: бот запустится после set-token.ps1 =="
fi

# 6. Проверка на тестовых данных
sudo -u analyzer env DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet "$APP/app/DiscordAnalyzerBot.dll" --selftest /tmp/analyzer-selftest.html
echo SETUP_OK
