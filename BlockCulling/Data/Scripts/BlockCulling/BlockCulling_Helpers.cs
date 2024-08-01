using System;
using System.Collections.Generic;
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

        public static void EnqueueMessage(string message)
        {
            lock (_lock)
            {
                _messageQueue.Enqueue(message);
            }
        }

        public static void ProcessLogQueue()
        {
            lock (_lock)
            {
                while (_messageQueue.Count > 0)
                {
                    MyLog.Default.WriteLineAndConsole($"[BlockCulling]: {_messageQueue.Dequeue()}");
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

        private void GenerateReport()
        {
            double syncTimeAvg = (double)_syncTimeTotal / REPORT_INTERVAL;
            double asyncTimeAvg = (double)_asyncTimeTotal / REPORT_INTERVAL;
            double tasksPerSecond = (double)_tasksProcessedTotal / (REPORT_INTERVAL / 60.0);

            string report = $"Performance Report (Last {REPORT_INTERVAL} ticks):\n" +
                            $"Avg Sync Time: {syncTimeAvg:F2}ms\n" +
                            $"Avg Async Time: {asyncTimeAvg:F2}ms\n" +
                            $"Tasks/Second: {tasksPerSecond:F2}";

            ThreadSafeLog.EnqueueMessage(report);
        }

        private void ResetCounters()
        {
            Interlocked.Exchange(ref _tickCount, 0);
            Interlocked.Exchange(ref _syncTimeTotal, 0);
            Interlocked.Exchange(ref _asyncTimeTotal, 0);
            Interlocked.Exchange(ref _tasksProcessedTotal, 0);
        }
    }
}