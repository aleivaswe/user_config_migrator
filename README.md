# User configuration migrator

[![GitHub release](https://img.shields.io/github/v/release/aleivaswe/user_config_migrator.svg)](https://github.com/aleivaswe/user_config_migrator/releases)
![Framework](https://img.shields.io/badge/.NET_Framework-4.8-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Status: Public](https://img.shields.io/badge/status-public-brightgreen)

A helper utility for migrating user-scoped application settings between different versions of a .NET application.

Useful when:
- Application version upgrades should reuse settings from older versions.
- Application **root namespace** or **assembly name** has changed.
- Supports downgrades by allowing import of settings from future versions.

> ⚠️ **Scope:** This is a minimal utility tailored for internal use in .NET Framework (e.g., 4.8) desktop applications that store settings using `SettingsBase` and `user.config`.  
It does **not** support roaming profiles, custom configuration files, or non-standard config structures.

## Features

- Finds the most recent valid `user.config` file from previous application versions.
- Exports settings from a `user.config` file.
- Applies exported settings from a `user.config` file into the current application settings, skipping unknown fields.

## 📦 NuGet

[![NuGet](https://img.shields.io/nuget/v/UserConfigMigrator.svg)](https://www.nuget.org/packages/UserConfigMigrator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/UserConfigMigrator.svg)](https://www.nuget.org/packages/UserConfigMigrator)

Install via NuGet Package Manager:
```bash
Install-Package UserConfigMigrator
```

Or via .NET CLI:
```bash
dotnet add package UserConfigMigrator
```

## Usage examples

### Step-by-step upgrading of user settings
```csharp
Version appVersion = Assembly.GetExecutingAssembly().GetName().Version;
string rootNamespace = "MyAppRootNamespace";
string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;

string userConfigPath;
if (UserConfigMigrator.TryFindLatestUserConfig(
        appVersion,
        new UserConfigMigrator.AppInfo(rootNamespace, assemblyName),
        out userConfigPath))
{
    IDictionary<string, object> exportedSettings =
        UserConfigMigrator.ExportSettings(userConfigPath, Properties.Settings.Default);

    UserConfigMigrator.ApplySettings(exportedSettings, Properties.Settings.Default);
}
```

### Direct upgrading of user settings
```csharp
Version appVersion = Assembly.GetExecutingAssembly().GetName().Version;
string rootNamespace = "MyAppRootNamespace";
string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;

UserConfigMigrator.UpgradeSettings(
    appVersion,
    new UserConfigMigrator.AppInfo(rootNamespace, assemblyName),
    Properties.Settings.Default);
```

> 💡 **Tip:** Unsure of your root namespace?  
> Define a dummy class (e.g. `Root`) in the project root and use:

```csharp
string rootNamespace = typeof(Root).Namespace;
```
> This helps ensure you reference the actual root namespace from project settings, which may differ from folder names.
