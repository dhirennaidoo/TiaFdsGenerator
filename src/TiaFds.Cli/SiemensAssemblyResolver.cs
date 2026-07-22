using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TiaFds.Cli
{
    internal static class SiemensAssemblyResolver
    {
        private const string RequiredAssemblyName = "Siemens.Engineering";
        private const string AssemblyVersion = "15.1.0.0";
        private const string PublicKeyToken = "d29ec89bac048f84";
        private const string OpennessRegistryPath = @"SOFTWARE\Siemens\Automation\Openness";

        private static readonly object SyncRoot = new object();
        private static Assembly loadedAssembly;

        public static void RegisterAndLoad()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            lock (SyncRoot)
            {
                if (loadedAssembly == null)
                {
                    loadedAssembly = LoadRegisteredAssembly();
                }
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var requestedAssembly = new AssemblyName(args.Name);
            if (!IsRequiredAssembly(requestedAssembly))
            {
                return null;
            }

            lock (SyncRoot)
            {
                if (loadedAssembly == null)
                {
                    loadedAssembly = LoadRegisteredAssembly();
                }

                return loadedAssembly;
            }
        }

        private static Assembly LoadRegisteredAssembly()
        {
            string assemblyPath = FindRegisteredAssemblyPath();
            if (assemblyPath == null)
            {
                throw new FileNotFoundException(
                    "No valid TIA Portal Openness 15.1 registration was found in the 64-bit " +
                    "Windows registry. Install TIA Portal V15.1 Update 4 with Openness, then " +
                    @"verify HKLM\SOFTWARE\Siemens\Automation\Openness and its registered " +
                    "Siemens.Engineering file path.");
            }

            var registeredAssembly = System.Reflection.AssemblyName.GetAssemblyName(assemblyPath);
            if (!IsRequiredAssembly(registeredAssembly))
            {
                throw new FileLoadException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The registered Siemens assembly at '{0}' does not match " +
                        "Siemens.Engineering, Version={1}, PublicKeyToken={2}.",
                        assemblyPath,
                        AssemblyVersion,
                        PublicKeyToken),
                    assemblyPath);
            }

            return Assembly.LoadFrom(assemblyPath);
        }

        private static string FindRegisteredAssemblyPath()
        {
            using (RegistryKey localMachine = RegistryKey.OpenBaseKey(
                       RegistryHive.LocalMachine,
                       RegistryView.Registry64))
            using (RegistryKey openness = localMachine.OpenSubKey(OpennessRegistryPath))
            {
                if (openness == null)
                {
                    return null;
                }

                foreach (string portalVersionName in openness.GetSubKeyNames())
                {
                    using (RegistryKey publicApi = openness.OpenSubKey(
                               portalVersionName + @"\PublicAPI"))
                    {
                        string path = FindVersionPath(publicApi);
                        if (path != null)
                        {
                            return path;
                        }
                    }
                }
            }

            return null;
        }

        private static string FindVersionPath(RegistryKey publicApi)
        {
            if (publicApi == null)
            {
                return null;
            }

            foreach (string apiVersionName in publicApi.GetSubKeyNames())
            {
                using (RegistryKey apiVersion = publicApi.OpenSubKey(apiVersionName))
                {
                    if (apiVersion == null || !IsRequiredVersion(apiVersionName, apiVersion))
                    {
                        continue;
                    }

                    string path = apiVersion.GetValue(RequiredAssemblyName) as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return Path.GetFullPath(path);
                    }
                }
            }

            return null;
        }

        private static bool IsRequiredVersion(string apiVersionName, RegistryKey apiVersion)
        {
            string registeredVersion = apiVersion.GetValue("AssemblyVersion") as string;
            if (string.IsNullOrWhiteSpace(registeredVersion))
            {
                registeredVersion = apiVersionName;
            }

            return string.Equals(
                registeredVersion.Trim().TrimStart('V', 'v'),
                AssemblyVersion,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRequiredAssembly(System.Reflection.AssemblyName assemblyName)
        {
            return string.Equals(assemblyName.Name, RequiredAssemblyName, StringComparison.OrdinalIgnoreCase) &&
                   assemblyName.Version != null &&
                   string.Equals(assemblyName.Version.ToString(), AssemblyVersion, StringComparison.Ordinal) &&
                   string.Equals(ToHex(assemblyName.GetPublicKeyToken()), PublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
