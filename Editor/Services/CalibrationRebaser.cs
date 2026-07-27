#nullable enable annotations

using System;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class CalibrationRebaser
{
	public static void RebaseAnimationData(
		WeaponAnimationDocument document,
		CalibrationSnapshot previous )
	{
		var oldPlacement = WeaponAnimationMath.Compose(
			previous.PhysicalTransform,
			previous.FramingTransform );
		var newPlacement = WeaponAnimationMath.Compose(
			document.Calibration.PhysicalTransform,
			document.Calibration.FramingTransform );

		foreach ( var clip in document.Clips )
		{
			foreach ( var track in clip.Tracks )
			{
				var isRoot = track.Target.Equals( document.Rig.RootBone, StringComparison.OrdinalIgnoreCase )
					|| track.Target.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase );
				var isWorldControl = track.Target.StartsWith( "@" )
					&& ResolveControl( document, track.Target ) is { AttachedBone.Length: 0 };
				var isCamera = track.Kind == RigControlKind.Camera;
				if ( !isRoot && !isWorldControl && !isCamera )
					continue;

				foreach ( var key in track.Keys )
				{
					var original = new Transform( key.Position, key.Rotation, key.Scale );
					var relative = oldPlacement.ToLocal( original );
					var rebased = new Transform(
						newPlacement.PointToWorld( relative.Position ),
						newPlacement.Rotation * relative.Rotation,
						newPlacement.Scale * relative.Scale );
					key.Position = rebased.Position;
					key.Rotation = rebased.Rotation;
					key.Scale = rebased.Scale;
				}
			}
		}

		foreach ( var target in new[]
		{
			document.Binding.PrimaryHand,
			document.Binding.SupportHand,
			document.Binding.PrimaryElbowPole,
			document.Binding.SupportElbowPole
		}.Where( x => string.IsNullOrWhiteSpace( x.AttachedBone ) ) )
		{
			var relative = oldPlacement.ToLocal( target.Transform );
			target.Transform = new Transform(
				newPlacement.PointToWorld( relative.Position ),
				newPlacement.Rotation * relative.Rotation,
				newPlacement.Scale * relative.Scale );
		}

		foreach ( var working in document.Workspace.WorkingPoseOverrides )
		{
			var isRoot = working.Target.Equals( document.Rig.RootBone, StringComparison.OrdinalIgnoreCase )
				|| working.Target.Equals( "weapon_root", StringComparison.OrdinalIgnoreCase );
			var isWorldControl = working.Target.StartsWith( "@", StringComparison.Ordinal )
				&& ResolveControl( document, working.Target ) is { AttachedBone.Length: 0 };
			if ( !isRoot && !isWorldControl && working.Kind != RigControlKind.Camera )
				continue;

			var relative = oldPlacement.ToLocal( working.Transform );
			working.Transform = new Transform(
				newPlacement.PointToWorld( relative.Position ),
				newPlacement.Rotation * relative.Rotation,
				newPlacement.Scale * relative.Scale );
		}
	}

	public static void DiscardAnimationData( WeaponAnimationDocument document )
	{
		foreach ( var clip in document.Clips )
		{
			clip.Tracks.Clear();
			clip.VisibilityTracks.Clear();
			clip.Constraints.Clear();
			clip.Tags.Clear();
			clip.ParameterEvents.Clear();
			clip.ImportedSequence = "";
			clip.Readiness = ClipReadiness.NotStarted;
		}

		document.Binding = new ArmBindingDefinition();
		document.Workspace.WorkingPoseOverrides.Clear();
	}

	private static RigTarget? ResolveControl( WeaponAnimationDocument document, string name ) => name switch
	{
		"@primary_hand" => document.Binding.PrimaryHand,
		"@support_hand" => document.Binding.SupportHand,
		"@primary_elbow" => document.Binding.PrimaryElbowPole,
		"@support_elbow" => document.Binding.SupportElbowPole,
		_ => null
	};
}
