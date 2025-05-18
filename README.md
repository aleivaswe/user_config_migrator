# User configuration migrator

[![GitHub release](https://img.shields.io/github/v/release/aleivaswe/user_config_migrator.svg)](https://github.com/aleivaswe/user_config_migrator/releases)
![Framework](https://img.shields.io/badge/.NET_Framework-4.8-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Status: Public](https://img.shields.io/badge/status-public-brightgreen)
[![Source Link](https://img.shields.io/badge/Source%20Link-enabled-brightgreen)](https://github.com/dotnet/sourcelink)

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

### Direct upgrading of user settings
```csharp
UserConfigMigrator.UpgradeSettings(Properties.Settings.Default);
```

### Direct upgrading of user settings with support for previous app versions
```csharp
string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
string oldCompanyName = "oldCompanyName";

IReadOnlyCollection<UserConfigMigrator.AppInfo> previousAppInfos =
    new ReadOnlyCollection<UserConfigMigrator.AppInfo>(
        new List<UserConfigMigrator.AppInfo>()
        {
            new UserConfigMigrator.AppInfo(
                company: new UserConfigMigrator.Filename(oldCompanyName),
                assembly_name: new UserConfigMigrator.Filename(assemblyName))
        });

UserConfigMigrator.UpgradeSettings(Properties.Settings.Default, previous_app_infos: previousAppInfos);
```

### Step-by-step upgrading of user settings
```csharp
ConfigurationUserLevel userLevelConf = ConfigurationUserLevel.PerUserRoaming;

Version appVersion;
UserConfigMigrator.AppInfo appInfo =
    UserConfigMigrator.GetAppInfoFromCurrentUserConfig(out appVersion, userLevelConf);

string userConfigPath;
if (UserConfigMigrator.TryFindLatestUserConfig(appVersion, appInfo, out userConfigPath))
{
    IDictionary<string, object> exportedSettings =
        UserConfigMigrator.ExportSettings(userConfigPath, Properties.Settings.Default);

    UserConfigMigrator.ApplySettings(exportedSettings, Properties.Settings.Default);
}
```
