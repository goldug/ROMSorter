﻿using RomDatabase5;
using SharpCompress.Archives;
using SharpCompress.Writers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RomSorter5WinForms
{
    public partial class Form2 : Form
    {
        Sorter sorter = null;
        public Form2()
        {
            sorter = new Sorter();
            InitializeComponent();
            txtDatPath.Text = Properties.Settings.Default.datFile;
            txtRomPath.Text = Properties.Settings.Default.romPath;
        }

        private void LockButtons()
        {
            btnCatalog.Enabled = false;
            btnCreateChds.Enabled= false;
            btnDatFolderSelect.Enabled=false;
            btnDetectDupes.Enabled=false;
            btnExtractChds.Enabled = false; 
            btnIdentifyAndZip.Enabled=false;    
            btnMakeDat.Enabled = false;
            btnRenameMultiFile.Enabled=false;
            btnRomFolderSelect.Enabled=false;
            btnUnzipAll.Enabled=false;
            btnVerify.Enabled=false;
            btnZipAllFiles.Enabled=false;
        }

        private void UnlockButtons()
        {
            btnCatalog.Enabled = true;
            btnCreateChds.Enabled = true;
            btnDatFolderSelect.Enabled = true;
            btnDetectDupes.Enabled = true;
            btnExtractChds.Enabled = true;
            btnIdentifyAndZip.Enabled = true;
            btnMakeDat.Enabled = true;
            btnRenameMultiFile.Enabled = true;
            btnRomFolderSelect.Enabled = true;
            btnUnzipAll.Enabled = true;
            btnVerify.Enabled = true;
            btnZipAllFiles.Enabled = true;
        }

        private void btnDatFolderSelect_Click(object sender, EventArgs e)
        {
            //Set the folder path box
            //Attempt to auto-import any dat files found
            //also read zip files for dats if present
            if (ofd1.ShowDialog() == DialogResult.OK)
            {
                Database.RebuildInitialDatabase(); //clears out the exisiting data in sqlite
                txtDatPath.Text = ofd1.FileName;
                System.Configuration.SettingsProperty prop = new System.Configuration.SettingsProperty("datFile");
                Properties.Settings.Default.datFile = txtDatPath.Text;
                Properties.Settings.Default.Save();
                //Progress<string> p = new Progress<string>(s => lblStatus.Text = s);
                Task.Factory.StartNew(() => DATImporter.ParseDatFileFast(ofd1.FileName));
            }
        }

        private void btnRomFolderSelect_Click(object sender, EventArgs e)
        {
            if (ofd1.ShowDialog() == DialogResult.OK)
            {
                txtRomPath.Text = System.IO.Path.GetDirectoryName(ofd1.FileName);
                Properties.Settings.Default.romPath = txtRomPath.Text;
                Properties.Settings.Default.Save();
                progressBar1.Maximum = sorter.FilesToScanCount;
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count();
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => DetectDupes(p));
            UnlockButtons();
        }

        private void DetectDupes(IProgress<string> p)
        {
            //Detect duplicates.
            Dictionary<string, string> crcHashes = new Dictionary<string, string>();
            Dictionary<string, string> md5Hashes = new Dictionary<string, string>();
            Dictionary<string, string> sha1Hashes = new Dictionary<string, string>();
            bool foundDupe = false;
            Hasher h = new Hasher();
            Directory.CreateDirectory(txtRomPath.Text + "\\Duplicates");
            foreach (var file in System.IO.Directory.EnumerateFiles(txtRomPath.Text))
            {
                var filename = Path.GetFileNameWithoutExtension(file);
                //TODO: check zipped data separately? Or assume stuff was run to make files consistent.
                p.Report(Path.GetFileName(file));
                //var fs = System.IO.File.Open(file, System.IO.FileMode.Open);
                string[] results;
                using (var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(file))
                using (var viewStream = mmf.CreateViewStream())
                {
                    results = h.HashFile(viewStream);
                }

                if (crcHashes.ContainsKey(results[0]) && md5Hashes.ContainsKey(results[1]) && sha1Hashes.ContainsKey(results[2]))
                {
                    // this is a dupe, we hit on all 3 hashes.
                    foundDupe = true;
                    var origName = crcHashes[results[0]];
                    var dirName = txtRomPath.Text + "\\Duplicates\\" + origName.Replace("(", "").Replace(")", "").Trim();
                    Directory.CreateDirectory(dirName);
                    File.Move(file, dirName + "\\" + Path.GetFileName(file));
                }
                crcHashes.TryAdd(results[0], filename);
                md5Hashes.TryAdd(results[1], filename);
                sha1Hashes.TryAdd(results[2], filename);
            }

            if (foundDupe)
                p.Report("Completed, duplicates found and moved.");
            else
                p.Report("Completed, no duplicates.");
        }

        private async void btnUnzipAll_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count();
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => UnzipLogic(p));
            UnlockButtons();
        }

        private async void btnZipAllFiles_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count();
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => ZipLogic(p));
            UnlockButtons();
        }

        private async void btnIdentifyAndZip_Click(object sender, EventArgs e)
        {
            if (txtDatPath.Text == "")
            {
                MessageBox.Show("You need to supply a dat file to identify games.");
                return;
            }

            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count() + 1;
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => IdentifyLogic(p));
            UnlockButtons();
        }

        private void IdentifyLogic(IProgress<string> progress)
        {
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).ToList();
            bool moveUnidentified = chkMoveUnidentified.Checked;
            if (moveUnidentified)
                Directory.CreateDirectory(txtRomPath.Text + "\\Unknown");

            bool useOffsets = chkUseIDOffsets.Checked;
            string errors = "";
            foreach (var file in files)
            {
                try
                {
                    progress.Report(Path.GetFileName(file));
                    //Identify it first.
                    var identifiedFile = sorter.IdentifyOneFile(file, useOffsets);
                    var destFileName = txtRomPath.Text + "\\" + (identifiedFile != "" ? identifiedFile : (moveUnidentified ? "\\Unknown\\" : "") + Path.GetFileName(file));

                    if (identifiedFile != destFileName)
                        File.Move(file, destFileName);

                }
                catch (Exception ex)
                {
                    errors += file + ": " + ex.Message + Environment.NewLine;
                }
            }

            progress.Report("Complete");
            if (errors != "")
                MessageBox.Show(errors);
        }

        private void IdentifyZipLogic(IProgress<string> progress)
        {
            //TODO: update this to match ZipLogic
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).ToList();
            bool moveUnidentified = chkMoveUnidentified.Checked;
            if (moveUnidentified)
                Directory.CreateDirectory(txtRomPath.Text + "\\Unknown");

            bool useOffsets = chkUseIDOffsets.Checked;
            string errors = "";
            foreach (var file in files)
            {
                try
                {
                    progress.Report(Path.GetFileName(file));
                    //Identify it first.
                    var identifiedFile = sorter.IdentifyOneFile(file, useOffsets);
                    var destFileName = txtRomPath.Text + "\\" + (identifiedFile != "" ? identifiedFile : (moveUnidentified ? "\\Unknown\\" : "") + Path.GetFileNameWithoutExtension(file)) + ".zip";

                    string tempfilename = Path.GetTempFileName();
                    SharpCompress.Archives.IArchive existingZip = null;
                    var fs = File.OpenRead(file);
                    var zfs = System.IO.File.Create(tempfilename);
                    var zf = new System.IO.Compression.ZipArchive(zfs, ZipArchiveMode.Create);
                    switch (System.IO.Path.GetExtension(file))
                    {
                        case ".zip":
                        case ".rar":
                        case ".gz":
                        case ".gzip":
                        case ".tar":
                        case ".7z": //7z is super slow, but it also doesn't actually support the stream method that would let it go faster.
                            existingZip = SharpCompress.Archives.ArchiveFactory.Open(fs);
                            RezipFromArchive(existingZip, zf);
                            break;
                        default:
                            zf.CreateEntryFromFile(file, Path.GetFileName(file));
                            break;
                    }
                    if (existingZip != null) existingZip.Dispose();
                    fs.Close(); fs.Dispose();
                    zf.Dispose();
                    File.Move(tempfilename, destFileName);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    //add 
                    errors += file + ": " + ex.Message + Environment.NewLine;
                }
            }

            progress.Report("Complete");
            if (errors != "")
                MessageBox.Show(errors);
        }

        private void ZipLogic(IProgress<string> progress)
        {
            //NOTE: 2 of my test set get correctly picked up in a NoIntro file. - NoIntro skips headers, TOSEC includes them.
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).ToList();
            int count = 1;
            foreach (var file in files)
            {
                progress.Report(count + "/" + files.Count() + ":" + Path.GetFileName(file));
                string tempfilename = Path.GetTempFileName();
                SharpCompress.Archives.IArchive existingZip = null;
                var fs = File.OpenRead(file);

                //Borks up on big files
                //var zf = SharpCompress.Writers.WriterFactory.Open(System.IO.File.Create(tempfilename), SharpCompress.Common.ArchiveType.Zip, options);
                var zfs = System.IO.File.Create(tempfilename);
                var zf = new System.IO.Compression.ZipArchive(zfs, ZipArchiveMode.Create);
                switch (System.IO.Path.GetExtension(file))
                {
                    case ".zip":
                    case ".rar":
                    case ".gz":
                    case ".gzip":
                    case ".tar":
                    case ".7z":
                        existingZip = SharpCompress.Archives.ArchiveFactory.Open(fs);
                        RezipFromArchive(existingZip, zf);
                        break;
                    default:
                        zf.CreateEntryFromFile(file, Path.GetFileName(file));
                        break;
                }
                if (existingZip != null) existingZip.Dispose();
                fs.Close(); fs.Dispose();
                zf.Dispose();
                zfs.Close(); zfs.Dispose();
                File.Move(tempfilename, Path.GetDirectoryName(file) + "\\" + Path.GetFileNameWithoutExtension(file) + ".zip", true);
                if (!file.EndsWith(".zip")) //we just overwrote this file, don't remove it.
                    File.Delete(file);
                count++;
            }
            progress.Report("Complete");
        }

        private void UnzipLogic(IProgress<string> progress)
        {
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).ToList();
            foreach (var file in files)
            {
                switch (Path.GetExtension(file))
                {
                    case ".zip":
                    case ".rar":
                    case ".gz":
                    case ".gzip":
                    case ".tar":
                    case ".7z":
                        var fs = File.OpenRead(file);
                        var existingZip = SharpCompress.Archives.ArchiveFactory.Open(fs);
                        if (existingZip != null)
                        {
                            foreach (var e in existingZip.Entries)
                                e.WriteToDirectory(txtRomPath.Text);

                            existingZip.Dispose();
                            fs.Close(); fs.Dispose();
                            System.IO.File.Delete(file);
                        }
                        break;
                }

                progress.Report(file);
            }
            progress.Report("Complete");
        }

        //Used only SharpCompress types
        //private void RezipFromArchive(SharpCompress.Archives.IArchive existingZip, SharpCompress.Writers.IWriter zf)
        //{
        //    foreach (var ez in existingZip.Entries)
        //    {
        //        if (!ez.IsDirectory)
        //            zf.Write(ez.Key, ez.OpenEntryStream());
        //    }
        //}

        private void RezipFromArchive(SharpCompress.Archives.IArchive existingZip, System.IO.Compression.ZipArchive zf)
        {
            foreach (var ez in existingZip.Entries)
            {
                if (!ez.IsDirectory)
                {
                    var tempFile = Path.GetTempFileName();
                    ez.WriteToFile(tempFile);
                    zf.CreateEntryFromFile(tempFile, ez.Key);
                    File.Delete(tempFile);
                }
            }
        }

        private async void btnCatalog_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count();
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => Catalog(p));
            UnlockButtons();
        }

        private void Catalog(IProgress<string> progress)
        {
            //Hash all files in directory, write results to a tab-separated values file 
            FileStream fs = File.OpenWrite(txtRomPath.Text + "\\catalog.tsv");
            StreamWriter sw = new StreamWriter(fs);
            sw.WriteLine("name\tmd5\tsha1\tcrc\tsize");
            Hasher hasher = new Hasher();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).Where(f => Path.GetFileName(f) != "catalog.tsv").ToList();
            foreach (var file in files)
            {
                progress.Report(file);
                var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(file);
                var viewStream = mmf.CreateViewStream();
                var hashes = hasher.HashFile(viewStream);
                sw.WriteLine(Path.GetFileName(file) + "\t" + hashes[0] + "\t" + hashes[1] + "\t" + hashes[2] + "\t" + viewStream.Length);
                viewStream.Close(); viewStream.Dispose();
                mmf.Dispose();
            }
            sw.Close(); sw.Dispose(); fs.Close(); fs.Dispose();
            progress.Report("Complete");
        }

        private async void btnVerify_Click(object sender, EventArgs e)
        {
            LockButtons();
            //Hash all files in directory, confirm if they do or don't match values in catalog TSV file.
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count();
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => Verify(p));
            UnlockButtons();
        }

        private void Verify(IProgress<string> progress)
        {
            bool alert = false;
            var files = File.ReadAllLines(txtRomPath.Text + "\\catalog.tsv");
            var foundfiles = new List<string>();
            var filesInFolder = Directory.EnumerateFiles(txtRomPath.Text).Where(s => Path.GetFileName(s) != "catalog.tsv").Select(s => Path.GetFileName(s)).ToList();
            Hasher hasher = new Hasher();
            foreach (var file in files.Skip(1))  //ignore header row.
            {
                string[] vals = file.Split("\t");
                foundfiles.Add(vals[0]);
                progress.Report(vals[0]);
                try
                {
                    using (var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(txtRomPath.Text + "\\" + vals[0]))
                    using (var viewStream = mmf.CreateViewStream())
                    {
                        var hashes = hasher.HashFile(viewStream);
                        if (vals[1] == hashes[0] && vals[2] == hashes[1] && vals[3] == hashes[2]) //intentionally leaving size out, despite recording it.
                        {
                            continue;
                        }
                        else
                        {
                            alert = true;
                            File.AppendAllText(txtRomPath.Text + "\\report.txt", vals[0] + " did not match:" + vals[1] + "|" + hashes[0] + " " + vals[2] + "|" + hashes[1] + " " + vals[3] + "|" + hashes[2] + " " + vals[4] + "|" + viewStream.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    alert = true;
                    File.AppendAllText(txtRomPath.Text + "\\report.txt", "Error checking on " + vals[0] + ":" + ex.Message);
                }
            }

            var missingfiles = filesInFolder.Except(foundfiles);
            foreach (var fif in missingfiles)
            {
                File.AppendAllText(txtRomPath.Text + "\\report.txt", "File " + fif + " not found in catalog");
            }
            if (!alert && missingfiles.Count() == 0)
                progress.Report("Complete, all files verified");
            else if (alert)
                progress.Report("Complete, error found, read report.txt for info");
            else
                progress.Report("Complete, uncataloged files found, read report.txt for info");
        }

        private async void btnRenameMultiFile_Click(object sender, EventArgs e)
        {
            if (txtDatPath.Text == "")
            {
                MessageBox.Show("You need to supply a dat file to identify games.");
                return;
            }

            LockButtons();
            var files = System.IO.Directory.EnumerateDirectories(txtRomPath.Text);
            if (chkZipInsteadOfFolders.Checked)
                files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count() + 1;
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => IdentifyMultiFileGames(p));
            UnlockButtons();
        }

        private void IdentifyMultiFileGames(IProgress<string> progress)
        {
            //TODO: might need a toggle between 'check zip files' and 'check subfolders'
            //writing code for 'check subfolders now', will adapt to zip files later.
            var topFolders = Directory.EnumerateDirectories(txtRomPath.Text).ToList();

            //TODO: need to handle perfect matches, missing files, and extra files separately?
            //keep in mind this will be run on MAME, SCUMMVM, and some CD format games.
            //MAME wants exact matches. The others can live with bonus content. 
            foreach(var folder in topFolders)
            {
                progress.Report(folder);



            }
            progress.Report("Completed");

        }

        private async void btnCreateChds_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).Where(f => f.EndsWith(".cue") || f.EndsWith(".iso"));
            progressBar1.Maximum = files.Count() + 1;
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => CreateChdLogic(p));
            UnlockButtons();
        }

        private void CreateChdLogic(IProgress<string> progress)
        {
            //TODO: either parse the cue to find all files to remove, or make a sub-folder for everything.
            foreach (var cue in Directory.EnumerateFiles(txtRomPath.Text).Where(f => f.EndsWith(".cue") || f.EndsWith(".iso")))
            {
                progress.Report(cue);
                var results = new CHD().CreateChd(cue);
                if (results)
                {
                    if (cue.EndsWith("cue"))
                    {
                        //find referenced files that were pulled in by the cue
                        var bins = FindBinsInCue(cue);
                        foreach (var b in bins)
                            File.Delete(b);
                    }
                    File.Delete(cue);
                }
            }
            progress.Report("Complete");
        }

        private List<string> FindBinsInCue(string cueFile)
        {
            //load file as lines
            //find lines like: FILE "1Xtreme (USA).bin" BINARY
            //remove ends, find all files in middle.
            string[] lines = File.ReadAllLines(cueFile);
            List<string> files = new List<string>();
            foreach (var line in lines)
            {
                var parts = line.Split('"');
                if (parts.Length != 3)
                    continue;

                var filename = parts[1];
                files.Add(filename);
            }

            return files;
        }

        private async void btnExtractChds_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text).Where(f => f.EndsWith(".chd"));
            progressBar1.Maximum = files.Count() + 1;
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => ExtractChdLogic(p));
            UnlockButtons();
        }
        
        private void ExtractChdLogic(IProgress<string> progress)
        {
            foreach (var chd in Directory.EnumerateFiles(txtRomPath.Text).Where(f => f.EndsWith(".chd")).ToList())
            {
                progress.Report(chd);
                var results = new CHD().ExtractCHD(chd);
                if (results)
                {
                    File.Delete(chd);
                }
            }
            progress.Report("Complete");
        }

        private async void btnMakeDat_Click(object sender, EventArgs e)
        {
            LockButtons();
            var files = System.IO.Directory.EnumerateFiles(txtRomPath.Text);
            progressBar1.Maximum = files.Count() + 1;
            progressBar1.Value = 0;

            Progress<string> p = new Progress<string>(s => { lblStatus.Text = s; if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
            await Task.Factory.StartNew(() => DatLogic(p));
            UnlockButtons();
        }

        private void DatLogic(IProgress<string> progress)
        {
            DatCreator.MakeDat(txtRomPath.Text, progress);
            progress.Report("Completed making DAT file");
        }
    }
}
