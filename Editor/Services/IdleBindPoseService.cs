#nullable enable annotations

using System;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class IdleBindPoseService
{
	public static void SeedFromCurrentBind(
		WeaponAnimationDocument document,
		HostSkeleton skeleton )
	{
		var idle = document.EnsureClip( WeaponClipRole.Idle );
		idle.Tracks.Clear();
		foreach ( var bone in skeleton.Bones.Where( x => x.IsWeaponBone ) )
		{
			var track = idle.EnsureTrack( bone.Name );
			track.Kind = RigControlKind.Weapon;
			WeaponAnimationMath.UpsertKey( track, 0, skeleton.GetBindLocal( bone ) );
		}

		idle.IsBindPoseSeed = true;
		idle.Readiness = ClipReadiness.Ready;
		document.Workspace.ClearWorkingPoses( idle.Id );
	}

	public static bool RepairUnintendedSelectionWrites(
		WeaponAnimationDocument document,
		HostSkeleton skeleton )
	{
		var idle = document.Clips.FirstOrDefault( x => x.Role == WeaponClipRole.Idle );
		if ( idle is null )
			return false;

		if ( idle.IsBindPoseSeed )
		{
			if ( MatchesCurrentBind( idle, skeleton ) )
				return false;

			SeedFromCurrentBind( document, skeleton );
			return true;
		}

		if ( !LooksLikeUnauthoredIdle( document, idle, skeleton )
			|| MatchesCurrentBind( idle, skeleton ) )
			return false;

		SeedFromCurrentBind( document, skeleton );
		return true;
	}

	public static void MarkAuthored( WeaponAnimationClip clip )
	{
		clip.IsBindPoseSeed = false;
	}

	private static bool LooksLikeUnauthoredIdle(
		WeaponAnimationDocument document,
		WeaponAnimationClip idle,
		HostSkeleton skeleton )
	{
		if ( document.Clips.Where( x => x.Id != idle.Id ).Any( HasAuthoredContent )
			|| idle.Constraints.Count > 0
			|| idle.Tags.Count > 0
			|| idle.VisibilityTracks.Any( x => x.Keys.Count > 0 )
			|| idle.ParameterEvents.Count > 0
			|| !string.IsNullOrWhiteSpace( idle.ImportedSequence )
			|| document.Workspace.WorkingPoseOverrides.Count > 0
			|| document.Binding.PrimaryHand.IsBound
			|| document.Binding.SupportHand.IsBound
			|| document.Binding.GripPoses.Count > 0
			|| document.Binding.CompletedChecklistItems.Count > 0 )
			return false;

		var weaponBones = skeleton.Bones.Where( x => x.IsWeaponBone ).ToList();
		if ( weaponBones.Count == 0 || idle.Tracks.Count < weaponBones.Count )
			return false;

		if ( idle.Tracks.Any( track =>
			track.Keys.Count != 1
			|| MathF.Abs( track.Keys[0].Time ) > 0.0001f ) )
			return false;

		return weaponBones.All( bone => idle.Tracks.Count( track =>
			track.Kind == RigControlKind.Weapon
			&& track.Target.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) ) == 1 );
	}

	private static bool HasAuthoredContent( WeaponAnimationClip clip ) =>
		clip.Tracks.Any( x => x.Keys.Count > 0 )
		|| clip.VisibilityTracks.Any( x => x.Keys.Count > 0 )
		|| clip.Constraints.Count > 0
		|| clip.Tags.Count > 0
		|| clip.ParameterEvents.Count > 0
		|| !string.IsNullOrWhiteSpace( clip.ImportedSequence );

	private static bool MatchesCurrentBind(
		WeaponAnimationClip idle,
		HostSkeleton skeleton )
	{
		var weaponBones = skeleton.Bones.Where( x => x.IsWeaponBone ).ToList();
		if ( idle.Tracks.Count != weaponBones.Count )
			return false;

		foreach ( var bone in weaponBones )
		{
			var track = idle.Tracks.SingleOrDefault( x =>
				x.Kind == RigControlKind.Weapon
				&& x.Target.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( track is null || track.Keys.Count != 1 )
				return false;

			var key = track.Keys[0];
			var keyed = new Transform( key.Position, key.Rotation, key.Scale );
			if ( !WeaponPoseProjection.TransformNear(
				keyed,
				skeleton.GetBindLocal( bone ) ) )
				return false;
		}

		return true;
	}
}
