using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartHunter.Core.Helpers;

namespace SmartHunter.Core
{
    public class ThreadedMemoryScan
    {
        public class Result
        {
            public AddressRange AddressRange { get; private set; }
            public List<ulong> Matches { get; private set; }
            public float ExecutionMilliseconds { get { return m_Stopwatch.ElapsedMilliseconds; } }

            Stopwatch m_Stopwatch;

            public Result(AddressRange addressRange)
            {
                AddressRange = addressRange;
                Matches = new List<ulong>();

                m_Stopwatch = new Stopwatch();
                m_Stopwatch.Start();
            }

            public void Complete()
            {
                m_Stopwatch.Stop();
            }
        }

        public class CompletedEventArgs : EventArgs
        {
            public List<Result> Results { get; private set; }

            public ulong FirstMatch
            {
                get
                {
                    return Results.OrderBy(result => result.AddressRange.Start).SelectMany(result => result.Matches).FirstOrDefault();
                }
            }

            public CompletedEventArgs(List<Result> results)
            {
                Results = results;
            }
        }

        class TaskData
        {
            public CancellationTokenSource CancellationTokenSource { get; private set; }
            public Result Result { get; private set; }

            public TaskData(CancellationTokenSource cancellationTokenSource, AddressRange addressRange)
            {
                CancellationTokenSource = cancellationTokenSource;
                Result = new Result(addressRange);
            }
        }

        public event EventHandler<CompletedEventArgs> Completed;

        ConcurrentDictionary<Task<List<ulong>>, TaskData> m_Tasks;
        Stopwatch m_Stopwatch;

        public Process Process { get; private set; }
        public BytePattern Pattern { get; private set; }
        public bool StopAfterFirst { get; private set; }

        volatile bool m_HasCompleted;
        int m_PendingTasks;
        public bool HasCompleted { get { return m_HasCompleted; } }
        public float ExecutionMilliseconds { get { return m_Stopwatch.ElapsedMilliseconds; } }

        public List<Result> Results
        {
            get
            {
                List<Result> results = new List<Result>();
                lock (m_Tasks)
                {
                    results = m_Tasks.Select(task => task.Value.Result).ToList();
                }

                return results;
            }
        }

        public ThreadedMemoryScan(Process process, BytePattern pattern, AddressRange addressRange, bool stopAfterFirst, int threads = 2)
        {
            Process = process;
            Pattern = pattern;
            StopAfterFirst = stopAfterFirst;

            m_Tasks = new ConcurrentDictionary<Task<List<ulong>>, TaskData>();

            m_Stopwatch = new Stopwatch();
            m_Stopwatch.Start();

            var addressRangeDivisions = MemoryHelper.DivideAddressRange(addressRange, threads);
            foreach (var addressRangeDivision in addressRangeDivisions)
            {
                ulong overlap = (ulong)Math.Max(0, pattern.Bytes.Length - 1);
                AddScanTask(new AddressRange(addressRangeDivision.Start,
                    Math.Min(addressRange.End - addressRangeDivision.Start, addressRangeDivision.Size + overlap)));
            }
            // Register every task before any can finish (including empty ranges).
            m_PendingTasks = m_Tasks.Count;
            if (m_PendingTasks == 0) TryComplete();
            foreach (var task in m_Tasks.Keys) task.Start(TaskScheduler.Default);
        }

        void AddScanTask(AddressRange addressRange)
        {
            var cancellationTokenSource = new CancellationTokenSource();

            Task<List<ulong>> task = new Task<List<ulong>>(() =>
            {
                try
                {
                    return MemoryHelper.FindPatternAddresses(Process, addressRange, Pattern, StopAfterFirst, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (!cancellationTokenSource.IsCancellationRequested) Log.WriteException(ex);
                }

                return null as List<ulong>;
            }, cancellationTokenSource.Token);

            task.ContinueWith((continuingTask) =>
            {
                try
                {
                    bool complete;
                    bool cancel;
                    lock (m_Tasks)
                    {
                        TaskData data;
                        if (!m_Tasks.TryGetValue(continuingTask, out data)) return;
                        data.Result.Complete();
                        if (HasCompleted) return;
                        var matches = continuingTask.Status == TaskStatus.RanToCompletion ? continuingTask.Result : null;
                        if (matches != null)
                        {
                            foreach (ulong address in matches)
                                if (!Pattern.MatchedAddresses.Contains(address)) Pattern.MatchedAddresses.Add(address);
                            data.Result.Matches.AddRange(matches);
                        }
                        cancel = StopAfterFirst && matches != null && matches.Count > 0;
                        complete = --m_PendingTasks == 0;
                    }
                    if (cancel) TryCancel();
                    else if (complete) TryComplete();
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.WriteException(ex);
                }
            });

            lock (m_Tasks)
            {
                m_Tasks.TryAdd(task, new TaskData(cancellationTokenSource, addressRange));
            }
        }

        public void TryCancel()
        {
            foreach (var memoryTaskData in m_Tasks.Values.ToList())
            {
                memoryTaskData.CancellationTokenSource.Cancel();
            }

            TryComplete();
        }

        void TryComplete()
        {
            lock (m_Tasks)
            {
                if (HasCompleted) return;
                m_Stopwatch.Stop();
                m_HasCompleted = true;
            }
            Completed?.Invoke(this, new CompletedEventArgs(Results));
        }
    }
}
