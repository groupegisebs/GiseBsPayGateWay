#!/usr/bin/env bash
# Normalise les variables de déploiement GHA (secrets souvent collés avec tab/CRLF).
# Usage : source deploy/gha-env.sh  puis  gha_write_env

gha_sanitize() {
  printf '%s' "$1" | tr -d '\r\n\t' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^"//' -e 's/"$//' -e "s/^'//" -e "s/'$//"
}

gha_sanitize_host() {
  local h
  h=$(gha_sanitize "$1")
  h="${h#http://}"
  h="${h#https://}"
  h="${h%%/*}"
  printf '%s' "$h"
}

gha_write_env() {
  local name value
  name=$1
  value=$2
  {
    printf '%s<<EOF\n' "$name"
    printf '%s\n' "$value"
    printf 'EOF\n'
  } >> "$GITHUB_ENV"
}
