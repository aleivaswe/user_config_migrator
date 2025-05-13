using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Xml;

namespace UserConfigMigration
{
    /// <summary>
    /// User configuration settings migration helper class.
    /// </summary>
    public static class UserConfigMigrator
    {
        private const string USER_CONFIG_FILENAME = "user.config";

        private static void ValidateFilename(string filename, string name)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentNullException($"'{name}' must be defined");
            }
            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"'{name}' value contains invalid filename characters: {filename}");
            }
        }

        private static void ValidatePath(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException($"'{name}' must be defined");
            }
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException($"'{name}' value contains invalid path characters: {path}");
            }
        }

        private static void DebugLog(string line)
        {
            System.Diagnostics.Debug.WriteLine(line);
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
            /// The assembly name (defined as 'Assembly name' in project application settings).
            /// </summary>
            public string AssemblyName { get; } = null;

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
        }

        /// <summary>
        /// Get application information based from the current user configuration file.
        /// </summary>
        /// <param name="current_app_version">The current application version based from the current user configuration file.</param>
        /// <param name="user_level_config"><b>Optional:</b> User level configuration.</param>
        /// <returns>The application information based from the current user configuration file.</returns>
        public static AppInfo GetAppInfoFromCurrentUserConfig(
            out Version current_app_version,
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoaming)
        {
            string user_config_path = ConfigurationManager.OpenExeConfiguration(user_level_config).FilePath;
            ValidatePath(user_config_path, name: nameof(user_config_path));

            DirectoryInfo version_dir = new DirectoryInfo(Path.GetDirectoryName(user_config_path));
            if (!Version.TryParse(version_dir.Name, out current_app_version))
            {
                throw new InvalidCastException($"'{version_dir}' is not a valid version directory");
            }

            /*
             * Assembly sub-folder format (Visual Studio only): 
             * Production: {ASSEMBLY_NAME}.exe_Url_{HASH}
             * Debugging : {ASSEMBLY_NAME}.vshost.exe_Url_{HASH}
             */
            DirectoryInfo assembly_dir = version_dir.Parent;
            int last_underscore_index = assembly_dir.Name.LastIndexOf("_Url_", StringComparison.OrdinalIgnoreCase);
            if (last_underscore_index < 0)
            {
                throw new InvalidOperationException($"'{assembly_dir}' is not a valid assembly directory");
            }
            string assembly_name = assembly_dir.Name.Substring(0, last_underscore_index);
            if (!assembly_name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"'{assembly_name}' assembly name does not end with '.exe'");
            }
            assembly_name = Path.GetFileNameWithoutExtension(assembly_name);

            DirectoryInfo company_or_root_namespace_dir = assembly_dir.Parent;
            string company_or_root_namespace = company_or_root_namespace_dir.Name;

            return new AppInfo(company_or_root_namespace, assembly_name);
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
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool TryFindLatestUserConfig(
            Version current_app_version,
            AppInfo current_app_info,
            out string user_config_path,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null)
        {
            if (current_app_version == null)
            {
                throw new ArgumentNullException($"'{nameof(current_app_version)}' must be defined");
            }
            if (current_app_info == null)
            {
                throw new ArgumentNullException($"'{nameof(current_app_info)}' must be defined");
            }

            List<AppInfo> app_infos = new List<AppInfo>() { current_app_info };
            if (previous_app_infos != null)
            {
                app_infos.AddRange(previous_app_infos);
            }

            // Root app settings folder: %LOCALAPPDATA%/{ROOT_NAMESPACE}
            string local_app_data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            /*
             * App settings sub-folder format (Visual Studio only): 
             * Production: {ASSEMBLY_NAME}.exe_Url_{HASH}
             * Debugging : {ASSEMBLY_NAME}.vshost.exe_Url_{HASH}
             */
            bool is_debugging = System.Diagnostics.Debugger.IsAttached;
            string exe_prefix = is_debugging ? ".vshost.exe" : ".exe";

            // Find latest version and the path to the user config file
            Version latest_version = null;
            string latest_user_config_path = null;
            foreach (AppInfo app_info in app_infos)
            {
                string root_settings_path = Path.Combine(local_app_data, app_info.RootNamespace);
                if (!Directory.Exists(root_settings_path))
                {
                    continue;
                }

                string sub_folder_prefix = $"{app_info.AssemblyName}{exe_prefix}_Url_";

                foreach (string sub_folder in Directory.GetDirectories(root_settings_path))
                {
                    string folder_name = Path.GetFileName(sub_folder);
                    if (!folder_name.StartsWith(sub_folder_prefix, StringComparison.OrdinalIgnoreCase))
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

            user_config_path = latest_user_config_path;
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
                throw new ArgumentNullException($"'{nameof(settings_to_export)}' must be defined");
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
                throw new ArgumentNullException($"'{nameof(imported_settings)}' must be defined");
            }
            if (target_settings == null)
            {
                throw new ArgumentNullException($"'{nameof(target_settings)}' must be defined");
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
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool UpgradeSettings(
            Version current_app_version,
            AppInfo current_app_info,
            out string user_config_path,
            SettingsBase target_settings,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null)
        {
            user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos))
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
        /// <returns><c>true</c> if a user configuration file is found, <c>false</c> if no file is found.</returns>
        public static bool UpgradeSettings(
            Version current_app_version,
            AppInfo current_app_info,
            SettingsBase target_settings,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null)
        {
            string user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos))
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
            ConfigurationUserLevel user_level_config = ConfigurationUserLevel.PerUserRoaming,
            bool accept_higher_app_versions = false,
            IReadOnlyCollection<AppInfo> previous_app_infos = null)
        {
            Version current_app_version;
            AppInfo current_app_info = GetAppInfoFromCurrentUserConfig(out current_app_version, user_level_config);

            string user_config_path = null;
            if (!TryFindLatestUserConfig(
                current_app_version,
                current_app_info,
                out user_config_path,
                accept_higher_app_versions,
                previous_app_infos))
            {
                return false;
            }

            IDictionary<string, object> exported_settings = ExportSettings(user_config_path, target_settings);
            ApplySettings(exported_settings, target_settings);
            return true;
        }
    }
}
