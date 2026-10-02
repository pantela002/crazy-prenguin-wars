#!/usr/bin/env bash
# Compile-checks the game's C# outside Unity (for CI/agents without a Unity install).
# Uses Unity 2021.3 reference assemblies from NuGet and uGUI built from source, so it catches
# syntax/type errors, not Unity-6-only API differences. Usage: Tools/compile_check.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REFS="${CPW_REFS:-$HOME/.cpw-refs}"
if [ ! -f "$REFS/ok" ]; then
  mkdir -p "$REFS" && cd "$REFS"
  curl -sL -o m.nupkg https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
  unzip -o -q m.nupkg -d mods
  curl -sL -o s.nupkg https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg
  unzip -o -q s.nupkg -d sdk
  rm -rf ugui && git clone -q --depth 1 -b 2018.4 https://github.com/Unity-Technologies/uGUI ugui
  U=ugui/UnityEngine.UI/UI/Core/Utility
  sed -i -E 's/UnityEngineInternal\.ScriptingUtils\.CreateDelegate\([^;]*\)( as [A-Za-z0-9_]+)?;/null;/' $U/ReflectionMethodsCache.cs
  sed -i -E 's/^(\s*)CanvasRenderer\.(AddUIVertexStream|CreateUIVertexStream|SplitUIVertexStreams)\(.*m_Uv0S.*\);/\1\/\/stub/' $U/VertexHelper.cs
  mkdir -p uiproj && cat > uiproj/ui.csproj <<'P'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>UnityEngine.UI</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>CS0618;CS0067;CS0414;CS0649;CS0169;CS0108;CS0114</NoWarn><AllowUnsafeBlocks>true</AllowUnsafeBlocks><DefineConstants>PACKAGE_PHYSICS;PACKAGE_PHYSICS2D;PACKAGE_ANIMATION</DefineConstants></PropertyGroup>
  <ItemGroup><Compile Include="../ugui/UnityEngine.UI/**/*.cs" /></ItemGroup>
  <ItemGroup><Reference Include="../mods/lib/netstandard2.0/*.dll" /></ItemGroup>
</Project>
P
  (cd uiproj && dotnet build -nologo -v q -o ../uiout >/dev/null)
  touch ok
fi
OUT="${TMPDIR:-/tmp}/cpw-compile"
mkdir -p "$OUT/rt" "$OUT/ed"
cat > "$OUT/rt/rt.csproj" <<P
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>Assembly-CSharp</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable><NoWarn>CS0618;CS0414;CS0649;CS0169;CS0067</NoWarn><DefineConstants>UNITY_ANDROID;UNITY_2021_3_OR_NEWER</DefineConstants></PropertyGroup>
  <ItemGroup><Compile Include="$ROOT/Assets/**/*.cs" Exclude="$ROOT/Assets/**/Editor/**/*.cs" /></ItemGroup>
  <ItemGroup><Reference Include="$REFS/mods/lib/netstandard2.0/*.dll" /><Reference Include="$REFS/uiout/UnityEngine.UI.dll" /></ItemGroup>
</Project>
P
cat > "$OUT/ed/ed.csproj" <<P
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>Assembly-CSharp-Editor</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>CS0618;CS0414;CS0649;CS0169;CS0067;CS1701;CS1702</NoWarn><DefineConstants>UNITY_EDITOR;UNITY_2021_3_OR_NEWER</DefineConstants></PropertyGroup>
  <ItemGroup><Compile Include="$ROOT/Assets/**/Editor/**/*.cs" /></ItemGroup>
  <ItemGroup><Reference Include="$REFS/mods/lib/netstandard2.0/*.dll" /><Reference Include="$REFS/uiout/UnityEngine.UI.dll" /><Reference Include="$REFS/sdk/lib/UnityEditor.dll" /><Reference Include="$OUT/rt/bin/Assembly-CSharp.dll" /></ItemGroup>
</Project>
P
echo "== runtime"
(cd "$OUT/rt" && dotnet build -nologo -v q -o bin 2>&1 | grep -E "error|Build succeeded" | sed "s#$ROOT/##" | sort -u | head -${MAXERR:-60})
if [ -n "$(find "$ROOT/Assets" -path "*/Editor/*.cs" | head -1)" ]; then
  echo "== editor"
  (cd "$OUT/ed" && dotnet build -nologo -v q -o bin 2>&1 | grep -E "error|Build succeeded" | sed "s#$ROOT/##" | sort -u | head -${MAXERR:-60})
fi
