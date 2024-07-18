using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Yabber;

namespace UXM
{
    static class ArchiveUnpacker
    {
        private const int WRITE_LIMIT = 1024 * 1024 * 100;

        public static bool Skip { get; private set; }
        static IntPtr _oodlePtr = IntPtr.Zero;

        public static void SetSkip(bool skip)
        {
            Skip = skip;
        }

        public static string Unpack(string exePath, IProgress<(double value, string status)> progress, CancellationToken ct)
        {

            progress.Report((0, "Preparing to unpack..."));
            string gameDir = Path.GetDirectoryName(exePath);

            Util.Game game;
            GameInfo gameInfo;
            try
            {
                game = Util.GetExeVersion(exePath);
                gameInfo = GameInfo.GetGameInfo(game);
            }
            catch (Exception ex)
            {

                return $"Error getting game info: {ex.Message} {ex.StackTrace}";
            }
            _oodlePtr = getOodlePtr(game, gameDir);

            if (FormFileView.SelectedFiles.Any() && Skip)
                gameInfo.Dictionary = new ArchiveDictionary(string.Join("\n", FormFileView.SelectedFiles), Util.GetBHD5Game(game));

            Dictionary<string, string> keys = null;
            if (game == Util.Game.DarkSouls2 || game == Util.Game.Scholar)
            {
                try
                {
                    keys = new Dictionary<string, string>();
                    foreach (string archive in gameInfo.Archives)
                    {
                        string pemPath = $@"{gameDir}\{archive.Replace("Ebl", "KeyCode")}.pem";
                        keys[archive] = File.ReadAllText(pemPath);
                    }
                }
                catch (Exception ex)
                {
                    return $"Failed to load Dark Souls 2 archive keys.\r\n\r\n{ex}";
                }
            }
            else if (game == Util.Game.DarkSouls3)
            {
                keys = ArchiveKeys.DarkSouls3Keys;
            }
            else if (game == Util.Game.Sekiro)
            {
                keys = ArchiveKeys.SekiroKeys;
            }
            else if (game == Util.Game.SekiroBonus)
            {
                keys = ArchiveKeys.SekiroBonusKeys;
            }
            else if (game == Util.Game.EldenRing)
            {
                keys = ArchiveKeys.EldenRingKeys;
            }
            else if (game == Util.Game.ArmoredCore6)
            {
                keys = ArchiveKeys.ArmoredCore6Keys;
            }

            string drive = Path.GetPathRoot(Path.GetFullPath(gameDir));
            DriveInfo driveInfo = new DriveInfo(drive);

            if (driveInfo.AvailableFreeSpace < gameInfo.RequiredGB * 1024 * 1024 * 1024)
            {
                DialogResult choice = MessageBox.Show(
                    $"{gameInfo.RequiredGB} GB of free space is required to fully unpack this game; " +
                    $"only {driveInfo.AvailableFreeSpace / (1024f * 1024 * 1024):F1} GB available.\r\n" +
                    "If you're only doing a partial unpack to restore some files you may ignore this warning, " +
                    "otherwise it will most likely fail.\r\n\r\n" +
                    "Do you want to continue?",
                    "Space Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (choice == DialogResult.No)
                    return null;
            }

            if (ct.IsCancellationRequested)
                return null;

            try
            {
                for (int i = 0; i < gameInfo.BackupDirs.Count; i++)
                {
                    string backup = gameInfo.BackupDirs[i];
                    progress.Report(((1.0 + (double)i / gameInfo.BackupDirs.Count) / (gameInfo.Archives.Count + 2.0),
                        $"Backing up directory \"{backup}\" ({i + 1}/{gameInfo.BackupDirs.Count})..."));

                    string backupSource = $@"{gameDir}\{backup}";
                    string backupTarget = $@"{gameDir}\_backup\{backup}";

                    if (Directory.Exists(backupSource) && !Directory.Exists(backupTarget))
                    {
                        foreach (string file in Directory.GetFiles(backupSource, "*", SearchOption.AllDirectories))
                        {
                            string relative = file.Substring(backupSource.Length + 1);
                            string target = backupTarget + "\\" + relative;
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Copy(file, target);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Failed to back up directories.\r\n\r\n{ex}";
            }

            for (int i = 0; i < gameInfo.Archives.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    return null;

                string archive = gameInfo.Archives[i];

                string error = UnpackArchive(gameDir, archive, keys?[archive], i,
                    gameInfo.Archives.Count, gameInfo.BHD5Game, gameInfo.Dictionary, progress, ct).Result;
                if (error != null)
                    return error;
            }

            if (game == Util.Game.DarkSouls)
                UnpackDarkSoulsPTDE(exePath, gameDir, progress);

            freeOodleLibrary();
            progress.Report((1, "Unpacking complete!"));
            return null;
        }

        private static IntPtr getOodlePtr(Util.Game game, string gameDir)
        {
            string oodlePath;
            switch (game)
            {
                case Util.Game.Sekiro:
                case Util.Game.EldenRing:
                    oodlePath = $"{gameDir}\\oo2core_6_win64.dll";
                    break;
                case Util.Game.ArmoredCore6:
                    oodlePath = $"{gameDir}\\oo2core_8_win64.dll";
                    break;
                default:
                    return IntPtr.Zero;
            }


            if (File.Exists(oodlePath))
            {
                return Kernel32.LoadLibrary(oodlePath);
            }

            oodlePath = Util.GetOodlePath();
            if (oodlePath != null)
            {
                return Kernel32.LoadLibrary(oodlePath);
            }

            return IntPtr.Zero;
        }

        private static void freeOodleLibrary()
        {
            if (_oodlePtr != IntPtr.Zero)
            {
                Kernel32.FreeLibrary(_oodlePtr);
                return;
            }

            IntPtr oodlePtr = Oodle.GetOodlePtr();
            if (oodlePtr != IntPtr.Zero)
            {
                Kernel32.FreeLibrary(oodlePtr);
                return;
            }
        }

        private static void UnpackDarkSoulsPTDE(string exePath, string gameDir, IProgress<(double value, string status)> progress)
        {
            progress.Report((0, "Grabbing missing BHDs"));
            GetBHD(gameDir, progress);

            if (!FormFileView.SelectedFiles.Any() || FormFileView.SelectedFiles.Contains(c4110Path))
            {
                progress.Report((0, "Creating c4110 file"));
                CreateC4110(gameDir);
            }

            progress.Report((0, "Moving map tpf files"));
            MoveTPFs(gameDir);

            progress.Report((0, "Extracting bhd/bdt pairs"));
            ExtractBHD(gameDir, progress);
        }

        private static async Task<string> UnpackArchive(string gameDir, string archive, string key, int index, int total,
            BHD5.Game gameVersion, ArchiveDictionary archiveDictionary,
            IProgress<(double value, string status)> progress, CancellationToken ct)
        {
            progress.Report(((index + 2.0) / (total + 2.0), $"Loading {archive}..."));
            string bhdPath = $@"{gameDir}\{archive}.bhd";
            string bdtPath = $@"{gameDir}\{archive}.bdt";

            if (gameVersion == BHD5.Game.DarkSouls1)
                bhdPath += "5";

            if (File.Exists(bhdPath) && File.Exists(bdtPath))
            {
                BHD5 bhd;
                try
                {
                    bool encrypted = true;
                    using (FileStream fs = File.OpenRead(bhdPath))
                    {
                        byte[] magic = new byte[4];
                        fs.Read(magic, 0, 4);
                        encrypted = Encoding.ASCII.GetString(magic) != "BHD5";
                    }

                    if (encrypted)
                    {
                        using (MemoryStream bhdStream = CryptographyUtility.DecryptRsa(bhdPath, key))
                        {
                            bhd = BHD5.Read(bhdStream, gameVersion);
                        }
                    }
                    else
                    {
                        using (FileStream bhdStream = File.OpenRead(bhdPath))
                        {
                            bhd = BHD5.Read(bhdStream, gameVersion);
                        }
                    }
                }
                catch (OverflowException ex)
                {
                    return $"Failed to open BHD:\n{bhdPath}\n\n{ex}";
                }

                int fileCount = bhd.Buckets.Sum(b => b.Count);

                try
                {
                    var asyncFileWriters = new List<Task<long>>();
                    using (FileStream bdtStream = File.OpenRead(bdtPath))
                    {
                        int currentFile = -1;
                        long writingSize = 0;

                        foreach (BHD5.Bucket bucket in bhd.Buckets)
                        {
                            if (ct.IsCancellationRequested)
                                break;

                            foreach (BHD5.FileHeader header in bucket)
                            {
                                if (ct.IsCancellationRequested)
                                    break;

                                currentFile++;
                                string path;
                                bool unknown;
                                if (archiveDictionary.GetPath(header.FileNameHash, out path))
                                {
                                    if (archive == @"sd\sd" || archive == @"sd\sd_dlc02")
                                        path = $@"\sd\{path}";

                                    unknown = false;
                                    path = gameDir + path.Replace('/', '\\');
                                    if (File.Exists(path))
                                        continue;
                                }
                                else
                                {
                                    if (Skip)
                                        continue;

                                    unknown = true;
                                    string filename = $"{archive.Split('\\')[0]}_{header.FileNameHash:D10}"; //sad :(
                                    string directory = $@"{gameDir}\_unknown";
                                    path = $@"{directory}\{filename}";
                                    if (File.Exists(path) || Directory.Exists(directory) && Directory.GetFiles(directory, $"{filename}.*").Length > 0)
                                        continue;
                                }

                                progress.Report(((index + 2.0 + currentFile / (double)fileCount) / (total + 2.0),
                                    $"Unpacking {archive} ({currentFile + 1}/{fileCount})..."));

                                long fileSize = GetFileSize(header, gameVersion);

                                while (asyncFileWriters.Count > 0 && writingSize + fileSize > WRITE_LIMIT)
                                {
                                    for (int i = 0; i < asyncFileWriters.Count; i++)
                                    {
                                        if (asyncFileWriters[i].IsCompleted)
                                        {
                                            writingSize -= await asyncFileWriters[i];
                                            asyncFileWriters.RemoveAt(i);
                                        }
                                    }

                                    if (asyncFileWriters.Count > 0 && writingSize + fileSize > WRITE_LIMIT)
                                        Thread.Sleep(10);
                                }

                                byte[] bytes;
                                try
                                {
                                    bytes = header.ReadFile(bdtStream);
                                    if (
                                        (archive == @"sd\sd" || archive == @"sd\sd_dlc02") &&
                                        gameVersion >= BHD5.Game.EldenRing && bytes.Length > fileSize
                                    )
                                    {
                                        bytes = bytes.Take((int)fileSize).ToArray();
                                    }

                                    if (unknown)
                                    {
                                        path += Util.GetExtensions(bytes);
                                    }

                                    if (gameVersion == BHD5.Game.DarkSouls1 && path.Contains(".dcx"))
                                    {
                                        bytes = DCX.Decompress(bytes, out DCX.Type compression);
                                        path = path.Replace(".dcx", "");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    return $"Failed to read file:\r\n{path}\r\n\r\n{ex}";
                                }

                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                                    writingSize += bytes.Length;
                                    asyncFileWriters.Add(WriteFileAsync(path, bytes));
                                }
                                catch (Exception ex)
                                {
                                    return $"Failed to write file:\r\n{path}\r\n\r\n{ex}";
                                }
                            }
                        }
                    }

                    foreach (Task<long> task in asyncFileWriters)
                        await task;
                }
                catch (Exception ex)
                {
                    return $"Failed to unpack BDT:\r\n{bdtPath}\r\n\r\n{ex}";
                }
            }

            return null;
        }

        private static long GetFileSize(BHD5.FileHeader header, BHD5.Game gameVersion)
        {
            if (gameVersion >= BHD5.Game.DarkSouls3)
            {
                return header.UnpaddedFileSize;
            }

            return header.PaddedFileSize;
        }

        private static async Task<long> WriteFileAsync(string path, byte[] bytes)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await fs.WriteAsync(bytes, 0, bytes.Length);
            }
            return bytes.Length;
        }

        private static void GetBHD(string gameDir, IProgress<(double, string)> progress)
        {
            var bdt = Directory.GetFiles(gameDir, "*.chrtpfbdt", SearchOption.AllDirectories);

            var position = 0;

            foreach (var path in bdt)
            {
                position++;
                var target = $@"{Path.GetDirectoryName(path)}\{Path.GetFileNameWithoutExtension(path)}.chrbnd";
                var percent = (double)position / bdt.Length;
                progress.Report((percent, $"Unpacking BND3 ({position}/{bdt.Length}): {target.Replace(gameDir, "")}..."));
                UnpackBND(target);
            }
        }
        public static void UnpackBND(string sourceFile)
        {
            string sourceDir = Path.GetDirectoryName(sourceFile);
            string filename = Path.GetFileName(sourceFile);
            string targetDir = $"{sourceDir}";

            using (var bnd = new BND3Reader(sourceFile))
            {
                bnd.Unpack(filename, targetDir, new Progress<float>(), true, true, true);
            }
        }

        static string c4110Path = "/chr/c4110.chrtpfbhd";
        private static void CreateC4110(string gameDir)
        {
            string path = $@"{gameDir}/{c4110Path}";

            File.WriteAllBytes(path, GameData.c4110);
        }
        private static void MoveTPFs(string gameDir)
        {
            var tpfbdt = Directory.GetFiles(gameDir, "*.tpfbdt", SearchOption.AllDirectories);
            var tpfbhd = Directory.GetFiles(gameDir, "*.tpfbhd", SearchOption.AllDirectories);
            if (tpfbdt.Length <= 0 && tpfbhd.Length <= 0)
                return;

            if (tpfbdt.Length != tpfbhd.Length)
                throw new Exception($"Missing a bdt:{tpfbdt.Length} or bhd:{tpfbhd.Length}. " +
                    $"These need to be paired evenly");

            Directory.CreateDirectory($@"{gameDir}\map\tx");
            foreach (var file in tpfbdt)
            {
                File.Move(file, $@"{gameDir}\map\tx\{Path.GetFileName(file)}");
            }

            foreach (var file in tpfbhd)
            {
                File.Move(file, $@"{gameDir}\map\tx\{Path.GetFileName(file)}");
            }
        }
        private static void ExtractBHD(string gameDir, IProgress<(double, string)> progress)
        {
            var bhd = Directory.GetFiles(gameDir, "*bhd", SearchOption.AllDirectories).Where(x => !x.Contains("bhd5")).ToArray();

            var position = 0;

            foreach (var filePath in bhd)
            {
                position++;
                var percent = (double)position / bhd.Length;
                progress.Report((percent, $"Unpacking BXF3 ({position}/{bhd.Length}): {filePath.Replace(gameDir, "")}..."));
                UnpackBHD(filePath);
                var bdt = filePath.Replace("bhd", "bdt");
                File.Delete(filePath);
                File.Delete(bdt);
            }
        }

        public static void UnpackBHD(string sourceFile)
        {
            string sourceDir = Path.GetDirectoryName(sourceFile);
            string filename = Path.GetFileName(sourceFile);
            string targetDir = $"{sourceDir}";
            if (filename.Contains(".chrtpfbhd"))
                targetDir += $@"\{Path.GetFileNameWithoutExtension(filename)}";

            string bdtExtension = Path.GetExtension(filename).Replace("bhd", "bdt");
            string bdtFilename = $"{Path.GetFileNameWithoutExtension(filename)}{bdtExtension}";
            string bdtPath = $"{sourceDir}\\{bdtFilename}";
            if (File.Exists(bdtPath))
            {
                using (var bxf = new BXF3Reader(sourceFile, bdtPath))
                {
                    bxf.Unpack(filename, bdtFilename, targetDir, new Progress<float>(), true, true, true);
                }
            }
            else
            {
                //progress.Report($"BDT not found for BHD: {filename}");
            }
        }

    }
}
