using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace LockPad.Core
{
    public sealed class SettingsStore
    {
        private const string FileName = "settings.json";
        private readonly string _directory;

        public SettingsStore(string directory)
        {
            if (directory == null) throw new ArgumentNullException("directory");
            _directory = directory;
        }

        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LockPad");
            }
        }

        /// <summary>Returns defaults when the file is missing or unreadable for any reason.</summary>
        public AppSettings Load()
        {
            try
            {
                string path = Path.Combine(_directory, FileName);
                if (!File.Exists(path)) return new AppSettings();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    var s = ser.ReadObject(fs) as AppSettings;
                    return (s ?? new AppSettings()).Normalized();
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return new AppSettings();
            }
        }

        /// <summary>Best effort: IO, access and serialization failures are swallowed.</summary>
        public void Save(AppSettings settings)
        {
            string temp = null;
            bool done = false;
            try
            {
                AppSettings n = (settings ?? new AppSettings()).Normalized();
                Directory.CreateDirectory(_directory);
                string target = Path.Combine(_directory, FileName);
                temp = Path.Combine(_directory, FileName + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");

                using (FileStream fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, n);
                    fs.Flush(true);
                }

                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                done = true;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // Best effort by design.
            }
            finally
            {
                if (!done && temp != null)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException)) { }
                }
            }
        }
    }
}
