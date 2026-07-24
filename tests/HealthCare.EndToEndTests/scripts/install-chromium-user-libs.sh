#!/usr/bin/env bash
set -euo pipefail
SYSROOT="${HOME}/playwright-sysroot"
TMP="/tmp/pw-deps-$$"
mkdir -p "${SYSROOT}" "${TMP}"
cd "${TMP}"
PKGS=(
  libatk1.0-0t64
  libatk-bridge2.0-0t64
  libatspi2.0-0t64
  libxcomposite1
  libxdamage1
  libxfixes3
  libxrandr2
  libgbm1
  libasound2t64
  libcups2t64
  libdrm2
  libxkbcommon0
  libpango-1.0-0
  libcairo2
  libnss3
  libnspr4
  libxrender1
  libxi6
)
echo "Downloading base packages into ${TMP}..."
apt-get download "${PKGS[@]}"
for deb in *.deb; do
  dpkg-deb -x "${deb}" "${SYSROOT}"
done
export LD_LIBRARY_PATH="${SYSROOT}/usr/lib/x86_64-linux-gnu:${SYSROOT}/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH:-}"
echo "LD_LIBRARY_PATH=${LD_LIBRARY_PATH}"
echo "Remaining missing libraries:"
ldd /home/speed/.cache/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-linux64/chrome-headless-shell 2>&1 | grep "not found" || echo "ALL LIBS RESOLVED"
