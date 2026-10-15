#!/usr/bin/env bash
set -euo pipefail
# Linux build/review host only. The WPF product runs natively on Windows.
toolkit_repository=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
toolkit_tools=${LOCAL_TOOLKIT_TOOLS:-/workspace/.tools}
mkdir -p "$toolkit_tools"
export DOTNET_CLI_HOME="$toolkit_tools/dotnet-home"
export NUGET_PACKAGES="$toolkit_tools/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
toolkit_sdk="$toolkit_tools/dotnet/dotnet"
if [[ -x "$toolkit_sdk" ]]; then
  [[ "$("$toolkit_sdk" --version)" == '10.0.401' ]] || { echo 'Existing SDK differs from the reviewed version; inspect it before replacing.' >&2; exit 1; }
else
  [[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || { echo 'This setup script requires Linux x64.' >&2; exit 1; }
  [[ ! -e "$toolkit_tools/dotnet" ]] || { echo 'SDK destination exists but is incomplete; inspect it before replacing.' >&2; exit 1; }
  toolkit_stage=$(mktemp -d "$toolkit_tools/local-toolkit-sdk.XXXXXXXX")
  trap 'rm -rf -- "$toolkit_stage"' EXIT
  curl --fail --location --retry 3 --proto '=https' --tlsv1.2 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz' --output "$toolkit_stage/sdk.tar.gz"
  echo '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b  '"$toolkit_stage/sdk.tar.gz" | sha512sum --check --status
  mkdir "$toolkit_stage/sdk"
  tar -xzf "$toolkit_stage/sdk.tar.gz" -C "$toolkit_stage/sdk"
  mv -- "$toolkit_stage/sdk" "$toolkit_tools/dotnet"
fi
cd -- "$toolkit_repository"
"$toolkit_sdk" restore LocalDeviceToolkit.sln --locked-mode
"$toolkit_sdk" --version
