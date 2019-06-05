using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThreadPool {
	public interface IJob {
		void Execute ();
	}

	static System.Threading.Thread[] pool;
	static ThreadSafeQueue<IJob> input = new ThreadSafeQueue<IJob>();
	static ThreadSafeQueue<IJob> output = new ThreadSafeQueue<IJob>();

	public static void Initialize () {
		int leave_cores_for_unity = 2;
		int thread_count = System.Environment.ProcessorCount - leave_cores_for_unity;
		thread_count = thread_count >= 1 ? thread_count : 1;

		var threads = new List<System.Threading.Thread>();
		for (int i=0; i<thread_count; ++i) {
			var thread = new System.Threading.Thread(thread_proc) {
				Priority = System.Threading.ThreadPriority.BelowNormal
			};
			threads.Add(thread);

			thread.Start();
		}

		pool = threads.ToArray();
	}

	public static void Push (IJob job) {
		input.Push(job);
	}
	public static IJob TryPop () {
		IJob job = null;
		output.TryPop(ref job);
		return job;
	}
	public static IJob Pop () {
		return output.Pop();
	}
	public static IJob[] PopAll () {
		return output.PopAll();
	}
	public static IJob[] PopAtLeastOne () {
		return output.PopAtLeastOne();
	}

	static void thread_proc () {
		for (;;) {
			var job = input.Pop();
			job.Execute();
			output.Push(job);
		}
	}
}

public class ThreadSafeQueue<T> {
	
	readonly Queue<T> q = new Queue<T>();

	public void Push (T item) {
		lock (q) {
			q.Enqueue(item);

			System.Threading.Monitor.Pulse(q);
		}
	}
	public T Pop () {
		lock (q) {
			while (q.Count == 0) {
				System.Threading.Monitor.Wait(q);
			}
			return q.Dequeue();
		}
	}
	public bool TryPop (ref T item) {
		lock (q) {
			if (q.Count == 0) {
				return false;
			}
			item = q.Dequeue();
			return true;
		}
	}
	public T[] PopAll () {
		lock (q) {
			var items = q.ToArray();
			q.Clear();
			return items;
		}
	}
	public T[] PopAtLeastOne () {
		lock (q) {
			while (q.Count == 0) {
				System.Threading.Monitor.Wait(q);
			}
			var items = q.ToArray();
			q.Clear();
			return items;
		}
	}
}
