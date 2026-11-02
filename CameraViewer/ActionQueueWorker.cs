using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CameraViewer
{
    public class ActionQueueWorker : IDisposable
    {
        private Thread _thread;
        private readonly ManualResetEvent _sleepSignal = new ManualResetEvent(initialState: false);
        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(initialState: false);
        private readonly ConcurrentQueue<TransformAction> _queue = new ConcurrentQueue<TransformAction>();
        private volatile bool _isRunning;

        public bool IsRunning => _isRunning;
        public int Count => _queue.Count;
        public int MaxCount { get; set; } = 1000;

        public void Start(ThreadPriority priority = ThreadPriority.Normal)
        {
            Stop();
            _isRunning = true;
            _stopSignal.Set();
            _thread = new Thread(() =>
            {
                while (_isRunning)
                {
                    _sleepSignal.WaitOne();
                    try
                    {
                        while (_queue.Count > 0 && _isRunning)
                        {
                            _stopSignal.WaitOne();
                            if (_queue.TryDequeue(out var action))
                                action.RunAction();
                        }
                    }
                    finally
                    {
                        _sleepSignal.Reset();
                    }
                }
                _isRunning = false;
            })
            {
                IsBackground = true,
                Priority = priority,
            };
            _thread.Start();
        }

        public void Enqueue(TransformAction item)
        {
            if (_queue.Count > MaxCount)
                throw new InvalidOperationException($"Queue overflow: max {MaxCount}");
            _queue.Enqueue(item);
            _sleepSignal.Set();
        }

        public void Continue() => _stopSignal.Set();
        public void Pause() => _stopSignal.Reset();

        public void Stop()
        {
            _isRunning = false;
            _sleepSignal.Set();
            _stopSignal.Set();
            try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            _sleepSignal.Dispose();
            _stopSignal.Dispose();
        }
    }
}
