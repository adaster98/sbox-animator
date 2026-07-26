#nullable enable annotations

using System;
using System.IO;
using Editor;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class RecoveryService
{
	public static void Write( WeaponAnimationDocument document )
	{
		try
		{
			AtomicFile.WriteAllText( GetPath( document.DocumentId ), Json.Serialize( document ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Weapon Animator] recovery write failed: {ex.Message}" );
		}
	}

	public static WeaponAnimationDocument? ReadNewerThan( Guid documentId, DateTime assetWriteUtc )
	{
		try
		{
			var path = GetPath( documentId );
			if ( !File.Exists( path ) || File.GetLastWriteTimeUtc( path ) <= assetWriteUtc )
				return null;
			return Json.Deserialize<WeaponAnimationDocument>( File.ReadAllText( path ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Weapon Animator] recovery read failed: {ex.Message}" );
			return null;
		}
	}

	public static void Clear( Guid documentId )
	{
		try
		{
			var path = GetPath( documentId );
			if ( File.Exists( path ) )
				File.Delete( path );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Weapon Animator] recovery cleanup failed: {ex.Message}" );
		}
	}

	private static string GetPath( Guid documentId )
	{
		var projectRoot = Project.Current?.RootDirectory?.FullName
			?? global::Editor.FileSystem.Content.GetFullPath( ".." );
		return Path.Combine(
			projectRoot,
			".weaponanim-recovery",
			$"{documentId:N}.json" );
	}
}
