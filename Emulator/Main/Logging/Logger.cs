//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//

using System;
using System.Collections.Generic;
using Emul8.Core;
using Emul8.Peripherals;
using Emul8.Utilities;
using System.Threading;
using System.Runtime.CompilerServices;
using System.IO;
using System.Collections.Concurrent;
using Antmicro.Migrant;
using Antmicro.Migrant.Hooks;
using Emul8.Exceptions;
using Emul8.Utilities.Collections;

namespace Emul8.Logging
{
    public static class Logger
    {
        public static void AddBackend(ILoggerBackend backend, string name, bool overwrite = false)
        {
            backendNames.AddOrUpdate(name, backend, (key, value) =>
            {
                if (!overwrite)
                {
                    throw new RecoverableException(string.Format("Backend with name '{0}' already exists", key));
                }
                value.Dispose();
                return backend;
            });
            backends.Add(backend);
        }

        public static IDictionary<string, ILoggerBackend> GetBackends()
        {
            return backendNames;
        }

        public static void Dispose()
        {
            foreach(var backend in backends.Items)
            {
                backend.Dispose();
            }
        }

        public static void Log(LogLevel type, string message, params object[] args)
        {
            Log(null, type, message, args);
        }

        public static void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public static void Noisy(string message)
        {
            Log(LogLevel.Noisy, message);
        }

        public static void DebugLog(this IEmulationElement e, string message)
        {
            Log(e, LogLevel.Debug, message);
        }

        public static void DebugLog(this IEmulationElement e, string message, params object[] args)
        {
            DebugLog(e, string.Format(message, args));
        }

        // TODO: zastanowić się nad późniejszym rozwiązywaniem tego tu
        public static void DebugLog(this IEmulationElement e, Func<string> messageGenerator)
        {
            DebugLog(e, messageGenerator());
        }

        public static void NoisyLog(this IEmulationElement e, string message)
        {
            Log(e, LogLevel.Noisy, message);
        }

        public static void NoisyLog(this IEmulationElement e, string message, params object[] args)
        {
            NoisyLog(e, string.Format(message, args));
        }

        public static void Log(this IEmulationElement e, LogLevel type, string message, params object[] args)
        {
            LogAs(e, type, message, args);
        }

        public static void LogAs(object o, LogLevel type, string message, params object[] args)
        {
            var emulationManager = EmulationManager.Instance;
            if(emulationManager != null)
            {
                ((ActualLogger)emulationManager.CurrentEmulation.CurrentLogger).ObjectInnerLog(o, type, message, args);
            }
        }

        public static void Trace(this IEmulationElement e, LogLevel type, string message = "", 
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string caller = null,
            [CallerFilePath] string fileName = null)
        {
            e.Log(type, String.Format("{0} in {1} ({2}@{3}).", message, caller, Path.GetFileName(fileName), lineNumber));
        }

        public static void Trace(this IEmulationElement e, string message = "", 
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string caller = null,
            [CallerFilePath] string fileName = null)
        {
            Trace(e, LogLevel.Debug, message, lineNumber, caller, fileName);
        }

        public static void LogUnhandledRead(this IPeripheral peripheral, long offset)
        {
            peripheral.Log(LogLevel.Warning, "Unhandled read from offset 0x{0:X}.", offset);
        }

        public static void LogUnhandledWrite(this IPeripheral peripheral, long offset, long value)
        {
            peripheral.Log(LogLevel.Warning, "Unhandled write to offset 0x{0:X}, value 0x{1:X}.", offset, value);
        }

        public static bool PrintFullName { get; set; }

        internal static ILogger GetLogger()
        {
            var logger = new ActualLogger();
            foreach(var backend in backends.Items)
            {
                backend.Reset();
            }
            return logger;
        }

        private static ulong nextEntryId = 0;
        private static ConcurrentDictionary<string, ILoggerBackend> backendNames = new ConcurrentDictionary<string, ILoggerBackend>();
        private static FastReadConcurrentCollection<ILoggerBackend> backends = new FastReadConcurrentCollection<ILoggerBackend>();

        private static string GetGenericName(object o)
        {
            if(Misc.IsPythonObject(o))
            {
                return Misc.GetPythonName(o);
            }
            var type = o.GetType();
            return PrintFullName ? type.FullName : type.Name;
        }

        internal class ActualLogger : ILogger
        {
            public ActualLogger()
            {
                Init();
            }

            public void Dispose() 
            {
                stopThread = true;
                cancellationToken.Cancel();
                loggingThread.Join();
            }

            public string GetMachineName(int id)
            {
                string objectName;
                string machineName;
                if(TryGetName(id, out objectName, out machineName))
                {
                    return machineName;
                }
                return null;
            }

            public string GetObjectName(int id)
            {
                string objectName;
                string machineName;
                if(TryGetName(id, out objectName, out machineName))
                {
                    return objectName;
                }
                return null;
            }

            public bool TryGetName(int id, out string objectName, out string machineName)
            {
                object obj;
                if(logSourcesMap.TryGetObject(id, out obj))
                {
                    if(EmulationManager.Instance.CurrentEmulation.TryGetEmulationElementName(obj, out objectName, out machineName))
                    {
                        return true;
                    }
                }

                objectName = null;
                machineName = null;
                return false;
            }

            public int GetOrCreateSourceId(object source)
            {
                return logSourcesMap.GetOrCreateId(source, () => Interlocked.Increment(ref nextNameId));
            }

            public bool TryGetSourceId(object source, out int id)
            {
                return logSourcesMap.TryGetId(source, out id);
            }

            public void ObjectInnerLog(object o, LogLevel type, string message, params object[] args)
            {
                int sourceId = (o == null) ? -1 : GetOrCreateSourceId(o);
                if(args.Length > 0)
                {
                    message = string.Format(message, args);
                }

                var entry = new LogEntry(CustomDateTime.Now, type, message, sourceId, Thread.CurrentThread.ManagedThreadId);
                entries.Add(entry);
            }

            private void LoggingThreadBody()
            {
                while(!stopThread)
                {
                    LogEntry entry;
                    try 
                    {
                        entry = entries.Take(cancellationToken.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // we set ids here to avoid the need of locking counter in `ObjectInnerLog`
                    entry.Id = Logger.nextEntryId++;
                    var backends = Logger.backends.Items;
                    for (int i = 0; i < backends.Length; i++)
                    {
                        backends[i].Log(entry);
                    }
                }
            }

            [PostDeserialization]
            private void Init()
            {
                logSourcesMap = new LogSourcesMap();
                nextNameId = 0;

                entries = new BlockingCollection<LogEntry>(10000);

                cancellationToken = new CancellationTokenSource();
                loggingThread = new Thread(LoggingThreadBody);
                loggingThread.IsBackground = true;
                loggingThread.Name = "Logging thread";
                loggingThread.Start();
            }

            [Transient]
            private Thread loggingThread;

            [Transient]
            private CancellationTokenSource cancellationToken;

            [Transient]
            private bool stopThread = false;

            [Transient]
            private BlockingCollection<LogEntry> entries;
            [Transient]
            private int nextNameId;
            [Transient]
            private LogSourcesMap logSourcesMap;

            private class LogSourcesMap
            {
                public LogSourcesMap()
                {
                    objectToIdMap = new ConcurrentDictionary<WeakWrapper<object>, int>();
                    idToObjectMap = new ConcurrentDictionary<int, WeakWrapper<object>>();
                }

                public int GetOrCreateId(object o, Func<int> idProvider)
                {
                    return objectToIdMap.GetOrAdd(WeakWrapper<object>.CreateForComparison(o), s => 
                    {
                        s.ConvertToRealWeakWrapper();

                        var id = idProvider();
                        idToObjectMap.TryAdd(id, s);
                        return id;
                    });
                }

                public bool TryGetId(object o, out int sourceId)
                {
                    return objectToIdMap.TryGetValue(WeakWrapper<object>.CreateForComparison(o), out sourceId);
                }

                public bool TryGetObject(int id, out object obj)
                {
                    WeakWrapper<object> outResult;
                    var result = idToObjectMap.TryGetValue(id, out outResult);
                    if(result)
                    {
                        return outResult.TryGetTarget(out obj);
                    }

                    obj = null;
                    return false;
                }

                private readonly ConcurrentDictionary<int, WeakWrapper<object>> idToObjectMap;
                private readonly ConcurrentDictionary<WeakWrapper<object>, int> objectToIdMap;
            }
        }
    }
}
