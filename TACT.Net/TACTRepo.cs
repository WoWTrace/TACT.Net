using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using TACT.Net.Common;
using TACT.Net.Configs;
using TACT.Net.Cryptography;
using TACT.Net.Network;

[assembly: InternalsVisibleTo("TACT.Net.Tests")]
namespace TACT.Net
{
    public sealed class TACTRepo
    {
        /// <summary>
        /// The root archive folder containing all files typically "tpr/wow/"
        /// </summary>
        public string BaseDirectory { get; private set; }

        /// <summary>
        /// The client build number of the repo
        /// </summary>
        public uint Build { get; private set; }

        /// <summary>
        /// The client full version string of the repo
        /// </summary>
        public string VersionsName { get; private set; }

        /// <summary>
        /// Remove unused TACT files after save storage
        /// </summary>
        public bool ActiveCleanup = false;

        /// <summary>
        /// Allow to cache system files downloaded from remote repo
        /// </summary>
        public bool RemoteCache = false;

        /// <summary>
        /// Must be set to cache remote system files
        /// </summary>
        public string RemoteCacheDirectory = null;

        /// <summary>
        /// Cleanup queue
        /// </summary>
        public Queue<string> CleanupQueue = new Queue<string>();

        #region System Files

        public Configs.ManifestContainer ManifestContainer { get; set; }
        public Configs.ConfigContainer ConfigContainer { get; set; }
        public Indices.IndexContainer IndexContainer { get; set; }
        public Encoding.EncodingFile EncodingFile { get; set; }
        public Root.RootFile RootFile { get; set; }
        public Download.DownloadFile DownloadFile { get; set; }
        public Download.DownloadSizeFile DownloadSizeFile { get; set; }
        public Install.InstallFile InstallFile { get; set; }
        public Patch.PatchFile PatchFile { get; set; }

        #endregion

        #region Constructors

        public TACTRepo(string baseDirectory = "")
        {
            BaseDirectory = baseDirectory;
        }

        /// <summary>
        /// Loads the CDNs/Versions manifests and optional stores the base directory
        /// </summary>
        /// <param name="product"></param>
        /// <param name="locale"></param>
        /// <param name="manifestDirectory"></param>
        /// <param name="baseDirectory"></param>
        public TACTRepo(string product, Locale locale, string manifestDirectory, string baseDirectory = "")
        {
            ManifestContainer = new Configs.ManifestContainer(product, locale);
            ManifestContainer.OpenLocal(manifestDirectory);
            BaseDirectory = baseDirectory;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new TACT container populated with: defaulted configs, an index container 
        /// and an empty root, encoding, install and download file
        /// </summary>
        /// <param name="product"></param>
        /// <param name="locale"></param>
        /// <param name="build"></param>
        /// <param name="versionName"></param>
        public void Create(string product, Locale locale, uint build, string versionName = null)
        {
            Build = build;

            if (!string.IsNullOrEmpty(versionName))
                VersionsName = versionName;

            ManifestContainer = new Configs.ManifestContainer(product, locale);
            ManifestContainer.Create();

            ConfigContainer = new Configs.ConfigContainer();
            ConfigContainer.Create();

            IndexContainer = new Indices.IndexContainer();
            RootFile = new Root.RootFile();
            EncodingFile = new Encoding.EncodingFile();
            InstallFile = new Install.InstallFile();
            DownloadFile = new Download.DownloadFile();

            ApplyVersionSpecificSettings(build);

            // set the default tag entries
            InstallFile.SetDefaultTags(build);
            DownloadFile.SetDefaultTags(build);
            DownloadSizeFile?.SetDefaultTags(build);
        }

        /// <summary>
        /// Opens an existing TACT container and loads the Root and Encoding files
        /// <para></para>
        /// NOTE: a ManifestContainer MUST be loaded first
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="product"></param>
        /// <param name="locale"></param>
        public void Open(string directory)
        {
            BaseDirectory = directory;         

            ConfigContainer = new Configs.ConfigContainer();
            ConfigContainer.OpenLocal(directory, ManifestContainer);

            if (uint.TryParse(ManifestContainer?.VersionsFile?.GetValue("BuildId", ManifestContainer.Locale), out uint build))
                Build = build;

            string versionsName = ManifestContainer?.VersionsFile?.GetValue("VersionsName", ManifestContainer.Locale);
            if (!string.IsNullOrEmpty(versionsName))
                VersionsName = versionsName;

            IndexContainer = new Indices.IndexContainer();
            IndexContainer.Open(directory, ConfigContainer, true);

            if (!ConfigContainer.EncodingEKey.IsEmpty)
            {
                EncodingFile = new Encoding.EncodingFile(BaseDirectory, ConfigContainer.EncodingEKey);

                // Open RootFile
                if (ConfigContainer.RootCKey.Value != null && EncodingFile.TryGetCKeyEntry(ConfigContainer.RootCKey, out var rootEKey))
                    RootFile = new Root.RootFile(BaseDirectory, rootEKey.EKeys[0]);

                // Open InstallFile
                if (ConfigContainer.InstallCKey.Value != null && EncodingFile.TryGetCKeyEntry(ConfigContainer.InstallCKey, out var installEKey))
                    InstallFile = new Install.InstallFile(BaseDirectory, installEKey.EKeys[0]);

                // Open DownloadFile
                if (ConfigContainer.DownloadCKey.Value != null && EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadCKey, out var downloadEKey))
                    DownloadFile = new Download.DownloadFile(BaseDirectory, downloadEKey.EKeys[0]);

                // Open DownloadSizeFile
                if (ConfigContainer.DownloadSizeCKey.Value != null && EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadSizeCKey, out var downloadSizeEKey))
                    DownloadSizeFile = new Download.DownloadSizeFile(BaseDirectory, downloadSizeEKey.EKeys[0]);
            }

            // Open PatchFile
            if (ConfigContainer.PatchEKey.Value != null)
                PatchFile = new Patch.PatchFile(BaseDirectory, ConfigContainer.PatchEKey);
        }

        /// <summary>
        /// Streams an existing TACT container from an external CDN
        /// </summary>
        /// <param name="product"></param>
        /// <param name="locale"></param>
        public void OpenRemote(string product, Locale locale)
        {
            if (ManifestContainer == null)
                ManifestContainer = new Configs.ManifestContainer(product, locale);

            bool manifestLoadedFromLocal = false;
            if (RemoteCache && !string.IsNullOrEmpty(RemoteCacheDirectory))
            {
                if (!string.IsNullOrEmpty(ManifestContainer.OverrideBuildConfig))
                {
                    string manifestCache = Path.Combine(RemoteCacheDirectory, "manifest", ManifestContainer.OverrideBuildConfig, product);

                    if (File.Exists(Path.Combine(manifestCache, ConfigType.CDNs.ToString().ToLowerInvariant())) && File.Exists(Path.Combine(manifestCache, ConfigType.Versions.ToString().ToLowerInvariant())))
                    {
                        ManifestContainer.OpenLocal(manifestCache);
                        manifestLoadedFromLocal = true;
                    }
                }
            }

            string remoteCacheDirectory = RemoteCache ? RemoteCacheDirectory : null;

            if (!manifestLoadedFromLocal)
                ManifestContainer.OpenRemote(remoteCacheDirectory);

            ConfigContainer = new Configs.ConfigContainer();
            ConfigContainer.OpenRemote(ManifestContainer, remoteCacheDirectory);

            if (uint.TryParse(ManifestContainer?.VersionsFile?.GetValue("BuildId", locale), out uint build))
                Build = build;

            string versionsName = ManifestContainer?.VersionsFile?.GetValue("VersionsName", ManifestContainer.Locale);
            if (!string.IsNullOrEmpty(versionsName))
                VersionsName = versionsName;

            // stream Indicies
            IndexContainer = new Indices.IndexContainer();
            IndexContainer.OpenRemote(ConfigContainer, ManifestContainer, true, remoteCacheDirectory);

            var cdnClient = new CDNClient(ManifestContainer, false, remoteCacheDirectory);

            if (ConfigContainer.EncodingEKey.Value != null)
            {
                // Stream EncodingFile
                EncodingFile = new Encoding.EncodingFile(cdnClient, ConfigContainer.EncodingEKey);

                // Stream RootFile
                if (EncodingFile.TryGetCKeyEntry(ConfigContainer.RootCKey, out var entry))
                    RootFile = new Root.RootFile(cdnClient, entry.EKeys[0]);

                // Stream InstallFile
                if (ConfigContainer.InstallEKey.Value != null)
                    InstallFile = new Install.InstallFile(cdnClient, ConfigContainer.InstallEKey);
                else if (EncodingFile.TryGetCKeyEntry(ConfigContainer.InstallCKey, out entry))
                    InstallFile = new Install.InstallFile(cdnClient, entry.EKeys[0]);

                // Stream DownloadFile
                if (ConfigContainer.DownloadEKey.Value != null)
                    DownloadFile = new Download.DownloadFile(cdnClient, ConfigContainer.DownloadEKey);
                else if (EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadCKey, out entry))
                    DownloadFile = new Download.DownloadFile(cdnClient, entry.EKeys[0]);

                // Stream DownloadSizeFile
                if (ConfigContainer.DownloadSizeEKey.Value != null)
                    DownloadSizeFile = new Download.DownloadSizeFile(cdnClient, ConfigContainer.DownloadSizeEKey);
                else if (ConfigContainer.DownloadSizeCKey.Value != null && EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadSizeCKey, out entry))
                    DownloadSizeFile = new Download.DownloadSizeFile(cdnClient, entry.EKeys[0]);
            }

            // Stream PatchFile
            if (ConfigContainer.PatchEKey.Value != null)
                PatchFile = new Patch.PatchFile(cdnClient, ConfigContainer.PatchEKey);
        }

        /// <summary>
        /// Download and open an remote TACT container
        /// <para>Note: This will download the entire CDN so will take a while</para>
        /// </summary>
        /// <param name="url"></param>
        /// <param name="tprDirectory"></param>
        /// <param name="product"></param>
        /// <param name="locale"></param>
        public void DownloadRemote(string tprDirectory, string product, Locale locale, bool systemFileOnly = false)
        {
            if (ManifestContainer == null)
                ManifestContainer = new Configs.ManifestContainer(product, locale);

            ManifestContainer.DownloadRemote(BaseDirectory);

            if (ConfigContainer == null)
                ConfigContainer = new Configs.ConfigContainer();

            ConfigContainer.DownloadRemote(tprDirectory, ManifestContainer);

            var cdnClient = new CDNClient(ManifestContainer);
            var queuedDownload = new QueuedDownloader(tprDirectory, cdnClient);

            if (ConfigContainer.EncodingEKey.Value != null)
            {
                // Download encoding file
                var encodingEKey = DownloadSystemFile(ConfigContainer.EncodingEKey, cdnClient, tprDirectory);
                if (encodingEKey.Value != null)
                    EncodingFile = new Encoding.EncodingFile(tprDirectory, encodingEKey, true);

                // Download PatchFile
                DownloadSystemFile(ConfigContainer.PatchEKey, cdnClient, tprDirectory, "patch");

                // Download RootFile
                if (EncodingFile.TryGetCKeyEntry(ConfigContainer.RootCKey, out var ekeyEntry))
                    ekeyEntry.EKeys.ForEach(x => queuedDownload.Enqueue(x.ToString()));                    

                // Download InstallFile
                if (EncodingFile.TryGetCKeyEntry(ConfigContainer.InstallCKey, out ekeyEntry))
                    ekeyEntry.EKeys.ForEach(x => queuedDownload.Enqueue(x.ToString()));

                // Download DownloadFile
                if (EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadCKey, out ekeyEntry))
                    ekeyEntry.EKeys.ForEach(x => queuedDownload.Enqueue(x.ToString()));

                // Download DownloadSizeFile
                if (EncodingFile.TryGetCKeyEntry(ConfigContainer.DownloadSizeCKey, out ekeyEntry))
                    ekeyEntry.EKeys.ForEach(x => queuedDownload.Enqueue(x.ToString()));

                queuedDownload.Download("data");
            }

            // Download Indices and archives
            if (!systemFileOnly)
            {
                IndexContainer = new Indices.IndexContainer();
                IndexContainer.DownloadRemote(tprDirectory, ConfigContainer, ManifestContainer);
            }

            Open(tprDirectory);
        }

        /// <summary>
        /// Saves all open System Files to disk.
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="manifestDirectory">Optional stores the Versions/CDNs in a different location</param>
        public void Save(string directory, string manifestDirectory = "")
        {
            // if this field exists and mismatches the generated file; the client will error
            // if this field is missing the client will generate the file and variable itself
            ConfigContainer?.CDNConfig?.GetValues("archive-group")?.Clear();

            IndexContainer?.Save(directory, ConfigContainer);
            RootFile?.Write(directory, this);
            DownloadFile?.Write(directory, this);
            DownloadSizeFile?.Write(directory, this);
            InstallFile?.Write(directory, this);
            EncodingFile?.Write(directory, this);
            ConfigContainer?.Save(directory, ManifestContainer, this);

            // save the manifests
            if (string.IsNullOrWhiteSpace(manifestDirectory))
                manifestDirectory = directory;

            ManifestContainer?.Save(manifestDirectory);

            RootFile?.FileLookup?.Close();

            Cleanup(directory);
        }

        public void Close()
        {
            IndexContainer?.Close();
            IndexContainer = null;

            PatchFile?.Close();
            PatchFile = null;

            RootFile?.Close();
            RootFile = null;

            DownloadFile?.Close();
            DownloadFile = null;

            DownloadSizeFile?.Close();
            DownloadSizeFile = null;

            InstallFile?.Close();
            InstallFile = null;

            EncodingFile?.Close();
            EncodingFile = null;

            ConfigContainer = null;
            ManifestContainer = null;
        }

        private void Cleanup(string directory)
        {
            if (!ActiveCleanup)
                return;

            while (CleanupQueue.Any())
            {
                string dataPath = CleanupQueue.Dequeue();
                if (File.Exists(dataPath))
                    File.Delete(dataPath);
            }

            CleanupEmptyFolder(directory);
        }

        private void CleanupEmptyFolder(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                CleanupEmptyFolder(directory);
                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                    Directory.Delete(directory, false);
            }
        }
        #endregion

        #region Helpers

        private void ApplyVersionSpecificSettings(uint build)
        {
            if (build > 24473 && DownloadFile != null)
                DownloadFile.DownloadHeader.Version = 2;

            if (build > 27547)
            {
                if (DownloadFile != null)
                    DownloadFile.DownloadHeader.Version = 3;

                DownloadSizeFile = new Download.DownloadSizeFile();
            }

            if (build >= 30080 && RootFile != null)
                RootFile.RootHeader.Version = 2;
        }

        private static MD5Hash DownloadSystemFile(MD5Hash key, CDNClient client, string directory, string dataFolder = "data")
        {
            if (key.Value == null)
                return default;

            string systemFileUrl = Helpers.GetCDNUrl(key.ToString(), dataFolder);
            client.DownloadFile(systemFileUrl, Helpers.GetCDNPath(key.ToString(), dataFolder, directory, true)).Wait();
            return key;
        }

        #endregion
    }
}
