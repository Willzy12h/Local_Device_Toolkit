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
# PS7 is a build/test dependency for read-only portable package verification, not product runtime.
source "$toolkit_repository/build/Use-CloudTools.sh"
mkdir -p "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME"
if [[ ! -x "$TOOLKIT_TEST_PWSH" ]]; then
  [[ ! -e "$toolkit_tools/powershell" ]] || { echo 'PowerShell destination is incomplete; inspect it before replacing.' >&2; exit 1; }
  "$toolkit_sdk" tool install PowerShell --tool-path "$toolkit_tools/powershell" --version 7.6.0 --add-source https://api.nuget.org/v3/index.json
fi
toolkit_ps_archive="$toolkit_tools/powershell/.store/powershell/7.6.0/powershell/7.6.0/powershell.7.6.0.nupkg"
echo '2cb1a4c4d2e9f142367ba46675912608012d051e4c1ad49a2be7bebc44627c1bf22601f1401704b19df43ca24adb0ab664c187dfe15bdc2006555babf908e79f  '"$toolkit_ps_archive" | sha512sum --check --status
[[ "$("$TOOLKIT_TEST_PWSH" -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()')" == '7.6.0' ]] || { echo 'PowerShell differs from the reviewed tool version.' >&2; exit 1; }
cd -- "$toolkit_repository"
"$toolkit_sdk" restore LocalDeviceToolkit.sln --locked-mode
"$toolkit_sdk" --version
