using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace TinyMUD
{
	public static class Loop
	{
		public interface Event { }

		private class EmptyEvent : Event { }
		private static readonly Event _EmptyEvent = new EmptyEvent();

		private static Action<Exception> errfn = e => { };
		private static readonly object actions_mtx = new object();
		private static List<Action> actions = new List<Action>();
		private static List<Action> actions_tmp = new List<Action>();
		private static readonly Queue<KeyValuePair<Action, Action>> async_actions = new Queue<KeyValuePair<Action, Action>>();
		private static List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>> operations = new List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>>();
		private static List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>> operations_tmp = new List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>>();
		private static readonly List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>> operations_action = new List<KeyValuePair<AsyncOperation, Action<AsyncOperation>>>();
		private static readonly Dictionary<string, Dictionary<Delegate, Action<string, Event>>> events = new Dictionary<string,Dictionary<Delegate,Action<string,Event>>>();
		private static readonly Stack<List<Action<string, Event>>> events_action = new Stack<List<Action<string, Event>>>();
		private static List<Action> idles = new List<Action>();
		private static List<Action> idles_tmp = new List<Action>();
		private static readonly SortedDictionary<string, Action> always_actions = new SortedDictionary<string, Action>();
		private static readonly List<Action> always_actions_order = new List<Action>();
		private static bool always_actions_invaild = false;
		private static readonly List<Exception> exceptions = new List<Exception>();
		private static bool initialized = false;
		private static bool main_thread_init = false;
		private static int main_thread = 0;
		private static int now_threads = 0;

		public static int MaxAsync = 8;

		public static bool IsCurrent
		{
			get { return main_thread_init && main_thread == Thread.CurrentThread.ManagedThreadId; }
		}

		public static void Initialize()
		{
			if (!initialized)
			{
				if (!Application.isPlaying)
					return;
				initialized = true;
				GameObject go = new GameObject("Loop");
				UnityObject.DontDestroyOnLoad(go);
				go.hideFlags |= HideFlags.HideInHierarchy;
				go.AddComponent<Updater>();
			}
		}

		public static void Execute(Action action)
		{
			if (main_thread_init && main_thread == Thread.CurrentThread.ManagedThreadId)
			{
				try
				{
					action();
				}
				catch (Exception e)
				{
					errfn(e);
				}
			}
			else
			{
				lock (actions_mtx)
				{
					actions.Add(action);
				}
			}
		}

		public static void ExecuteAsync(Action action, Action complete)
		{
			lock (async_actions)
			{
				async_actions.Enqueue(new KeyValuePair<Action, Action>(action, complete));
			}
			while (true)
			{
				if (now_threads >= MaxAsync)
					return;

				int old_threads = now_threads;
				if (Interlocked.CompareExchange(ref now_threads, old_threads + 1, old_threads) == old_threads)
					break;
			}
			ThreadPool.QueueUserWorkItem(AsyncAction);
		}

		private static void AsyncAction(object o)
		{
			while (true)
			{
				KeyValuePair<Action, Action> action;
				lock (async_actions)
				{
					if (async_actions.Count == 0)
						break;
					action = async_actions.Dequeue();
				}
				try
				{
					action.Key();
				}
				catch (Exception e)
				{
					lock (exceptions)
					{
						exceptions.Add(e);
					}
				}
				Execute(action.Value);
			}
			Interlocked.Decrement(ref now_threads);
		}

		public static void Wait<T>(this T operation, Action action) where T : AsyncOperation
		{
			if (operation == null)
				throw new ArgumentNullException("operation");
			if (action == null)
				throw new ArgumentNullException("action");

			operations.Add(new KeyValuePair<AsyncOperation, Action<AsyncOperation>>(operation, AsyncWaiter.Acquire(action).Emit));
		}

		public static void Wait<T>(this T operation, Action<T> action) where T : AsyncOperation
		{
			if (operation == null)
				throw new ArgumentNullException("operation");
			if (action == null)
				throw new ArgumentNullException("action");

			operations.Add(new KeyValuePair<AsyncOperation, Action<AsyncOperation>>(operation, AsyncWaiter<T>.Acquire(action).Emit));
		}

		public static void Cancel(this AsyncOperation operation)
		{
			if (operation == null)
				throw new ArgumentNullException("operation");

			for (int i = 0; i < operations.Count; ++i)
			{
				var kv = operations[i];
				if (kv.Key == operation)
					operations[i] = new KeyValuePair<AsyncOperation, Action<AsyncOperation>>(null, null);
			}
		}

		private class AsyncWaiter
		{
			public Action action;
			public readonly Action<AsyncOperation> Emit;

			public AsyncWaiter()
			{
				Emit = operation =>
				{
					action();
					action = null;
					stack.Push(this);
				};
			}

			public static AsyncWaiter Acquire(Action action)
			{
				if (stack.Count == 0)
					return new AsyncWaiter {action = action};
				AsyncWaiter waiter = stack.Pop();
				waiter.action = action;
				return waiter;
			}

			private static readonly Stack<AsyncWaiter> stack = new Stack<AsyncWaiter>();
		}

		private class AsyncWaiter<T> where T : AsyncOperation
		{
			public Action<T> action;
			public readonly Action<AsyncOperation> Emit;

			public AsyncWaiter()
			{
				Emit = operation =>
				{
					T t = operation as T;
					if (t != null)
						action(t);
					action = null;
					stack.Push(this);
				};
			}

			public static AsyncWaiter<T> Acquire(Action<T> action)
			{
				if (stack.Count == 0)
					return new AsyncWaiter<T> {action = action};
				AsyncWaiter<T> waiter = stack.Pop();
				waiter.action = action;
				return waiter;
			}

			private static readonly Stack<AsyncWaiter<T>> stack = new Stack<AsyncWaiter<T>>();
		}

		public static void RunAlways(string name, Action action)
		{
			if (action == null)
				throw new ArgumentNullException("action");
			always_actions_invaild = true;
			always_actions[name] = action;
		}

		public static void RemoveAlways(string name)
		{
			always_actions_invaild = true;
			always_actions.Remove(name);
		}

		public static void Idle(Action action)
		{
			if (action == null)
				throw new ArgumentNullException("action");
			idles.Add(action);
		}

		public static void Broadcast(string msg)
		{
			Broadcast(msg, _EmptyEvent);
		}

		public static void Broadcast(string msg, Event evt)
		{
			if (evt == null)
				throw new ArgumentNullException("evt");
			if (evt == _EmptyEvent)
				evt = null;
			Dictionary<Delegate, Action<string, Event>> actions;
			if (events.TryGetValue(msg, out actions))
			{
				var enumerator = actions.GetEnumerator();
				List<Action<string, Event>> list = events_action.Count == 0
					? new List<Action<string, Event>>()
					: events_action.Pop();
				while (enumerator.MoveNext())
				{
					list.Add(enumerator.Current.Value);
				}
				enumerator.Dispose();
				for (int i = 0; i < list.Count; ++i)
				{
					try
					{
						list[i](msg, evt);
					}
					catch (Exception e)
					{
						errfn(e);
					}
				}
				list.Clear();
				events_action.Push(list);
			}
		}

		private static Dictionary<Delegate, Action<string, Event>> eventActions(string msg)
		{
			Dictionary<Delegate, Action<string, Event>> actions;
			if (!events.TryGetValue(msg, out actions))
			{
				actions = new Dictionary<Delegate, Action<string, Event>>();
				events.Add(msg, actions);
			}
			return actions;
		}

		public static void Subscribe(string msg, Action action)
		{
			eventActions(msg).Add(action, (str, evt) => action());
		}

		public static void Subscribe(string msg, Action<string> action)
		{
			eventActions(msg).Add(action, (str, evt) => action(str));
		}

		public static void Subscribe<T>(string msg, Action<T> action) where T : class, Event
		{
			eventActions(msg).Add(action, (str, evt) =>
			{
				T t = evt as T;
				if (t != null)
					action(t);
			});
		}

		public static void Subscribe<T>(string msg, Action<string, T> action) where T : class, Event
		{
			eventActions(msg).Add(action, (str, evt) =>
			{
				T t = evt as T;
				if (t != null)
					action(str, t);
			});
		}

		public static void Unsubscribe(string msg, Delegate action)
		{
			Dictionary<Delegate, Action<string, Event>> actions;
			if (events.TryGetValue(msg, out actions))
			{
				if (actions.Remove(action) && actions.Count == 0)
					events.Remove(msg);
			}
		}

		public static void Error(Action<Exception> fn)
		{
			if (fn == null)
			{
				errfn = e => { };
			}
			else
			{
				errfn = e =>
				{
					try
					{
						fn(e);
					}
					catch (Exception)
					{
					}
				};
			}
		}

		private class Updater : MonoBehaviour
		{
			void Start()
			{
				main_thread = Thread.CurrentThread.ManagedThreadId;
				Thread.MemoryBarrier();
				main_thread_init = true;
			}

			void Update()
			{
				Clock.Update();

				#region 异步操作检测
				if (operations.Count > 0)
				{
					for (int i = 0, j = operations.Count; i < j; ++i)
					{
						var kv = operations[i];
						if (kv.Key != null && !kv.Key.isDone)
							operations_tmp.Add(kv);
						else if (kv.Value != null)
							operations_action.Add(kv);
					}
					operations.Clear();
					var tmp = operations;
					operations = operations_tmp;
					operations_tmp = tmp;
					for (int i = 0, j = operations_action.Count; i < j; ++i)
					{
						var kv = operations_action[i];
						try
						{
							kv.Value(kv.Key);
						}
						catch (Exception e)
						{
							errfn(e);
						}
					}
					operations_action.Clear();
				}
				#endregion

				#region 异步调用异常处理
				if (exceptions.Count > 0)
				{
					lock (exceptions)
					{
						for (int i = 0, j = exceptions.Count; i < j; ++i)
							errfn(exceptions[i]);
						exceptions.Clear();
					}
				}
				#endregion

				#region 跨线程执行请求
				if (actions.Count > 0)
				{
					lock (actions_mtx)
					{
						var tmp = actions_tmp;
						actions_tmp = actions;
						actions = tmp;
					}
					for (int i = 0, j = actions_tmp.Count; i < j; ++i)
					{
						try
						{
							actions_tmp[i]();
						}
						catch (Exception e)
						{
							errfn(e);
						}
					}
					actions_tmp.Clear();
				}
				#endregion

				#region 每帧更新调用
				if (always_actions_invaild)
				{
					always_actions_invaild = false;
					always_actions_order.Clear();
					foreach (var action in always_actions)
					{
						always_actions_order.Add(action.Value);
					}
				}
				for (int i = 0, j = always_actions_order.Count; i < j; ++i)
				{
					try
					{
						always_actions_order[i]();
					}
					catch (Exception e)
					{
						errfn(e);
					}
				}
				#endregion

				// 计时器调度
				Clock.Update(errfn);

				#region 帧尾调用
				if (idles.Count > 0)
				{
					var tmp = idles_tmp;
					idles_tmp = idles;
					idles = tmp;
					for (int i = 0; i < idles_tmp.Count; ++i)
					{
						try
						{
							idles_tmp[i]();
						}
						catch (Exception e)
						{
							errfn(e);
						}
					}
					idles_tmp.Clear();
				}
				#endregion
			}

			void OnApplicationQuit()
			{
				NetManager.ExitAll();
			}
		}
	}
}