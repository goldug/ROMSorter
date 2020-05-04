﻿using SharpCompress.Writers.Zip;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RomDatabase5
{
    public class Sorter
    {
        //options
        public bool moveUnidentified = false;
        public bool ZipInsteadOfMove = false;
        public bool UseMultithreading = true;
        public bool PreserveOriginals = true;
        public bool DisplayAllInfo = false;
        public bool IdentifyOnly = false;

        //internal stuff.
        string tempFolderPath = Path.GetTempPath();
        ConcurrentBag<string> files = new ConcurrentBag<string>();

        int filesMovedOrExtracted = 0;
        int filesToReportBetween = 1000; //set to 1% of the workload or 1000 files during sorting to keep the user aware that its doing work.

        public Sorter()
        {
        }

        void EnumerateAllFiles(string topFolder)
        {
            foreach (var file in Directory.EnumerateFiles(topFolder).ToList())
                files.Add(file);
            foreach (var folder in Directory.EnumerateDirectories(topFolder).ToList())
                EnumerateAllFiles(folder);
        }

        LookupEntry GetFileHashes(string file, Hasher hasher)
        {

            var hashes = hasher.HashFile(File.ReadAllBytes(file));
            FileInfo fi = new FileInfo(file);
            LookupEntry le = new LookupEntry();
            le.originalFileName = file;
            le.fileType = LookupEntryType.File;
            le.md5 = hashes[0];
            le.sha1 = hashes[1];
            le.crc = hashes[2];
            le.size = fi.Length;

            return le;
        }

        public void getFilesToScan(string sourceFolder, IProgress<string> progress = null)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            //Step 1: enumerate all files first.
            files = new ConcurrentBag<string>();
            progress.Report("Scanning for files");
            EnumerateAllFiles(sourceFolder);
            progress.Report(files.Count() + " files found in " + sw.Elapsed.ToString());
            sw.Stop();
        }

        public void Sort(string topFolder, string destinationFolder, IProgress<string> progress = null)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            filesMovedOrExtracted = 0;

            filesToReportBetween = files.Count() / 100;
            if (filesToReportBetween > 1000)
                filesToReportBetween = 1000;
            if (filesToReportBetween < 1 || DisplayAllInfo)
                filesToReportBetween = 1;

            //Step 2: hash files, looking into zip files
            progress.Report("Hashing files");
            ConcurrentBag<LookupEntry> filesToFind = new ConcurrentBag<LookupEntry>();
            int hashedFileCount = 0;
            Parallel.ForEach(files, (file) =>
            {
                //Making a new Hasher() per thread is faster than sharing 1 across all thread.
                Hasher hasher = new Hasher();
                switch (Path.GetExtension(file))
                {
                    case ".zip":
                        var zipResults = hasher.HashFromZip(file);
                        if (zipResults != null)
                            foreach (var zr in zipResults)
                                filesToFind.Add(zr);
                        break;
                    case ".rar":
                        var rarResults = hasher.HashFromRar(file);
                        foreach (var rr in rarResults)
                            filesToFind.Add(rr);
                        break;
                    case ".gz":
                    case ".gzip":
                        var gzResults = hasher.HashFromGzip(file);
                        foreach (var gz in gzResults)
                            filesToFind.Add(gz);
                        break;
                    case ".tar":
                        var tarResults = hasher.HashFromTar(file);
                        foreach (var tr in tarResults)
                            filesToFind.Add(tr);
                        break;
                    case ".7z":
                        var sevenZResults = hasher.HashFrom7z(file);
                        foreach (var sz in sevenZResults)
                            filesToFind.Add(sz);
                        break;
                    default:
                        filesToFind.Add(GetFileHashes(file, hasher));
                        break;
                }

                hashedFileCount++;
                if (hashedFileCount % filesToReportBetween == 0)
                    progress.Report("Hashed " + file + " (" + hashedFileCount + " done so far)");
            });
            progress.Report(filesToFind.Count() + " files hashed in " + sw.Elapsed.ToString());

            //Step 3
            //identify files we found, including zip entries.

            progress.Report("Identifying files");
            sw.Restart();
            int foundCount = 0;
            Parallel.ForEach(filesToFind, (possibleGame) =>
            {
                var db = new DatabaseEntities(); //Using the EF here is twice as fast in my testing versus the original code, before adding console names Adding console names makes it ~ 50% slower (15s vs 10s).
                var gameEntry = db.FindGame(possibleGame.size, possibleGame.crc, possibleGame.md5, possibleGame.sha1);
                if (gameEntry != null)
                {
                    foundCount++;
                    possibleGame.destinationFileName = destinationFolder + "\\" + db.consoleIDs[gameEntry.Console.Value].First() + "\\" + gameEntry.Description;
                    possibleGame.console = db.consoleIDs[gameEntry.Console.Value].First();
                    possibleGame.isIdentified = true;
                }
                else
                {
                    var discEntries = db.FindDisc(possibleGame.size, possibleGame.crc, possibleGame.md5, possibleGame.sha1);
                    if (discEntries.Count > 0)
                    {
                        foreach (var de in discEntries)
                        {
                            foundCount++;
                            if (ZipInsteadOfMove)
                                possibleGame.destinationFileName = destinationFolder + "\\" + db.consoleIDs[de.Console.Value].First() + "\\" + de.Name + ".zip";
                            else
                                possibleGame.destinationFileName = destinationFolder + "\\" + db.consoleIDs[de.Console.Value].First() + "\\" + de.Name + "\\" + de.Description;
                            possibleGame.console = db.consoleIDs[de.Console.Value].First() + "\\" + de.Name; //Discs treat games as folders (or zip files)
                            possibleGame.isIdentified = true;
                            possibleGame.isDiscEntry = true;
                            possibleGame.discEntryName = de.Description;
                            //possibleGame.discGameName = de.Name;
                        }
                    }
                }
                if (foundCount % filesToReportBetween == 0)
                {
                    if (!String.IsNullOrEmpty(possibleGame.destinationFileName))
                        progress.Report("Identified " + Path.GetFileName(possibleGame.originalFileName) + (possibleGame.entryPath == null ? "" : "[" + possibleGame.entryPath + "]") + " as " + Path.GetFileName(possibleGame.destinationFileName));
                    else
                        progress.Report("Couldn't identify " + Path.GetFileName(possibleGame.originalFileName));
                }

            });
            progress.Report(foundCount + " files identified in " + sw.Elapsed.ToString());
            sw.Restart();

            if (IdentifyOnly)
            {
                sw.Stop();
                return;
            }

            //step 4
            //start moving files. Requires a little bit of organizing in case a zip has multiple files and they are split between ID'd and un-ID'd. Might need an extra function
            var unidentified = filesToFind.Where(f => !f.isIdentified).ToList();
            var foundFiles = filesToFind.Where(f => f.isIdentified && !f.isDiscEntry).ToList();
            //var discsToHandle = filesToFind.Where(f => f.isIdentified && f.isDiscEntry).ToList();
            //var problemZips = unidentified.Where(w => foundFiles.Select(f => f.originalFileName).Distinct().ToList().Contains(w.originalFileName)).ToList(); //probably not optimally performing. Will need to work on this later.
            List<IGrouping<string, LookupEntry>> problemDiscs = foundFiles.GroupBy(f => f.destinationFileName).Where(ff => ff.Count() > 1).ToList(); //discs (and games) that want to hit the same destination file from different original files.
            foundFiles = foundFiles.Where(ff => !problemDiscs.Any(pd => pd.Key == ff.destinationFileName)).ToList();

            var plainFiles = foundFiles.Where(f => f.fileType == LookupEntryType.File).ToList();
            var zippedFiles = foundFiles.Where(f => f.fileType == LookupEntryType.ZipEntry).GroupBy(f => f.originalFileName).ToList();
            var raredFiles = foundFiles.Where(f => f.fileType == LookupEntryType.RarEntry).GroupBy(f => f.originalFileName).ToList();
            var taredFiles = foundFiles.Where(f => f.fileType == LookupEntryType.TarEntry).GroupBy(f => f.originalFileName).ToList();
            var sevenZippedFiles = foundFiles.Where(f => f.fileType == LookupEntryType.SevenZEntry).GroupBy(f => f.originalFileName).ToList();
            var gZippedFiles = foundFiles.Where(f => f.fileType == LookupEntryType.GZipEntry).GroupBy(f => f.originalFileName).ToList();
            progress.Report(unidentified.Count() + " files not identified in source folder.");

            progress.Report("Beginning file move/zip operations");
            sw.Restart();
            //Create all needed directories now, instead of attempting for each file.
            var dirsToMake = foundFiles.Select(f => f.console).Distinct().ToList();
            if (moveUnidentified)
                dirsToMake.Add("Unidentified");
            foreach (var dir in dirsToMake)
                Directory.CreateDirectory(destinationFolder + "\\" + dir);

            var plainFilesTask = Task.Factory.StartNew(() => HandlePlainFiles(plainFiles));
            var zipFilesTask = Task.Factory.StartNew(() => HandleZipEntries(zippedFiles));
            var rarFilesTask = Task.Factory.StartNew(() => HandleRarEntries(raredFiles));
            var tarFilesTask = Task.Factory.StartNew(() => HandleTarEntries(taredFiles));
            var sevenZipFilesTask = Task.Factory.StartNew(() => Handle7zEntries(sevenZippedFiles));
            var gZipFilesTask = Task.Factory.StartNew(() => HandleGZipEntries(gZippedFiles));

            Task.WaitAll(plainFilesTask, zipFilesTask, rarFilesTask, tarFilesTask, sevenZipFilesTask, gZipFilesTask); //, unidentifiedTask);
            var conflicts = Task.Factory.StartNew(() => HandlePotentialConflicts(problemDiscs));
            Task.WaitAll(conflicts);

            progress.Report(filesMovedOrExtracted + " files moved or extracted in " + sw.Elapsed.ToString());

            if (moveUnidentified)
            {
                sw.Restart();
                progress.Report("Moving unidentified files");
                var unidentifiedTask = Task.Factory.StartNew(() => HandleUnidentifiedFiles(unidentified, destinationFolder + "\\Unidentified\\"));
                Task.WaitAll(unidentifiedTask);
                progress.Report("Unidentified files moved in " + sw.Elapsed.ToString());
            }

            sw.Restart();

            //step 5
            //clean up. Remove empty folders?
            if (!PreserveOriginals)
            {
                foreach (var file in foundFiles)
                    File.Delete(file.originalFileName);
            }
            CleanupLoop(topFolder);
            progress.Report("Source Directory cleanup completed in " + sw.Elapsed.ToString());
            sw.Stop();
        }

        void CleanupLoop(string folder)
        {
            foreach (var subfolder in Directory.EnumerateDirectories(folder))
                CleanupLoop(subfolder);

            if (Directory.EnumerateFiles(folder).Count() == 0 && Directory.EnumerateDirectories(folder).Count() == 0)
                Directory.Delete(folder);
        }

        void HandleUnidentifiedFiles(List<LookupEntry> unidentifiedFiles, string unidentifiedFolder)
        {
            if (!moveUnidentified)
                return;

            Parallel.ForEach(unidentifiedFiles.GroupBy(u => u.originalFileName), (uf) => //If we refer to the original file more than once, EX because it's a zip file, we only want to move it once.
            {
                if (!File.Exists(unidentifiedFolder + Path.GetFileName(uf.Key)))
                    File.Move(uf.Key, unidentifiedFolder + Path.GetFileName(uf.Key));
                filesMovedOrExtracted++;
            });
        }

        void HandlePotentialConflicts(List<IGrouping<string, LookupEntry>> conflicts)
        {
            //These are identified as files that will conflict in a multithreaded scenario, so we will single-thread access to these.
            foreach (var entry in conflicts) //conflicts is the destination file. We want to swap these to the origin file.
            {
                foreach (var key in entry)
                {
                    var listEntry = new List<LookupEntry>() { key };
                    var groupedEntry = listEntry.GroupBy(g => g.originalFileName).ToList();
                    switch (key.fileType)
                    {
                        case LookupEntryType.File:
                            HandlePlainFiles(listEntry);
                            break;
                        case LookupEntryType.GZipEntry:
                            HandleGZipEntries(groupedEntry);
                            break;
                        case LookupEntryType.RarEntry:
                            HandleRarEntries(groupedEntry);
                            break;
                        case LookupEntryType.SevenZEntry:
                            Handle7zEntries(groupedEntry);
                            break;
                        case LookupEntryType.TarEntry:
                            HandleTarEntries(groupedEntry);
                            break;
                        case LookupEntryType.ZipEntry:
                            HandleZipEntries(groupedEntry);
                            break;
                    }
                }
            }
        }

        void HandlePlainFiles(List<LookupEntry> plainFiles)
        {
            Parallel.ForEach(plainFiles, (pf) =>
            {
                if (pf.originalFileName == pf.destinationFileName) //dont bother moving a file onto itself.
                    return;

                if (ZipInsteadOfMove)
                {
                    if (!pf.isDiscEntry)
                        CreateZip(pf.originalFileName, pf.destinationFileName + ".zip", Path.GetFileName(pf.destinationFileName));
                    else
                    {
                        //Add this to a zip of all disc entries. Note the lack of .zip extention on a disc entry.
                        CreateZip(pf.originalFileName, pf.destinationFileName, pf.discEntryName);
                    }
                }
                else
                {
                    if (!File.Exists(pf.destinationFileName))
                        File.Move(pf.originalFileName, pf.destinationFileName);
                }

                filesMovedOrExtracted++;
            });
        }

        void HandleZipEntries(List<IGrouping<string, LookupEntry>> zippedFiles)
        {
            Parallel.ForEach(zippedFiles, (zf) =>
            {
                var zipFile = ZipFile.OpenRead(zf.Key); //might have multiple files to extract from a zip, thats why these are grouped.
                foreach (var entryToFind in zf)
                {
                    var entry = zipFile.Entries.Where(e => e.FullName == entryToFind.entryPath).FirstOrDefault();
                    if (!File.Exists(entryToFind.destinationFileName) && !ZipInsteadOfMove)
                    {
                        entry.ExtractToFile(entryToFind.destinationFileName); //Don't overwrite existing files in this path.
                    }
                    else if (ZipInsteadOfMove) //dont zip if the file doesnt exist
                    {
                        if (!entryToFind.isDiscEntry)
                        {
                            byte[] fileData = new byte[entry.Length];
                            new BinaryReader(entry.Open()).Read(fileData, 0, (int)entry.Length);
                            MemoryStream ms = new MemoryStream();
                            using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Update, true))
                            {
                                var zippedFile = za.CreateEntry(entryToFind.entryPath);
                                using (var zfDest = zippedFile.Open())
                                    zfDest.Write(fileData);
                            }

                            using (FileStream fs = new FileStream(entryToFind.destinationFileName + ".zip", FileMode.OpenOrCreate))
                            {
                                ms.Position = 0;
                                ms.CopyTo(fs);
                                ms.Close();
                                ms.Dispose();
                            }
                        }
                        else
                        {
                            //more complicated because the file may already exist on disc. This causes access errors again, since different threads might want to write to the same zip file.
                            byte[] fileData = new byte[entry.Length];
                            new BinaryReader(entry.Open()).Read(fileData, 0, (int)entry.Length);
                            FileStream fs = new FileStream(entryToFind.destinationFileName, FileMode.OpenOrCreate);
                            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Update, true))
                            {
                                if (!za.Entries.Any(e => e.FullName == entryToFind.discEntryName))
                                {
                                    var zippedFile = za.CreateEntry(entryToFind.discEntryName);
                                    using (var zfDest = zippedFile.Open())
                                        zfDest.Write(fileData);
                                }
                            }
                        }

                    }
                }
                filesMovedOrExtracted++;
                zipFile.Dispose();
            });
        }

        void InnerArchiveLoop(SharpCompress.Archives.IArchive file, IGrouping<string, LookupEntry> entries)
        {
            foreach (var entryToFind in entries)
            {
                if (!File.Exists(entryToFind.destinationFileName))
                {
                    var entry = file.Entries.Where(e => e.Key == entryToFind.entryPath).FirstOrDefault();
                    byte[] fileData = new byte[entry.Size];
                    new BinaryReader(entry.OpenEntryStream()).Read(fileData, 0, (int)entry.Size);

                    if (ZipInsteadOfMove)
                    {
                        if (!entryToFind.isDiscEntry)
                        {
                            MemoryStream ms = new MemoryStream();
                            using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Update, true))
                            {
                                var zippedFile = za.CreateEntry(entryToFind.entryPath);
                                using (var zf = zippedFile.Open())
                                    zf.Write(fileData);
                            }
                            using (FileStream fs = new FileStream(entryToFind.destinationFileName + ".zip", FileMode.OpenOrCreate))
                            {
                                ms.Position = 0;
                                ms.CopyTo(fs);
                                ms.Close();
                                ms.Dispose();
                            }
                        }
                        else
                        {
                            //disc entries
                            MemoryStream ms = new MemoryStream();
                            using (ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Update, true))
                            {
                                var zippedFile = za.CreateEntry(entryToFind.entryPath);
                                using (var zf = zippedFile.Open())
                                    zf.Write(fileData);
                            }
                            using (FileStream fs = new FileStream(entryToFind.destinationFileName, FileMode.OpenOrCreate))
                            {
                                ms.Position = 0;
                                ms.CopyTo(fs);
                                ms.Close();
                                ms.Dispose();
                            }
                        }

                    }
                    else
                        File.WriteAllBytes(entryToFind.destinationFileName, fileData);
                }
                filesMovedOrExtracted++;
            }
        }

        void HandleRarEntries(List<IGrouping<string, LookupEntry>> raredFiles)
        {
            Parallel.ForEach(raredFiles, (rf) =>
            {
                var rarFile = SharpCompress.Archives.Rar.RarArchive.Open(rf.Key);//might have multiple files to extract from a zip, thats why these are grouped.
                InnerArchiveLoop(rarFile, rf);
                rarFile.Dispose();
            });
        }

        void Handle7zEntries(List<IGrouping<string, LookupEntry>> sevenZdFiles)
        {
            Parallel.ForEach(sevenZdFiles, (sz) =>
            {
                var sevenZFile = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(sz.Key);//might have multiple files to extract from a zip, thats why these are grouped.
                InnerArchiveLoop(sevenZFile, sz);
                sevenZFile.Dispose();
            });
        }

        void HandleGZipEntries(List<IGrouping<string, LookupEntry>> gZippedFiles)
        {
            Parallel.ForEach(gZippedFiles, (gz) =>
            {
                var gzFile = SharpCompress.Archives.GZip.GZipArchive.Open(gz.Key);//might have multiple files to extract from a zip, thats why these are grouped.
                InnerArchiveLoop(gzFile, gz);
                gzFile.Dispose();
            });
        }

        void HandleTarEntries(List<IGrouping<string, LookupEntry>> taredFiles)
        {
            Parallel.ForEach(taredFiles, (tf) =>
            {
                var tarFile = SharpCompress.Archives.Tar.TarArchive.Open(tf.Key);//might have multiple files to extract from a zip, thats why these are grouped.
                InnerArchiveLoop(tarFile, tf);
                tarFile.Dispose();
            });
        }

        //static void MoveFile(string folderpath, string localFilepath, string newFileName)
        //{
        //    //Sanity check - if we would move this file to it's current location, don't. (EX: scanning a folder named Unidentified and finding an unidentified file)
        //    if (localFilepath == folderpath + "\\" + newFileName)
        //        return;

        //    Directory.CreateDirectory(folderpath); //does nothing if folder already exists
        //    if (!File.Exists(folderpath + "\\" + newFileName))
        //        File.Move(localFilepath, folderpath + "\\" + newFileName);
        //    else if (localFilepath != folderpath + "\\" + newFileName) //If we scan a directory that's also a destination, don't remove the file that we were going to move onto itself.
        //        File.Delete(localFilepath);
        //}

        //static void CreateSingleZip(string file, string zipPath)
        //{
        //    //Directory.CreateDirectory(Path.GetDirectoryName(zipPath)); //does nothing if folder already exists
        //    FileStream fs;
        //    if (!File.Exists(zipPath))
        //    {
        //        fs = new FileStream(zipPath, FileMode.CreateNew);
        //    }
        //    else
        //        return; //We arent going to handle dupes.

        //    ZipArchive zf = new ZipArchive(fs, ZipArchiveMode.Create);

        //    zf.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);


        //    zf.Dispose();
        //    fs.Close();
        //    fs.Dispose();
        //}

        static void CreateZip(string file, string zipPath, string entryName)
        {
            FileStream fs = new FileStream(zipPath, FileMode.OpenOrCreate);
            ZipArchive zf = new ZipArchive(fs, ZipArchiveMode.Update);

            if (!zf.Entries.Any(e => e.FullName == entryName)) //sanity check to avoid adding duplicate files to a zip
                zf.CreateEntryFromFile(entryName, Path.GetFileName(file), CompressionLevel.Optimal);

            zf.Dispose();
            fs.Close();
            fs.Dispose();
        }
    }
}
