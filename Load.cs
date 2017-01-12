using System;

namespace TinyMUD
{
	public static class Load
	{
		public static void Initialize()
		{
			Clock.Initialize();
			Loop.Initialize();
		}
	}
}