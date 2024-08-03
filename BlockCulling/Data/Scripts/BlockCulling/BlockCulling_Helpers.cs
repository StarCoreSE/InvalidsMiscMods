using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Scripts.BlockCulling
{
    public static class ThreadSafeLog
    {
        private static readonly object _lock = new object();
        private static readonly Queue<string> _messageQueue = new Queue<string>();
        private static TextWriter _writer;
        public static bool EnableDebugLogging = false;

        private static bool _writerInitialized;  // Add this!

        private static TextWriter Writer
        {
            get
            {
                if (_writer == null && EnableDebugLogging)
                {
                    lock (_lock)
                    {
                        if (_writer == null && EnableDebugLogging)
                        {
                            try
                            {
                                string logFileName = GenerateLogFileName();
                                _writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFileName, typeof(ThreadSafeLog));
                                _writer.WriteLine("Debug log initialized: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                                _writer.Flush();
                            }
                            catch (Exception e)
                            {
                                MyLog.Default.WriteLineAndConsole(string.Format("BlockCulling: Failed to initialize debug log: {0}", e.Message));
                            }
                        }
                    }
                }
                return _writer;
            }
        }

        private static TextWriter EnsureWriter()  // Method, not Property!
        {
            if (!EnableDebugLogging) return null;

            lock (_lock)  // Single, always-respected lock
            {
                if (_writer == null && !_writerInitialized && EnableDebugLogging)
                {
                    try
                    {
                        _writerInitialized = true;  // Important! Set this first.
                        string logFileName = GenerateLogFileName();
                        _writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFileName, typeof(ThreadSafeLog));
                        _writer.WriteLine("Debug log initialized: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        _writer.Flush();
                    }
                    catch (Exception e)
                    {
                        _writerInitialized = false;  // Reset if initialization failed
                        MyLog.Default.WriteLineAndConsole(string.Format("BlockCulling: Failed to initialize debug log: {0}", e.Message));
                    }
                }
                return _writer;  // Always return from within lock
            }
        }

        private static string GenerateLogFileName()
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return string.Format("BlockCulling_Debug_{0}.log", timestamp);
        }

        public static void EnqueueMessage(string message)
        {
            if (!EnableDebugLogging || string.IsNullOrEmpty(message)) return;

            lock (_lock)
            {
                _messageQueue.Enqueue(string.Format("[{0:HH:mm:ss.fff}] {1}", DateTime.Now, message));
            }
        }

        public static void EnqueueMessageDebug(string message)
        {
            if (EnableDebugLogging)
            {
                EnqueueMessage(string.Format("[DEBUG] {0}", message));
            }
        }

        public static void ProcessLogQueue()
        {
            if (!EnableDebugLogging) return;

            lock (_lock)  // Single lock-context for whole method
            {
                TextWriter writer = EnsureWriter();  // Get writer under same lock as queue processing
                if (writer == null) return;

                while (_messageQueue.Count > 0)
                {
                    string msg = _messageQueue.Dequeue();
                    try
                    {
                        writer.WriteLine(msg);
                        writer.Flush();
                    }
                    catch (Exception e)
                    {
                        MyLog.Default.WriteLineAndConsole(string.Format("BlockCulling: Failed writing to debug log: {0}", e.Message));
                    }
                }
            }
        }

        public static void Close()
        {
            lock (_lock)
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Close();
                    _writer = null;
                }
            }
        }
    }
    public static class MainThreadDispatcher
    {
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();

        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
                _actionQueue.Enqueue(action);
            }
        }

        public static void Update()
        {
            lock (_lock)
            {
                while (_actionQueue.Count > 0)
                {
                    try
                    {
                        _actionQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ThreadSafeLog.EnqueueMessage($"Error in dispatched task: {ex.Message}");
                    }
                }
            }
        }
    }

    public class TaskScheduler
    {
        private const int MAX_TASKS_PER_FRAME = 10;
        private readonly object _lock = new object();
        private readonly Queue<Action> _taskQueue = new Queue<Action>();

        public void EnqueueTask(Action task)
        {
            lock (_lock)
            {
                _taskQueue.Enqueue(task);
            }
        }

        public void ProcessTasks()
        {
            int processedCount = 0;
            lock (_lock)
            {
                while (_taskQueue.Count > 0 && processedCount < MAX_TASKS_PER_FRAME)
                {
                    MainThreadDispatcher.Enqueue(_taskQueue.Dequeue());
                    processedCount++;
                }
            }
        }
    }

    public class SafeEntityRef<TEntity> where TEntity : class, VRage.ModAPI.IMyEntity
    {
        private long _entityId;

        public SafeEntityRef(TEntity entity)
        {
            SetEntity(entity);
        }

        public void SetEntity(TEntity entity) => Interlocked.Exchange(ref _entityId, entity?.EntityId ?? 0);

        public bool TryGetEntity(out TEntity entity)
        {
            entity = MyAPIGateway.Entities.GetEntityById(Interlocked.Read(ref _entityId)) as TEntity;
            return entity != null;
        }
    }

    public class PerformanceMonitor
    {
        private const int REPORT_INTERVAL = 1000;
        private int _tickCount;
        private long _syncTimeTotal;
        private long _asyncTimeTotal;
        private int _tasksProcessedTotal;
        private long _blocksCulledTotal;   // New!
        private long _blocksUnculledTotal; // New!

        public void RecordSyncOperation(long milliseconds) => Interlocked.Add(ref _syncTimeTotal, milliseconds);
        public void RecordAsyncOperation(long milliseconds) => Interlocked.Add(ref _asyncTimeTotal, milliseconds);
        public void RecordTasksProcessed(int count) => Interlocked.Add(ref _tasksProcessedTotal, count);

        public void Update()
        {
            if (Interlocked.Increment(ref _tickCount) >= REPORT_INTERVAL)
            {
                GenerateReport();
                ResetCounters();
            }
        }

        public void RecordBlocksCulled(int count) => Interlocked.Add(ref _blocksCulledTotal, count);
        public void RecordBlocksUnculled(int count) => Interlocked.Add(ref _blocksUnculledTotal, count);

        private void GenerateReport()
        {
            double syncTimeAvg = (double)_syncTimeTotal / REPORT_INTERVAL;
            double asyncTimeAvg = (double)_asyncTimeTotal / REPORT_INTERVAL;
            double tasksPerSecond = (double)_tasksProcessedTotal / (REPORT_INTERVAL / 60.0);
            double blocksCulled = Interlocked.Read(ref _blocksCulledTotal);
            double blocksUnculled = Interlocked.Read(ref _blocksUnculledTotal);

            string report = $"Performance Report (Last {REPORT_INTERVAL} ticks):\n" +
                            $"Avg Sync Time: {syncTimeAvg:F2}ms\n" +
                            $"Avg Async Time: {asyncTimeAvg:F2}ms\n" +
                            $"Tasks/Second: {tasksPerSecond:F2} + " +
                            $"Blocks Culled: {blocksCulled:F2}\n" +  // New!
                            $"Blocks Unculled: {blocksUnculled:F2}"; // New!



            ThreadSafeLog.EnqueueMessage(report);
        }

        private void ResetCounters()
        {
            Interlocked.Exchange(ref _tickCount, 0);
            Interlocked.Exchange(ref _syncTimeTotal, 0);
            Interlocked.Exchange(ref _asyncTimeTotal, 0);
            Interlocked.Exchange(ref _tasksProcessedTotal, 0);
            Interlocked.Exchange(ref _blocksCulledTotal, 0);
            Interlocked.Exchange(ref _blocksUnculledTotal, 0);
        }
    }
}