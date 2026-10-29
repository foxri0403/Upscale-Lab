#!/usr/bin/env bash
# Manual frontend-only deployment. Does not touch the API checkout, service or database.
set -euo pipefail
archive=$(realpath "$1")
config=$(realpath "$2")
release=$3
case "$release" in web-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]) ;; *) echo 'Invalid release name' >&2; exit 1;; esac
base=/var/www/upscale-lab
destination="$base/releases/$release"
backup="$HOME/deploy-backups/web/$release"
nginx_config=/etc/nginx/sites-available/upscale-lab
test -f "$archive" && test -f "$config"
test ! -e "$destination"
test ! -e "$base/current" || test -L "$base/current"
mkdir -p "$backup"
sudo cp "$nginx_config" "$backup/nginx.conf"
previous=$(readlink "$base/current" || true)
printf '%s\n' "$previous" > "$backup/previous-release.txt"
sudo mkdir -p "$destination"
sudo tar -xzf "$archive" -C "$destination"
sudo test -f "$destination/index.html"
sudo test -f "$destination/config.js"
sudo find "$destination" -type d -exec chmod 755 {} +
sudo find "$destination" -type f -exec chmod 644 {} +
rollback() {
  sudo cp "$backup/nginx.conf" "$nginx_config"
  if [ -n "$previous" ]; then
    sudo ln -s "$previous" "$base/rollback-$release"
    sudo mv -Tf "$base/rollback-$release" "$base/current"
  fi
  sudo nginx -t && sudo systemctl reload nginx
}
trap rollback ERR
sudo ln -s "$destination" "$base/next-$release"
sudo mv -Tf "$base/next-$release" "$base/current"
sudo cp "$config" "$nginx_config"
sudo nginx -t
sudo systemctl reload nginx
curl --fail --silent http://127.0.0.1/health
printf '\nFrontend deployed: %s\nBackup: %s\n' "$destination" "$backup"
trap - ERR
