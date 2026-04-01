#!/usr/bin/env bash

set -euo pipefail

SERVER_HEALTH_URL="${SERVER_HEALTH_URL:-http://127.0.0.1:5000/health}"
MOBILE_HEALTH_URL="${MOBILE_HEALTH_URL:-http://127.0.0.1:5001/health}"
LOG_TAG="einsatz-health-check"

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
