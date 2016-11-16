//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace Emul8.Utilities
{
    public class TemporaryFilesManager
    {
        public static TemporaryFilesManager Instance { get; private set; }

        static TemporaryFilesManager()
        {
            Instance = new TemporaryFilesManager();
        }

        public string GetTemporaryFile()
        {
            lock(emulatorTemporaryPath)
            {
                string path;
                do
                {
                    var fileName = string.Format("{0}.tmp", Guid.NewGuid());
                    path = Path.Combine(emulatorTemporaryPath, fileName);
                    // this is guid, collision is very unlikely
                }
                while(File.Exists(path));

                using(File.Create(path))
                {
                    //that's the simplest way to create and NOT have the file open
                }

                var ofc = OnFileCreated;
                if(ofc != null)
                {
                    ofc(path);
                }

                return path;
            }
        }

        public void Cleanup()
        {
            foreach(var entry in Directory.GetDirectories(Directory.GetParent(emulatorTemporaryPath).FullName)
                .Where(x => x != emulatorTemporaryPath && x.StartsWith(otherEmulatorTempPrefix, StringComparison.Ordinal)
                    && !x.EndsWith(CrashSuffix, StringComparison.Ordinal)))
            {
                var pid = entry.Substring(otherEmulatorTempPrefix.Length);
                int processId;
                if(pid != null && int.TryParse(pid, out processId) && IsProcessAlive(processId))
                { 
                    continue;
                }
                ClearDirectory(entry);
            }
        }

        public event Action<string> OnFileCreated;

        public string EmulatorTemporaryPath
        {
            get
            { 
                return emulatorTemporaryPath;
            }
        }

        private TemporaryFilesManager()
        {
            if(AppDomain.CurrentDomain.IsDefaultAppDomain())
            {
                id = Process.GetCurrentProcess().Id.ToString();
            }
            else
            {
                id = string.Format("{0}:{1}", Process.GetCurrentProcess().Id, AppDomain.CurrentDomain.Id);
            }
            otherEmulatorTempPrefix = Path.Combine(Path.GetTempPath(), DirectoryPrefix);
            emulatorTemporaryPath = otherEmulatorTempPrefix + id;

            if(!Directory.Exists(emulatorTemporaryPath))
            {
                Directory.CreateDirectory(emulatorTemporaryPath);
            }
            Cleanup();
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                return proc != null && !proc.HasExited;
            }
            catch(ArgumentException)
            {
                return false;
            }
        }

        ~TemporaryFilesManager()
        {
            Cleanup();
            ClearDirectory(emulatorTemporaryPath);           
        }

        private static void ClearDirectory(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch(Exception e)
            {
                if(!(e is IOException || e is DirectoryNotFoundException || e is UnauthorizedAccessException))
                    throw;
            }
        }

        public const string CrashSuffix = "-crash";

        private readonly string otherEmulatorTempPrefix;
        private readonly string emulatorTemporaryPath; 
        private readonly string id;

        private const string DirectoryPrefix = "emul8-";
    }
}
