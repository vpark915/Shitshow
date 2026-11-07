using System;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEngine;

namespace IPGames.HTraceAO.Runtime.Data.Public
{
	[Serializable]
	public class DebugSettings
	{
		public bool                 ShowBowels           = false;
		public bool                 ShowFullDebugLog     = false;
		public LayerMask            HTraceLayer          = ~0;

		public bool TestCheckBox1;
		public bool TestCheckBox2;
		public bool TestCheckBox3;
	}
}
