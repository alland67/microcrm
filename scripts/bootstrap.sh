#!/usr/bin/env bash
# One-time scaffold for MicroCRM: .NET 10 minimal API + xUnit v3 + React/TS (Vite) + Vitest.
# Run from the repository root AFTER installing the harness. Safe to re-run: skips what exists.
set -euo pipefail

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required tool: $1"; exit 1; }; }

need dotnet; need node; need npm; need git
DOTNET_MAJOR="$(dotnet --version | cut -d. -f1)"
[ "$DOTNET_MAJOR" -ge 10 ] || { echo "Need .NET SDK 10+, found $(dotnet --version)"; exit 1; }
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]')"
[ "$NODE_MAJOR" -ge 20 ] || { echo "Need Node 20+, found $(node -v)"; exit 1; }

API=src/MicroCrm.Api
TESTS=tests/MicroCrm.Api.Tests

# ---------------------------------------------------------------- backend
say "Solution-level files"
# global.json: pin the SDK and opt dotnet test into Microsoft.Testing.Platform (MTP) mode,
# so the behavior is the same on every machine (MTP needs --solution / --project and xUnit v3 filter flags)
if ! grep -qs 'Microsoft.Testing.Platform' global.json; then
  SDK_VER="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' global.json 2>/dev/null | head -n1)"
  SDK_VER="${SDK_VER:-$(dotnet --version)}"
  cat > global.json <<JSON
{
  "sdk": {
    "version": "$SDK_VER",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
JSON
fi
[ -f MicroCrm.slnx ]    || dotnet new sln -n MicroCrm            # .NET 10 defaults to .slnx
if ! grep -qs '\[Bb\]in' .gitignore; then          # keep any existing lines (e.g. harness entries)
  cp .gitignore .gitignore.keep 2>/dev/null || touch .gitignore.keep
  dotnet new gitignore --force
  { echo; echo "# kept"; cat .gitignore.keep; echo "node_modules/"; echo "*.db"; echo "*.db-shm"; echo "*.db-wal"; } >> .gitignore && rm -f .gitignore.keep
fi
[ -f .editorconfig ]    || dotnet new editorconfig

if [ ! -f Directory.Build.props ]; then
cat > Directory.Build.props <<'XML'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest</AnalysisLevel>
    <!-- xUnit v3 runs natively on Microsoft.Testing.Platform (matches global.json) -->
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <!-- NuGet audit: high/critical advisories (NU1903/NU1904) fail the build; low/moderate stay warnings -->
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
XML
fi

say "API project ($API)"
if [ ! -d "$API" ]; then
  dotnet new webapi -n MicroCrm.Api -o "$API" --framework net10.0
  dotnet sln MicroCrm.slnx add "$API/MicroCrm.Api.csproj"
  # Pin patched Microsoft.OpenApi: the SDK's Microsoft.AspNetCore.OpenApi pulls 2.0.0 transitively,
  # which has GHSA-v5pm-xwqc-g5wc (high). 2.7.5 is the patched 2.x release.
  dotnet add "$API" package Microsoft.OpenApi --version 2.7.5
  # Make Program visible to WebApplicationFactory<Program> in tests
  grep -q 'partial class Program' "$API/Program.cs" || printf '\npublic partial class Program;\n' >> "$API/Program.cs"
fi

say "Test project ($TESTS) with xUnit v3"
if [ ! -d "$TESTS" ]; then
  dotnet new install xunit.v3.templates >/dev/null
  dotnet new xunit3 -n MicroCrm.Api.Tests -o "$TESTS"
  # Target framework comes from Directory.Build.props; drop the template's own value
  sed -i.bak -E '/<TargetFramework>.*<\/TargetFramework>/d' "$TESTS/MicroCrm.Api.Tests.csproj" && rm -f "$TESTS"/*.bak
  dotnet add "$TESTS" reference "$API/MicroCrm.Api.csproj"
  dotnet add "$TESTS" package Microsoft.AspNetCore.Mvc.Testing
  dotnet sln MicroCrm.slnx add "$TESTS/MicroCrm.Api.Tests.csproj"
  rm -f "$TESTS"/UnitTest1.cs
  mkdir -p "$TESTS/Integration" "$TESTS/Unit"
  cat > "$TESTS/Integration/SmokeTests.cs" <<'CS'
using Microsoft.AspNetCore.Mvc.Testing;

namespace MicroCrm.Api.Tests.Integration;

// Harness smoke test: proves the API host boots under WebApplicationFactory.
public sealed class SmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task OpenApiDocument_IsServed_InDevelopment()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Expected 2xx, got {(int)response.StatusCode}");
    }
}
CS
fi

say "Backend build + tests"
grep -q 'Include="Microsoft.OpenApi"' "$API/MicroCrm.Api.csproj" || dotnet add "$API" package Microsoft.OpenApi --version 2.7.5
grep -qs 'UseMicrosoftTestingPlatformRunner' Directory.Build.props || \
  sed -i.bak 's#</PropertyGroup>#  <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>\
  </PropertyGroup>#' Directory.Build.props && rm -f Directory.Build.props.bak
dotnet build MicroCrm.slnx --nologo
dotnet test --solution MicroCrm.slnx

# ---------------------------------------------------------------- frontend
say "Web app (web/) with Vite React + TypeScript"
if [ ! -f web/package.json ]; then
  npm create vite@latest web -- --template react-ts --no-interactive
fi

(
  cd web
  npm install
  npm install @tanstack/react-query
  npm install -D vitest jsdom @testing-library/react @testing-library/jest-dom \
                 @testing-library/user-event msw prettier

  npm pkg set scripts.test="vitest run"
  npm pkg set scripts.test:watch="vitest"
  npm pkg set scripts.typecheck="tsc -b"
  npm pkg set scripts.format="prettier --write ."

  cat > vite.config.ts <<'TS'
/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiTarget = process.env.API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': { target: apiTarget, changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
})
TS

  mkdir -p src/test src/api src/features
  cat > src/test/setup.ts <<'TS'
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => cleanup())
TS

  cat > src/test/smoke.test.tsx <<'TS'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

// Harness smoke test: proves Vitest + jsdom + Testing Library are wired up.
describe('test harness', () => {
  it('renders React into jsdom', () => {
    render(<h1>MicroCRM</h1>)
    expect(screen.getByRole('heading', { name: 'MicroCRM' })).toBeInTheDocument()
  })
})
TS

  [ -f .prettierrc.json ] || printf '{\n  "semi": false,\n  "singleQuote": true,\n  "trailingComma": "all",\n  "printWidth": 100\n}\n' > .prettierrc.json
)

say "Frontend typecheck + tests"
npm --prefix web run typecheck
npm --prefix web test

# ---------------------------------------------------------------- git
say "Git"
[ -d .git ] || git init -q
git add -A
git commit -q -m "chore: bootstrap MicroCRM solution and test harness" || echo "(nothing to commit)"

say "Done. Both suites are green."
echo "Next: open Claude Code here, accept the trust prompt, review docs/adr/0002-stack-and-technical-defaults.md,"
echo "then run:  /spec (see docs/roadmap.md for the suggested first spec)"
