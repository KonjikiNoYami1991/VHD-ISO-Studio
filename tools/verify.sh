#!/usr/bin/env bash
# verify.sh - controlli statici eseguibili anche su Linux (nessuna build dell'exe):
#  1) chiavi di localizzazione IT/EN   2) XAML vs assembly WPF   3) compilazione C# con stub XAML
# Requisiti: python3, .NET SDK 10. Codici di uscita: 0 ok, 1 errore.
set -Eeuo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

echo "== 1/3 stringhe"
python3 "$here/check_strings.py"

echo "== 2/3 XAML"
dotnet restore "$here/CompileCheck/CompileCheck.csproj" -v q >/dev/null
ref="$(ls -d "$HOME"/.nuget/packages/microsoft.windowsdesktop.app.ref/10.*/ref/net10.0 | sort -V | tail -1)"
dotnet build "$here/XamlCheck/XamlCheck.csproj" -c Release -v q -nologo >/dev/null
dotnet "$here/XamlCheck/bin/Release/net10.0/XamlCheck.dll" "$ref" "$here/../src/VhdIsoStudio" "$tmp/Stubs.g.cs"

echo "== 3/3 compilazione C#"
dotnet build "$here/CompileCheck/CompileCheck.csproj" -c Release -nologo -v q -p:StubFile="$tmp/Stubs.g.cs" \
  -warnaserror:CS8600,CS8602,CS8603,CS8604,CS0168,CS0219 2>&1 | grep -E "error|warning|Error|Warn" || true
dotnet build "$here/CompileCheck/CompileCheck.csproj" -c Release -nologo -v q -p:StubFile="$tmp/Stubs.g.cs" >/dev/null
echo "== tutto OK"
