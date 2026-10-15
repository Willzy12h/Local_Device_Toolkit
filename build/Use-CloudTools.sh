#!/usr/bin/env bash
# Source this file in the Linux cloud shell; no product service needs starting.
toolkit_tools=${LOCAL_TOOLKIT_TOOLS:-/workspace/.tools}
export DOTNET_ROOT="$toolkit_tools/dotnet"
export DOTNET_CLI_HOME="$toolkit_tools/dotnet-home"
export NUGET_PACKAGES="$toolkit_tools/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export TOOLKIT_TEST_PWSH="$toolkit_tools/powershell/pwsh"
export XDG_CACHE_HOME="$toolkit_tools/powershell-cache"
export XDG_CONFIG_HOME="$toolkit_tools/powershell-config"
export XDG_DATA_HOME="$toolkit_tools/powershell-data"
export PATH="$toolkit_tools/dotnet:$toolkit_tools/powershell:$PATH"
