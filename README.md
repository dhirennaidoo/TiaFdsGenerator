# TiaFdsGenerator

Milestone 0.1.0 provides an x64 command-line reader for TIA Portal V15.1 Update 4 projects and archives. It opens `.ap15_1` projects, retrieves `.zap15_1` archives, and prints the project name, project path, and top-level device names. PLC software-container enumeration is intentionally outside this milestone.

## Prerequisites

- Visual Studio 2022 with the **.NET desktop development** workload
- .NET Framework 4.8 Developer Pack
- TIA Portal V15.1 Update 4 with Openness installed
- A 64-bit Windows environment

The Windows account running the tool must be a member of the **Siemens TIA Openness** local user group. Sign out and back in after the account is added so the group membership takes effect.

## Siemens reference

Only `TiaFds.Openness` references `Siemens.Engineering.dll`. The project expects it at:

```text
Openness API\V15.1\Siemens.Engineering.dll
```

The reference has `Private`/Copy Local set to `false`. The Siemens DLL is proprietary and deliberately ignored by Git; obtain it from the installed TIA Portal V15.1 PublicAPI/Openness installation or place a local development copy at the path above. Do not commit it.

At runtime, the CLI resolves the exact `Siemens.Engineering` version `15.1.0.0` from the 64-bit Siemens Openness registry entries below `HKLM\SOFTWARE\Siemens\Automation\Openness`. The registered assembly path, version, and public key token are validated before the DLL is loaded. This allows the TIA Portal installation directory to differ between the development and execution systems without packaging the proprietary DLL.

To inspect the target machine's Openness registration:

```powershell
reg query "HKLM\SOFTWARE\Siemens\Automation\Openness" /s /reg:64
```

## Build

Open `TiaFdsGenerator.sln` in Visual Studio 2022, select `Debug | x64` or `Release | x64`, and build the solution.

From a Visual Studio Developer PowerShell prompt:

```powershell
msbuild .\TiaFdsGenerator.sln /restore /p:Configuration=Debug /p:Platform=x64
```

## Run

Open an existing project:

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.ap15_1"
```

Retrieve and inspect an archive:

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.zap15_1" --retrieve-to "C:\TIA\Retrieved"
```

`--retrieve-to` is required for `.zap15_1` input. Run the executable on a machine with the matching TIA Portal V15.1 Update 4 Openness runtime installed. Retrieved projects and generated build output must remain uncommitted.
