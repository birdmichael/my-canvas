using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace JonsboCanvas
{
    internal static class EmbeddedRuntime
    {
        private const string ResourcePrefix = "JonsboCanvas.Embedded.";
        private const string RuntimeVersion = "dual-runtime-v1";

        private sealed class EmbeddedFile
        {
            public string Resource;
            public string RelativePath;

            public EmbeddedFile(string resource, string relativePath)
            {
                Resource = resource;
                RelativePath = relativePath;
            }
        }

        private static readonly EmbeddedFile[] Files =
        {
            new EmbeddedFile("MSDISPLAYSDKWRRAPER.dll", "MSDISPLAYSDKWRRAPER.dll"),
            new EmbeddedFile("libusb0.dll", "libusb0.dll"),
            new EmbeddedFile("cpuidsdk.dll", "cpuidsdk.dll"),
            new EmbeddedFile("themes.cyber-cyan.png", @"themes\cyber-cyan.png"),
            new EmbeddedFile("themes.molten-amber.png", @"themes\molten-amber.png"),
            new EmbeddedFile("themes.aurora-violet.png", @"themes\aurora-violet.png"),
            new EmbeddedFile("themes.animated-cyan.gif", @"themes\animated-cyan.gif"),
            new EmbeddedFile("assets.codex-pet-idle.png", @"assets\codex-pet-idle.png"),
            new EmbeddedFile("assets.claude-companion.png", @"assets\claude-companion.png"),
            new EmbeddedFile("driver.msusbdisplay.inf", @"drivers\MSUSBDisplay\msusbdisplay.inf"),
            new EmbeddedFile("driver.MSUSBDisplay.cat", @"drivers\MSUSBDisplay\MSUSBDisplay.cat"),
            new EmbeddedFile("driver.libusb0.sys", @"drivers\MSUSBDisplay\amd64\libusb0.sys"),
            new EmbeddedFile("libusb0.dll", @"drivers\MSUSBDisplay\amd64\libusb0.dll"),
            new EmbeddedFile("driver.libusb0_x86.dll", @"drivers\MSUSBDisplay\x86\libusb0_x86.dll")
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string pathName);

        public static string DataDirectory { get; private set; }
        public static string RuntimeDirectory { get; private set; }
        public static string ConfigPath { get { return Path.Combine(DataDirectory, "config.json"); } }
        public static string LogPath { get { return Path.Combine(DataDirectory, "JonsboCanvas.log"); } }
        public static string PreviewPath
        {
            get
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrWhiteSpace(pictures))
                    pictures = DataDirectory;
                return Path.Combine(pictures, "MyCanvas-preview.png");
            }
        }

        public static void Initialize()
        {
            string dataOverride = Environment.GetEnvironmentVariable("JONSBO_CANVAS_DATA_DIR");
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
                    dataOverride = argument.Substring("--data-dir=".Length);
            DataDirectory = string.IsNullOrWhiteSpace(dataOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCanvas")
                : Path.GetFullPath(dataOverride);
            RuntimeDirectory = Path.Combine(DataDirectory, RuntimeVersion);
            Directory.CreateDirectory(RuntimeDirectory);

            foreach (EmbeddedFile file in Files)
                Extract(file);

            SetDllDirectory(RuntimeDirectory);
            MigrateLegacyConfig();
        }

        public static string ResolveDataPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
                return path;
            return Path.Combine(RuntimeDirectory, path);
        }

        private static void Extract(EmbeddedFile file)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = ResourcePrefix + file.Resource;
            string targetPath = Path.Combine(RuntimeDirectory, file.RelativePath);
            string directory = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (Stream source = assembly.GetManifestResourceStream(resourceName))
            {
                if (source == null)
                    throw new InvalidOperationException("EXE 内缺少资源：" + resourceName);
                if (File.Exists(targetPath) && StreamsMatch(source, targetPath))
                    return;
                source.Position = 0;
                using (FileStream target = new FileStream(targetPath, FileMode.Create,
                    FileAccess.Write, FileShare.None))
                    source.CopyTo(target);
            }
        }

        private static bool StreamsMatch(Stream embedded, string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (embedded.Length != file.Length)
                    return false;
                using (SHA256 hash = SHA256.Create())
                {
                    byte[] embeddedHash = hash.ComputeHash(embedded);
                    embedded.Position = 0;
                    using (FileStream existing = File.OpenRead(path))
                    {
                        byte[] fileHash = hash.ComputeHash(existing);
                        if (embeddedHash.Length != fileHash.Length)
                            return false;
                        for (int i = 0; i < embeddedHash.Length; i++)
                            if (embeddedHash[i] != fileHash[i])
                                return false;
                        return true;
                    }
                }
            }
            catch
            {
                embedded.Position = 0;
                return false;
            }
        }

        private static void MigrateLegacyConfig()
        {
            if (File.Exists(ConfigPath))
                return;
            try
            {
                string executableDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string legacy = Path.Combine(executableDirectory, "config.json");
                if (File.Exists(legacy))
                    File.Copy(legacy, ConfigPath, false);
            }
            catch
            {
            }
        }
    }
}
