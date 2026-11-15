#!/usr/bin/env bash

set -euo pipefail

SERVER_HEALTH_URL="${SERVER_HEALTH_URL:-http://127.0.0.1:5000/health}"
MOBILE_HEALTH_URL="${MOBILE_HEALTH_URL:-http://127.0.0.1:5001/health}"
LOG_TAG="einsatz-health-check"
APP_ROOT="${APP_ROOT:-/opt/einsatzueberwachung}"
DISK_WARN_PERCENT="${DISK_WARN_PERCENT:-85}"
DISK_CRIT_PERCENT="${DISK_CRIT_PERCENT:-95}"
DISK_FULL=0

# Plattenplatz (#181): bei voller Platte hilft ein Neustart nicht, er fuehrt nur zur Neustart-Schleife
check_disk() {
  local used
  used=$(df --output=pcent "$APP_ROOT" 2>/dev/null | tail -1 | tr -dc '0-9')
  [[ -n "$used" ]] || return 0
  if (( used >= DISK_CRIT_PERCENT )); then
    logger -p user.crit -t "$LOG_TAG" "Platte zu ${used} % belegt ($APP_ROOT). Keine Neustarts. Platz schaffen: Einstellungen -> Wartung."
    DISK_FULL=1
  elif (( used >= DISK_WARN_PERCENT )); then
    logger -p user.warning -t "$LOG_TAG" "Platte zu ${used} % belegt ($APP_ROOT)."
  fi
}

check_time_sync() {
  if ! timedatectl show -p NTPSynchronized --value 2>/dev/null | grep -qi "yes"; then
    logger -t "$LOG_TAG" "Zeitsynchronisierung ist nicht aktiv (NTPSynchronized=no)."
    return 1
  fi

  if ! timedatectl show -p SystemClockSynchronized --value 2>/dev/null | grep -qi "yes"; then
    logger -t "$LOG_TAG" "SystemClockSynchronized=no erkannt."
    return 1
  fi

  if command -v chronyc >/dev/null 2>&1; then
    if ! chronyc tracking >/dev/null 2>&1; then
      logger -t "$LOG_TAG" "chronyc tracking fehlgeschlagen."
      return 1
    fi
  fi

  return 0
}

check_and_recover() {
  local service_name="$1"
  local health_url="$2"

  if ! curl --silent --show-error --fail --max-time 10 "$health_url" >/dev/null; then
    if (( DISK_FULL )); then
      logger -t "$LOG_TAG" "Healthcheck fehlgeschlagen fuer $service_name, Platte voll: kein Neustart."
      return 1
    fi
    logger -t "$LOG_TAG" "Healthcheck fehlgeschlagen fuer $service_name ($health_url). Neustart wird ausgefuehrt."
    systemctl restart "$service_name"
    sleep 5

    if ! curl --silent --show-error --fail --max-time 10 "$health_url" >/dev/null; then
      logger -t "$LOG_TAG" "Healthcheck nach Neustart weiterhin fehlerhaft fuer $service_name."
      return 1
    fi

    logger -t "$LOG_TAG" "Dienst $service_name erfolgreich wiederhergestellt."
  fi

  return 0
}

check_disk

if ! check_time_sync; then
  logger -t "$LOG_TAG" "Versuche chrony neu zu starten."
  systemctl restart chrony || true
  sleep 2

  if ! check_time_sync; then
    logger -t "$LOG_TAG" "Zeit-Synchronisierung weiterhin fehlerhaft."
    exit 1
  fi

  logger -t "$LOG_TAG" "Zeit-Synchronisierung nach Neustart wiederhergestellt."
fi

check_and_recover "einsatzueberwachung-server.service" "$SERVER_HEALTH_URL"
check_and_recover "einsatzueberwachung-mobile.service" "$MOBILE_HEALTH_URL"
