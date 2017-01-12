using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace TinyMUD
{
	/// <summary>
	/// 表示原生类型的异步操作
	/// 这个接口只能用于异步方法声明
	/// </summary>
	public abstract class Async
	{
		public abstract void ExecuteStep(Action<System.Exception> cont);

		public class Exception : System.Exception
		{
			public Exception(System.Exception e) : base(e.Message, e) { }

			public override IDictionary Data
			{
				get { return InnerException.Data; }
			}
			public override string HelpLink
			{
				get { return InnerException.HelpLink; }
				set { InnerException.HelpLink = value; }
			}

			public override string Source
			{
				get { return InnerException.Source; }
				set { InnerException.Source = value; }
			}

			public override string StackTrace
			{
				get { return InnerException.StackTrace; }
			}

			public override System.Exception GetBaseException()
			{
				return InnerException.GetBaseException();
			}

			public override void GetObjectData(SerializationInfo info, StreamingContext context)
			{
				InnerException.GetObjectData(info, context);
			}

			public override string ToString()
			{
				return InnerException.ToString();
			}
		}

		internal static Action<System.Exception> errfn = e => { };
		public static void Error(Action<System.Exception> fn)
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
					catch (System.Exception)
					{
					}
				};
			}
		}

		/// <summary>
		/// 用于从异步操作返回值
		/// </summary>
		/// <example><code>
		/// // Returns "Hello world"
		/// IEnumerable&lt;Async.IAsync&gt; Hello()
		/// {
		///   yield return new Result&lt;String&gt;("Hello world");
		/// }
		/// </code></example>
		public class Result<T> : Async
		{
			public T ReturnValue { get; private set; }

			public Result(T value)
			{
				ReturnValue = value;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				throw new InvalidOperationException("Cannot call ExecuteStep on 'Result'.");
			}
		}

		#region dummy类型
		private class Dummy
		{
			private Dummy() { }
			static Dummy()
			{
				Value = new Dummy();
			}
			public static Dummy Value { get; private set; }
		}
		#endregion

		/// <summary>
		/// 合并给定的所有异步方法，返回一个异步方法，
		/// 该方法执行时会并行执行所有给定的方法
		/// </summary>
		public static Async Parallel(params IEnumerable<Async>[] operations)
		{
			return new Primitive(cont =>
			{
				bool[] completed = new bool[operations.Length];
				for (int i = 0; i < operations.Length; i++)
					ExecuteAndSet(operations[i], completed, i, cont).Execute();
			});
		}

		#region 实现
		private static IEnumerable<Async> ExecuteAndSet(IEnumerable<Async> op, bool[] flags, int index, Action cont)
		{
			foreach (Async async in op) yield return async;
			bool allSet = true;
			lock (flags)
			{
				flags[index] = true;
				foreach (bool b in flags) if (!b) { allSet = false; break; }
			}
			if (allSet) cont();
		}
		#endregion

		public class Primitive : Async
		{
			private readonly Action<Action> action1;
			private readonly Action<Action<System.Exception>> action2;

			public Primitive(Action<Action> action)
			{
				action1 = action;
			}

			public Primitive(Action<Action<System.Exception>> action)
			{
				action2 = action;
			}

			public Primitive(Func<AsyncCallback, object, IAsyncResult> begin, Action<IAsyncResult> end)
				: this(cont => begin(delegate(IAsyncResult res)
				{
					System.Exception exception = null;
					try
					{
						end(res);
					}
					catch (System.Exception e)
					{
						exception = e;
					}
					cont(exception);
				}, null)) {}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				bool IsMain = Loop.IsCurrent;
				if (action1 != null)
				{
					action1(() =>
					{
						if (IsMain)
						{
							Loop.Execute(() =>
							{
								cont(null);
							});
						}
						else
						{
							cont(null);
						}
					});
				}
				else
				{
					action2(e =>
					{
						if (IsMain)
						{
							Loop.Execute(() =>
							{
								cont(e);
							});
						}
						else
						{
							cont(e);
						}
					});
				}
			}
		}

		public class Primitive<T> : Async<T>
		{
			private readonly Action<Action<T>> action1;
			private readonly Action<Action<T, System.Exception>> action2;

			public Primitive(Action<Action<T>> action)
			{
				action1 = action;
			}

			public Primitive(Action<Action<T, System.Exception>> action)
			{
				action2 = action;
			}

			public Primitive(Func<AsyncCallback, object, IAsyncResult> begin, Func<IAsyncResult, T> end)
				: this(cont => begin(delegate(IAsyncResult res)
				{
					T t = default(T);
					System.Exception exception = null;
					try
					{
						t = end(res);
					}
					catch (System.Exception e)
					{
						exception = e;
					}
					cont(t, exception);
				}, null)) {}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				bool IsMain = Loop.IsCurrent;
				if (action1 != null)
				{
					action1(res =>
					{
						if (IsMain)
						{
							Loop.Execute(() =>
							{
								result = res;
								completed = true;
								cont(null);
							});
						}
						else
						{
							result = res;
							completed = true;
							cont(null);
						}
					});
				}
				else
				{
					action2((res, e) =>
					{
						if (IsMain)
						{
							Loop.Execute(() =>
							{
								result = res;
								completed = true;
								cont(e);
							});
						}
						else
						{
							result = res;
							completed = true;
							cont(e);
						}
					});
				}
			}
		}

		public static Primitive Create(Action<Action> action)
		{
			return new Primitive(action);
		}

		public static Primitive Create(Action<Action<System.Exception>> action)
		{
			return new Primitive(action);
		}

		public static Primitive Create(Func<AsyncCallback, object, IAsyncResult> begin, Action<IAsyncResult> end)
		{
			return new Primitive(begin, end);
		}

		public static Primitive<T> Create<T>(Action<Action<T>> action)
		{
			return new Primitive<T>(action);
		}

		public static Primitive<T> Create<T>(Action<Action<T, System.Exception>> action)
		{
			return new Primitive<T>(action);
		}

		public static Primitive<T> Create<T>(Func<AsyncCallback, object, IAsyncResult> begin, Func<IAsyncResult, T> end)
		{
			return new Primitive<T>(begin, end);
		}

		private class Operation<T> : Async<T> where T : AsyncOperation
		{
			private readonly T operation;

			public Operation(T operation)
			{
				this.operation = operation;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				operation.Wait(op =>
				{
					result = op;
					completed = true;
					cont(null);
				});
			}
		}

		private class WWW : Async<UnityEngine.WWW>
		{
			private readonly UnityEngine.WWW www;
			private Action action;

			public WWW(UnityEngine.WWW www)
			{
				this.www = www;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				action = () =>
				{
					if (www.isDone)
					{
						result = www;
						completed = true;
						cont(null);
					}
					else
					{
						Loop.Idle(action);
					}
				};
				Loop.Idle(action);
			}
		}

		private class Coroutine : Async
		{
			private readonly IEnumerator routine;
			private static Executer executer;

			public Coroutine(IEnumerator routine)
			{
				this.routine = routine;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				if (executer == null)
				{
					GameObject go = new GameObject("Coroutine");
					UnityEngine.Object.DontDestroyOnLoad(go);
					go.hideFlags |= HideFlags.HideInHierarchy;
					executer = go.AddComponent<Executer>();
				}
				executer.StartCoroutine(Transmit(cont));
			}

			private IEnumerator Transmit(Action<System.Exception> action)
			{
				yield return executer.StartCoroutine(routine);
				action(null);
			}

			private class Executer : MonoBehaviour {}
		}

		public static Async<T> Create<T>(T operation) where T : AsyncOperation
		{
			return new Operation<T>(operation);
		}

		public static Async<UnityEngine.WWW> Create(UnityEngine.WWW www)
		{
			return new WWW(www);
		}

		public static Async Create(IEnumerator routine)
		{
			return new Coroutine(routine);
		}

		public class ReturnResult<T> : Async<T>
		{
			private readonly IEnumerable<Async> iterator;
			private readonly bool failOnError;

			public ReturnResult(IEnumerable<Async> async) : this(async, true) { }

			public ReturnResult(IEnumerable<Async> async, bool failOnError)
			{
				this.iterator = async;
				this.failOnError = failOnError;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				AsyncExtensions.Run<T>(iterator.GetEnumerator(), (res, e) =>
				{
					result = res;
					completed = true;
					if (failOnError || e == null)
					{
						cont(e);
					}
					else
					{
						errfn(e);
						cont(null);
					}
				});
			}
		}

		public class ReturnNothing : Async
		{
			private readonly IEnumerable<Async> iterator;
			private readonly bool failOnError;

			public ReturnNothing(IEnumerable<Async> async) : this(async, true) { }

			public ReturnNothing(IEnumerable<Async> async, bool failOnError)
			{
				this.iterator = async;
				this.failOnError = failOnError;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				AsyncExtensions.Run(iterator.GetEnumerator(), e =>
				{
					if (failOnError || e == null)
					{
						cont(e);
					}
					else
					{
						errfn(e);
						cont(null);
					}
				});
			}
		}

		private class EmptyStep : Async<Dummy>
		{
			public EmptyStep()
			{
				result = Dummy.Value;
			}

			public override void ExecuteStep(Action<System.Exception> cont)
			{
				cont(null);
			}
		}

		public static readonly Async Empty = new EmptyStep();
	}

	/// <summary>
	/// 表示返回泛型类型的异步操作
	/// </summary>
	public abstract class Async<T> : Async
	{
		protected T result;
		protected bool completed = false;

#pragma warning disable 0108
		public T Result
#pragma warning restore 0108
		{
			get
			{
				if (!completed) throw new System.Exception("Operation not completed, did you forgot 'yield return'?");
				return result;
			}
		}
	}

	/// <summary>
	/// 为系统类型和异步操作提供一些扩展方法，以方便使用
	/// </summary>
	public static class AsyncExtensions
	{
		#region System Extensions
		/// <summary>
		/// 使用BeginRead从Stream中异步读取数据
		/// </summary>
		/// <param name="stream">需要读取数据的Stream</param>
		/// <param name="buffer">缓冲区。此方法返回时，该缓冲区包含指定的字符数组，该数组的 offset 和 (offset + count -1) 之间的值由从当前源中读取的字节替换</param>
		/// <param name="offset">buffer 中的从零开始的字节偏移量，从此处开始存储从当前流中读取的数据</param>
		/// <param name="count">最多要从Stream中读取的字节数</param>
		/// <returns>读入缓冲区中的总字节数。若返回零 (0)表示Stream已到达末尾</returns>
		public static Async<int> ReadBytesAsync(this Stream stream, byte[] buffer, int offset, int count)
		{
			return new Async.Primitive<int>(
				(callback, st) => stream.BeginRead(buffer, offset, count, callback, st),
				stream.EndRead);
		}

		/// <summary>
		/// 从流的当前位置到末尾异步读取所有数据
		/// </summary>
		/// <param name="stream">需要读取数据的Stream</param>
		/// <returns>读取的所有字节序列组成的数组</returns>
		public static IEnumerable<Async> ReadToEndAsync(this Stream stream)
		{
			MemoryStream ms = new MemoryStream();
			int read = -1;
			while (read != 0)
			{
				byte[] buffer = new byte[1024];
				Async<int> count = stream.ReadBytesAsync(buffer, 0, 1024);
				yield return count;

				ms.Write(buffer, 0, count.Result);
				read = count.Result;
			}

			ms.Seek(0, SeekOrigin.Begin);
			byte[] bytes = new byte[ms.Length];
			Array.Copy(ms.GetBuffer(), ms.Position, bytes, 0, bytes.Length);

			yield return new Async.Result<byte[]>(bytes);
		}
		#endregion

		#region Async Extensions
		/// <summary>
		/// 执行异步操作，并堵塞调用线程等待异步操作完成
		/// </summary>
		/// <param name="async">需要执行的异步操作</param>
		public static void ExecuteAndWait(this IEnumerable<Async> async)
		{
			ManualResetEvent wh = new ManualResetEvent(false);
			Exception exception = null;
			Run(async.GetEnumerator(),
				e =>
				{
					exception = e;
					wh.Set();
				});
			wh.WaitOne();
			if (exception != null)
				throw new Async.Exception(exception);
		}


		/// <summary>
		/// 执行异步操作，不等待结果返回
		/// </summary>
		/// <param name="async">需要执行的异步操作</param>
		public static void Execute(this IEnumerable<Async> async)
		{
			Run(async.GetEnumerator());
		}

		/// <summary>
		/// 执行异步操作，不等待结果返回
		/// </summary>
		/// <param name="async">需要执行的异步操作</param>
		public static void Execute(this Async async)
		{
			Execute(SimpleYield(async));
		}

		/// <summary>
		/// 在异步方法中执行另一个异步方法，假定该方法返回泛型类型
		/// </summary>
		public static Async<T> ExecuteAsync<T>(this IEnumerable<Async> async)
		{
			return new Async.ReturnResult<T>(async);
		}
		public static Async<T> ExecuteAsync<T>(this IEnumerable<Async> async, bool failOnError)
		{
			return new Async.ReturnResult<T>(async, failOnError);
		}

		/// <summary>
		/// 在异步方法中执行另一个异步方法，假定该方法没有返回值
		/// </summary>
		public static Async ExecuteAsync(this IEnumerable<Async> async)
		{
			return new Async.ReturnNothing(async);
		}
		public static Async ExecuteAsync(this IEnumerable<Async> async, bool failOnError)
		{
			return new Async.ReturnNothing(async, failOnError);
		}
		#endregion

		#region Implementation
		internal static void Run<T>(IEnumerator<Async> iterator, Action<T, Exception> cont)
		{
			bool result;
			try
			{
				result = iterator.MoveNext();
			}
			catch (Exception e)
			{
				cont(default(T), e);
				return;
			}
			if (!result)
			{
				cont(default(T),
					new InvalidOperationException(
						"Asynchronous workflow executed using 'ReturnResult' didn't return result using 'Result'!"));
			}
			else
			{
				var res = iterator.Current as Async.Result<T>;
				if (res != null)
				{
					cont(res.ReturnValue, null);
					return;
				}

				iterator.Current.ExecuteStep
					(e =>
					{
						if (e != null)
							cont(default(T), e);
						else
							Run(iterator, cont);
					});
			}
		}

		internal static void Run(IEnumerator<Async> iterator, Action<Exception> cont)
		{
			bool result;
			try
			{
				result = iterator.MoveNext();
			}
			catch (Exception e)
			{
				cont(e);
				return;
			}
			if (!result) { cont(null); return; }
			iterator.Current.ExecuteStep
				(e =>
				{
					if (e != null)
						cont(e);
					else
						Run(iterator, cont);
				});
		}

		private static void Run(IEnumerator<Async> iterator)
		{
			try
			{
				if (!iterator.MoveNext())
					return;
			}
			catch (Exception e)
			{
				Async.errfn(e);
				return;
			}
			iterator.Current.ExecuteStep
				(e =>
				{
					if (e != null)
						Async.errfn(e);
					else
						Run(iterator);
				});
		}

		private static IEnumerable<Async> SimpleYield(Async async)
		{
			yield return async;
		}
		#endregion
	}
}
