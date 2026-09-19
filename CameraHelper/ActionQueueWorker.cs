using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CameraHelper;

public class ActionQueueWorker
{
	protected Thread thread;

	private ManualResetEvent sleepSignal = new ManualResetEvent(initialState: false);

	private ManualResetEvent stopSignal = new ManualResetEvent(initialState: false);

	private ConcurrentQueue<IAction> queue = new ConcurrentQueue<IAction>();

	public bool IsRunning { get; protected set; }

	public int Count => queue.Count;

	public int MaxCount { get; set; } = 1000;

	public void Destroy()
	{
		IsRunning = false;
		thread?.Abort();
	}

	public void Start(ThreadPriority priority)
	{
		Destroy();
		IsRunning = true;
		Continue();
		thread = new Thread((ThreadStart)delegate
		{
			while (IsRunning)
			{
				sleepSignal.WaitOne();
				try
				{
					while (queue.Count > 0)
					{
						stopSignal.WaitOne();
						if (queue.TryDequeue(out var result))
						{
							result.RunAction();
						}
					}
				}
				finally
				{
					sleepSignal.Reset();
				}
			}
			IsRunning = false;
		})
		{
			IsBackground = true,
			Priority = priority
		};
		thread.Start();
	}

	public void Enqueue(IAction item)
	{
		if (Count > MaxCount)
		{
			throw new Exception($"超出缓存队列最大数量：{MaxCount}");
		}
		queue.Enqueue(item);
		sleepSignal.Set();
	}

	public void Continue()
	{
		stopSignal.Set();
	}

	public void Pause()
	{
		stopSignal.Reset();
	}

	public void Close()
	{
		IsRunning = false;
		sleepSignal.Reset();
		stopSignal.Reset();
	}
}
