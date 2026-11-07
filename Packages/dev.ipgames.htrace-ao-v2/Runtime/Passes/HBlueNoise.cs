using IPGames.HTraceAO.Runtime.Data.Public;
using IPGames.HTraceAO.Runtime.RuntimeResources;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPGames.HTraceAO.Runtime.Passes
{
	internal static class HBlueNoise
	{
		internal static readonly  int g_OwenScrambledTexture = Shader.PropertyToID("g_OwenScrambledTexture");
		internal static readonly int g_ScramblingTileXSPP   = Shader.PropertyToID("g_ScramblingTileXSPP");
		internal static readonly int g_RankingTileXSPP      = Shader.PropertyToID("g_RankingTileXSPP");
		internal static readonly int g_ScramblingTexture    = Shader.PropertyToID("g_ScramblingTexture");
		internal static readonly int g_STBNTexture          = Shader.PropertyToID("g_STBNTexture");
		internal static readonly int g_STBNTexture2D        = Shader.PropertyToID("g_STBNTexture2D");
		
		private static         Texture2D _owenScrambledTexture;
		public static Texture2D OwenScrambledTexture
		{
			get
			{
				if (_owenScrambledTexture == null)
					_owenScrambledTexture = GetSharedResources().OwenScrambledNoise256;
				return _owenScrambledTexture;
			}
		}
		
		private static Texture2D _scramblingTileXSPP;
		public static Texture2D ScramblingTileXSPP
		{
			get
			{
				if (_scramblingTileXSPP == null)
					_scramblingTileXSPP = GetSharedResources().ScramblingTile8Spp;
				return _scramblingTileXSPP;
			}
		}
		private static Texture2D _rankingTileXSPP;
		public static Texture2D RankingTileXSPP
		{
			get
			{
				if (_rankingTileXSPP == null)
					_rankingTileXSPP = GetSharedResources().RankingTile8Spp;
				return _rankingTileXSPP;
			}
		}
		private static Texture2D _scramblingTexture;
		public static Texture2D ScramblingTexture
		{
			get
			{
				if (_scramblingTexture == null)
					_scramblingTexture = GetSharedResources().ScrambleNoise;
				return _scramblingTexture;
			}
		}
		private static Texture3D _stbnTexture;
		public static Texture3D STBNTexture
		{
			get
			{
				if (_stbnTexture == null)
					_stbnTexture = GetSharedResources().Stbn1D128X128X16;
				return _stbnTexture;
			}
		}
		private static Texture3D _stbnTexture2D;
		public static Texture3D STBNTexture2D
		{
			get
			{
				if (_stbnTexture2D == null)
					_stbnTexture2D = GetSharedResources().Stbn2D128X128X16;
				return _stbnTexture2D;
			}
		}

		private static HTraceAOResources.SharedResources GetSharedResources()
		{
			HTraceAOResources resources = HTraceAOSettings.ActiveResources;
			if (resources.Shared.IsValid)
				return resources.Shared;
#if UNITY_EDITOR
			return HTraceAOResources.CreateEditorResources().Shared;
#else
			return default;
#endif
		}
		
		public static void SetTextures(CommandBuffer cmd)
		{
			cmd.SetGlobalTexture(g_OwenScrambledTexture, OwenScrambledTexture);
			cmd.SetGlobalTexture(g_ScramblingTileXSPP,   ScramblingTileXSPP);
			cmd.SetGlobalTexture(g_RankingTileXSPP,      RankingTileXSPP);
			cmd.SetGlobalTexture(g_ScramblingTexture,    ScramblingTexture);
			cmd.SetGlobalTexture(g_STBNTexture,          STBNTexture);
			cmd.SetGlobalTexture(g_STBNTexture2D,        STBNTexture2D);
		}
	}
}
