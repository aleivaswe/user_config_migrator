﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml;

namespace UserConfigMigration
{
    /// <summary>
    /// User configuration settings migration helper class.
    /// </summary>
    public static class UserConfigMigrator
    {
        private const string USER_CONFIG_FILENAME = "user.config";

        private static void ValidateFilename(string filename, string param_name)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentNullException(param_name);
            }
            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"'{param_name}' value contains invalid filename characters: {filename}");
            }
        }

        private static void ValidatePath(string path, string param_name)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException(param_name);
            }
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException($"'{param_name}' value contains invalid path characters: {path}");
            }
        }

        private static FileInfo GetCurrentUserConfigPath(ConfigurationUserLevel user_level_config)
        {
            string user_config_path = ConfigurationManager.OpenExeConfiguration(user_level_config).FilePath;
            ValidatePath(user_config_path, param_name: nameof(user_config_path));
            return new FileInfo(user_config_path);
        }

        private static DirectoryInfo GetUserConfigVersionDir(FileInfo user_config_file)
        {
            return user_config_file.Directory;
        }

        private static Version GetUserConfigVersion(FileInfo user_config_file)
        {
            DirectoryInfo version_dir = GetUserConfigVersionDir(user_config_file);
            Version version;
            if (!Version.TryParse(version_dir.Name, out version))
            {
                throw new InvalidCastException($"'{version_dir}' is not a valid version directory");
            }
            return version;
        }

        private static DirectoryInfo GetUserConfigAssemblyDir(FileInfo user_config_file)
        {
            DirectoryInfo version_dir = GetUserConfigVersionDir(user_config_file);
            return version_dir.Parent;
        }

        private static DirectoryInfo GetUserConfigRootSettingsDir(FileInfo user_config_file)
        {
            DirectoryInfo assembly_dir = GetUserConfigAssemblyDir(user_config_file);
            return assembly_dir.Parent;
        }

        private static DirectoryInfo GetUserConfigAppDataDir(FileInfo user_config_file)
        {
            DirectoryInfo root_settings_dir = GetUserConfigRootSettingsDir(user_config_file);
            return root_settings_dir.Parent;
        }

        private static string GetAssemblyName(string assembly_dir_name)
        {
            // Indicator of assembly directory name is ".exe_Url_" in the middle of the assembly name.
            const string indicator = ".exe_Url_";
            int indicator_start_index = assembly_dir_name.LastIndexOf(indicator, StringComparison.OrdinalIgnoreCase);
            if (indicator_start_index < 0)
            {
                return assembly_dir_name;
            }
            if (indicator_start_index == 0)
            {
                throw new InvalidOperationException(
                    $"'{assembly_dir_name}' assembly name must be at least one character long");
            }
            else if ((indicator_start_index + indicator.Length) >= assembly_dir_name.Length)
            {
                throw new InvalidOperationException(
                    $"'{assembly_dir_name}' assembly hash must be at least one character long");
            }
            string assembly_name = assembly_dir_name.Substring(0, indicator_start_index);
            return Path.GetFileNameWithoutExtension(assembly_name);
        }

        private static bool IsAssemblyDirName(string assembly_dir_name)
        {
            return GetAssemblyName(assembly_dir_name).Length != assembly_dir_name.Length;
        }

        private static AppInfo ConvertToAssemblyNameWithExtension(AppInfo app_info, bool debugging)
        {
            if (app_info == null)
            {
                return null;
            }
            if (IsAssemblyDirName(app_info.AssemblyName))
            {
                return app_info;
            }

            string assembly_name = Path.GetFileNameWithoutExtension(app_info.AssemblyName);
            string extension = debugging ? ".vshost.exe" : ".exe";
            Filename assembly_name_with_extension = new Filename(assembly_name + extension);

            if (app_info.Company != null)
            {
                return new AppInfo(new Filename(app_info.Company), assembly_name_with_extension);
            }
            else if (app_info.RootNamespace != null)
            {
                return new AppInfo(app_info.RootNamespace, assembly_name_with_extension.Value);
            }
            throw new InvalidOperationException(
                $"Either '{nameof(app_info.Company)}' or '{nameof(app_info.RootNamespace)}' must be defined in '{nameof(AppInfo)}'");
        }

        private static string FindLatestUserConfigFile(
            string app_data_dir,
            List<AppInfo> app_infos,
            Version current_app_version,
            bool accept_higher_app_versions)
        {
            Version latest_version = null;
            string latest_user_config_path = null;
            foreach (AppInfo app_info in app_infos)
            {
                string root_settings_path = Path.Combine(app_data_dir, app_info.RootSettingsDirName);
                if (!Directory.Exists(root_settings_path))
                {
                    continue;
                }

                string assembly_name = app_info.AssemblyName;
                if (!string.IsNullOrWhiteSpace(app_info.AssemblyDirName))
                {
                    assembly_name = app_info.AssemblyDirName;
                }

                foreach (string sub_folder in Directory.GetDirectories(root_settings_path))
                {
                    string folder_name = Path.GetFileName(sub_folder);
                    if (!folder_name.StartsWith(assembly_name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string version_folder in Directory.GetDirectories(sub_folder))
                    {
                        string user_config_file = Path.Combine(version_folder, USER_CONFIG_FILENAME);
                        if (!File.Exists(user_config_file))
                        {
                            continue;
                        }

                        string version_raw = Path.GetFileName(version_folder);
                        Version version;
                        if (!Version.TryParse(version_raw, out version))
                        {
                            continue;
                        }
                        if (version == current_app_version)
                        {
                            continue;
                        }
                        if (!accept_higher_app_versions && (version > current_app_version))
                        {
                            continue;
                        }
                        if ((latest_version == null) || (version > latest_version))
                        {
                            latest_version = version;
                            latest_user_config_path = user_config_file;
                        }
                    }
                }
            }
            return latest_user_config_path;
        }

        private static void DebugLog(string line)
        {
            System.Diagnostics.Debug.WriteLine(line);
        }

        /// <summary>
        /// Filename class.
        /// </summary>
        public sealed class Filename
        {
            /// <summary>
            /// The filename string value.
            /// </summary>
            public string Value { get; } = null;

            /// <summary>
            /// Create a new filename instance.
            /// </summary>
            /// <param name="value">The filename string value.</param>
            public Filename(string value)
            {
                ValidateFilename(value, nameof(value));
                Value = value;
            }

            /// <summary>
            /// Get the filename string value.
            /// </summary>
            /// <returns></returns>
            public override string ToString() { return Value; }
        }

        /// <summary>
        /// Application information data class.
        /// </summary>
        public class AppInfo
        {
            /// <summary>
            /// The root namespace (defined as 'Default namescape' in project application settings).
            /// </summary>
            public string RootNamespace { get; } = null;

            /// <summary>
            /// The company (defined as 'Company' in project assembly information).
            /// </summary>
            public string Company { get; } = null;

            /// <summary>
            /// The assembly name (defined as 'Assembly name' in project application settings).
            /// </summary>
            public string AssemblyName { get; } = null;

            /// <summary>
            /// The assembly directory name (obtained from the current user configuration file).
            /// </summary>
            internal string AssemblyDirName { get; } = null;

            /// <summary>
            /// The root settings directory name (either <see cref="Company"/> or <see cref="RootNamespace"/>).
            /// </summary>
            internal string RootSettingsDirName
            {
                get
                {
                    if (Company != null)
                    {
                        return Company;
                    }
                    else if (RootNamespace != null)
                    {
                        return RootNamespace;
                    }
                    throw new InvalidOperationException(
                        $"Either '{nameof(Company)}' or '{nameof(RootNamespace)}' must be defined");
                }
            }

            /// <summary>
            /// Create a new application information instance.
            /// </summary>
            /// <param name="root_namespace">The application root namespace.</param>
            /// <param name="assembly_name">The application assembly name.</param>
            public AppInfo(string root_namespace, string assembly_name)
            {
                ValidateFilename(root_namespace, nameof(root_namespace));
                ValidateFilename(assembly_name, nameof(assembly_name));
                RootNamespace = root_namespace;
                AssemblyName = assembly_name;
            }

            /// <summary>
            /// Create a new application information instance.
            /// </summary>
            /// <param name="company">The application company.</param>
            /// <param name="assembly_name">The application assembly name.</param>
            public AppInfo(Filename company, Filename assembly_name)
            {
                if (company == null)
                {
                    throw new ArgumentNullException(nameof(company));
                }
                if (assembly_name == null)
                {
                    throw new ArgumentNullException(nameof(assembly_name));
                }
                Company = company.Value;
                AssemblyName = assembly_name.Value;
            }

            /// <summary>
            /// Create a new application information instance.
            /// </summary>
            /// <param name="company">The application company.</param>
            /// <param name="assembly_name">The application assembly name.</param>
            /// <param name="assembly_dir_name">The application assembly directory name.</param>
            /// <exception cref="ArgumentNullException"></exception>
            internal AppInfo(Filename company, Filename assembly_name, Filename assembly_dir_name)
            {
                if (company == null)
                {
                    throw new ArgumentNullException(nameof(company));
                }
                if (assembly_name == null)
                {
                    throw new ArgumentNullException(nameof(assembly_name));
                }
                if (assembly_dir_name == null)
                {
                    throw new ArgumentNullException(nameof(assembly_dir_name));
                }
                Company = company.Value;
                AssemblyName = assembly_name.Value;
                AssemblyDirName = assembly_dir_name.Value;
            }
        }

        /// <summary>
        /// Get application information based from the current user configuration file.
        /// </summary>
        /// <param name="current_app_version">The current application version based from the current user configuration file.</param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <returns>The application information based from the current user configuration file.</returns>
        public static AppInfo GetAppInfoFromCurrentUserConfig(
            out Version current_app_version,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoamingAndLocal)
        {
            FileInfo user_config_path = GetCurrentUserConfigPath(user_level_config);
            Filename root_settings_dir_name = new Filename(GetUserConfigRootSettingsDir(user_config_path).Name);
            Filename assembly_dir_name = new Filename(GetUserConfigAssemblyDir(user_config_path).Name);
            Filename assembly_name = new Filename(GetAssemblyName(assembly_dir_name.Value));
            current_app_version = GetUserConfigVersion(user_config_path);

            return new AppInfo(
                company: root_settings_dir_name,
                assembly_name: assembly_name,
                assembly_dir_name: assembly_dir_name);
        }

        /// <summary>
        /// Find the latest user configuration file stored in local application settings.
        /// </summary>
        /// <param name="current_app_version">The current application version.</param>
        /// <param name="current_app_info">The current application information.</param>
        /// <param name="user_config_path">The path to the located user configuration file.</param>
        /// <param name="accept_higher_app_versions"><b>Optional:</b> If true a user configuration file from higher application versions is allowed in the search.
        /// <para>Useful when downgrading an application and settings from higher application versions shall be included in the search.</para></param>
        /// <param name="previous_app_infos"><b>Optional:</b> If defined, the search is extended to all application information items provided.
        /// <para>Useful when application information (root namespace or assembly name) have been renamed and settings from previous versions shall be included in the search.</para></param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool TryFindLatestUserConfig(
            Version current_app_version,
            AppInfo current_app_info,
            out string user_config_path,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoamingAndLocal)
        {
            if (current_app_version == null)
            {
                throw new ArgumentNullException(nameof(current_app_version));
            }
            if (current_app_info == null)
            {
                throw new ArgumentNullException(nameof(current_app_info));
            }

            FileInfo current_user_config_file = GetCurrentUserConfigPath(user_level_config);
            string app_data_dir = GetUserConfigAppDataDir(current_user_config_file).FullName;

            /*
             * Priority order for user configuration file search:
             * 1. Current app info, if assembly directory name is defined
             * 2. If debugging and no assembly directory name, current app info with debug assembly name extension
             * 3. If debugging, previous app infos with debug assembly name extension
             * 4. If no assembly directory name, current app info with .exe assembly name extension
             * 5. Previous app infos with .exe assembly name extension
             */
            bool assembly_dir_is_defined = !string.IsNullOrWhiteSpace(current_app_info.AssemblyDirName);
            bool debugging = System.Diagnostics.Debugger.IsAttached;

            user_config_path = null;
            if (assembly_dir_is_defined)
            {
                user_config_path = FindLatestUserConfigFile(
                    app_data_dir,
                    new List<AppInfo>() { current_app_info },
                    current_app_version,
                    accept_higher_app_versions);
            }
            if (debugging)
            {
                if ((user_config_path == null) && !assembly_dir_is_defined)
                {
                    user_config_path = FindLatestUserConfigFile(
                        app_data_dir,
                        new List<AppInfo>() { ConvertToAssemblyNameWithExtension(current_app_info, debugging: true) },
                        current_app_version,
                        accept_higher_app_versions);
                }
                if ((user_config_path == null) && (previous_app_infos != null))
                {
                    user_config_path = FindLatestUserConfigFile(
                        app_data_dir,
                        previous_app_infos
                            .Where(x => x != null)
                            .Select(x => ConvertToAssemblyNameWithExtension(x, debugging: true))
                            .ToList(),
                        current_app_version,
                        accept_higher_app_versions);
                }
            }
            if ((user_config_path == null) && !assembly_dir_is_defined)
            {
                user_config_path = FindLatestUserConfigFile(
                    app_data_dir,
                    new List<AppInfo>() { ConvertToAssemblyNameWithExtension(current_app_info, debugging: false) },
                    current_app_version,
                    accept_higher_app_versions);    
            }
            if ((user_config_path == null) && (previous_app_infos != null))
            {
                user_config_path = FindLatestUserConfigFile(
                    app_data_dir,
                    previous_app_infos
                        .Where(x => x != null)
                        .Select(x => ConvertToAssemblyNameWithExtension(x, debugging: false))
                        .ToList(),
                    current_app_version,
                    accept_higher_app_versions);
            }
            return (user_config_path != null);
        }

        /// <summary>
        /// Export setting from the user configuration file.
        /// </summary>
        /// <param name="user_config_path">Path to the user configuration file.</param>
        /// <param name="settings_to_export">Collection of settings to export.</param>
        /// <returns>Dictionary of exported setting <c>(name, value)</c> key, value pairs.</returns>
        /// <remarks>
        /// <b>Note:</b> Only settings available in the export setting collection are exported.
        /// <para>Any additional settings in the user configuration file, that have no match in the export setting collection, are ignored.</para>
        /// </remarks>
        public static IDictionary<string, object> ExportSettings(
            string user_config_path,
            SettingsBase settings_to_export)
        {
            ValidatePath(user_config_path, nameof(user_config_path));
            if (!user_config_path.EndsWith(Path.Combine("", USER_CONFIG_FILENAME), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{user_config_path}' must end with '{USER_CONFIG_FILENAME}'");
            }
            if (!File.Exists(user_config_path))
            {
                throw new FileNotFoundException($"'{user_config_path}' file does not exist");
            }
            if (settings_to_export == null)
            {
                throw new ArgumentNullException(nameof(settings_to_export));
            }

            XmlDocument xml_doc = new XmlDocument();
            try
            {
                xml_doc.Load(user_config_path);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{user_config_path}' XML file load failure message: {ex.Message}");
            }

            XmlNodeList setting_nodes = xml_doc.SelectNodes("//userSettings/*/setting");
            if (setting_nodes == null)
            {
                return new Dictionary<string, object>();
            }

            Dictionary<string, object> exported_settings = new Dictionary<string, object>();
            foreach (XmlNode setting_node in setting_nodes)
            {
                string setting_name = setting_node.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(setting_name))
                {
                    DebugLog($"Setting node '{setting_node.OuterXml}' is missing a 'name' attribute");
                    continue;
                }

                SettingsProperty settings_property = settings_to_export.Properties[setting_name];
                if (settings_property == null)
                {
                    DebugLog($"Setting with name '{setting_name}' is not found in export setting collection");
                    continue;
                }

                Type setting_type = settings_property.PropertyType;
                string setting_value_raw = setting_node.SelectSingleNode("value")?.InnerText;
                try
                {
                    object setting_value = Convert.ChangeType(setting_value_raw, setting_type);
                    exported_settings.Add(setting_name, setting_value);
                }
                catch (Exception ex)
                {
                    DebugLog(
                        $"Failed to export setting '{setting_name}' raw value '{setting_value_raw}' as type '{setting_type}': {ex.Message}");
                }
            }
            return exported_settings;
        }

        /// <summary>
        /// Apply imported settings into the target settings object.
        /// </summary>
        /// <param name="imported_settings">Dictionary of imported settings from a user configuration file.</param>
        /// <param name="target_settings">The target settings object where the imported settings shall be applied.</param>
        /// <remarks>
        /// <b>Note:</b> Only settings available in the target settings object are applied.
        /// <para>Any additional settings in the dictionary of imported settings, that have no match in the target settings object, are ignored.</para>
        /// </remarks>
        public static void ApplySettings(
            IDictionary<string, object> imported_settings,
            SettingsBase target_settings)
        {
            if (imported_settings == null)
            {
                throw new ArgumentNullException(nameof(imported_settings));
            }
            if (target_settings == null)
            {
                throw new ArgumentNullException(nameof(target_settings));
            }

            foreach (KeyValuePair<string, object> kvp in imported_settings)
            {
                string setting_name = kvp.Key;
                object setting_value = kvp.Value;

                if (string.IsNullOrEmpty(setting_name))
                {
                    DebugLog($"Setting with value '{setting_value}' is missing a 'key'");
                    continue;
                }

                SettingsProperty settings_property = target_settings.Properties[setting_name];
                if (settings_property == null)
                {
                    DebugLog($"Setting with name '{setting_name}' is not found in target setting collection");
                    continue;
                }

                Type setting_type = settings_property.PropertyType;
                try
                {
                    target_settings[setting_name] = setting_value;
                }
                catch (Exception ex)
                {
                    DebugLog(
                        $"Failed to apply setting '{setting_name}' value '{setting_value}' as type '{setting_type}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Find the latest user configuration file stored in local application settings, export and apply settings into the target settings object.
        /// </summary>
        /// <param name="current_app_version">The current application version.</param>
        /// <param name="current_app_info">The current application information.</param>
        /// <param name="user_config_path">The path to the located user configuration file.</param>
        /// <param name="target_settings">The target settings object where the imported settings shall be applied.</param>
        /// <param name="accept_higher_app_versions"><b>Optional:</b> If true a user configuration file from higher application versions is allowed in the search.
        /// <para>Useful when downgrading an application and settings from higher application versions shall be included in the search.</para></param>
        /// <param name="previous_app_infos"><b>Optional:</b> If defined, the search is extended to all application information items provided.
        /// <para>Useful when application information (root namespace or assembly name) have been renamed and settings from previous versions shall be included in the search.</para></param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool UpgradeSettings(
            Version current_app_version,
            AppInfo current_app_info,
            out string user_config_path,
            SettingsBase target_settings,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoamingAndLocal)
        {
            user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos,
                user_level_config))
            {
                return false;
            }

            IDictionary<string, object> exported_settings = ExportSettings(user_config_path, target_settings);
            ApplySettings(exported_settings, target_settings);
            return true;
        }

        /// <summary>
        /// Find the latest user configuration file stored in local application settings, export and apply settings into the target settings object.
        /// </summary>
        /// <param name="current_app_version">The current application version.</param>
        /// <param name="current_app_info">The current application information.</param>
        /// <param name="target_settings">The target settings object where the imported settings shall be applied.</param>
        /// <param name="accept_higher_app_versions"><b>Optional:</b> If true a user configuration file from higher application versions is allowed in the search.
        /// <para>Useful when downgrading an application and settings from higher application versions shall be included in the search.</para></param>
        /// <param name="previous_app_infos"><b>Optional:</b> If defined, the search is extended to all application information items provided.
        /// <para>Useful when application information (root namespace or assembly name) have been renamed and settings from previous versions shall be included in the search.</para></param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool UpgradeSettings(
            Version current_app_version,
            AppInfo current_app_info,
            SettingsBase target_settings,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoamingAndLocal)
        {
            string user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos,
                user_level_config))
            {
                return false;
            }

            IDictionary<string, object> exported_settings = ExportSettings(user_config_path, target_settings);
            ApplySettings(exported_settings, target_settings);
            return true;
        }

        /// <summary>
        /// Find the latest user configuration file stored in local application settings, export and apply settings into the target settings object.
        /// </summary>
        /// <param name="target_settings">The target settings object where the imported settings shall be applied.</param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <param name="accept_higher_app_versions"><b>Optional:</b> If true a user configuration file from higher application versions is allowed in the search.
        /// <para>Useful when downgrading an application and settings from higher application versions shall be included in the search.</para></param>
        /// <param name="previous_app_infos"><b>Optional:</b> If defined, the search is extended to all application information items provided.
        /// <para>Useful when application information (root namespace or assembly name) have been renamed and settings from previous versions shall be included in the search.</para></param>
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool UpgradeSettings(
            SettingsBase target_settings,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoamingAndLocal,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null)
        {
            Version current_app_version;
            AppInfo current_app_info = GetAppInfoFromCurrentUserConfig(
                out current_app_version,
                user_level_config);

            string user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos,
                user_level_config))
            {
                return false;
            }

            IDictionary<string, object> exported_settings = ExportSettings(user_config_path, target_settings);
            ApplySettings(exported_settings, target_settings);
            return true;
        }
    }
}
