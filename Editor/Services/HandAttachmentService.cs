#nullable enable annotations

using System;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class HandAttachmentService
{
	public static bool ChangeAttachment(
		WeaponAnimationDocument document,
		string controlName,
		string newAttachment )
	{
		var target = ResolveControl( document, controlName );
		if ( target is null
			|| target.AttachedBone.Equals( newAttachment, StringComparison.OrdinalIgnoreCase ) )
			return false;

		var skeleton = HostSkeletonBuilder.Build( document, includeArmProfile: false );
		if ( !string.IsNullOrWhiteSpace( newAttachment )
			&& (!skeleton.ByName.TryGetValue( newAttachment, out var newBone)
				|| !newBone.IsWeaponBone) )
			return false;

		var oldAttachment = target.AttachedBone;
		target.Transform = Rebase(
			target.Transform,
			ParentBindTransform( skeleton, oldAttachment ),
			ParentBindTransform( skeleton, newAttachment ) );

		foreach ( var clip in document.Clips )
		{
			var track = clip.Tracks.FirstOrDefault( x =>
				x.Target.Equals( controlName, StringComparison.OrdinalIgnoreCase ) );
			if ( track is not null )
			{
				foreach ( var key in track.Keys )
				{
					var pose = AnimationPoseEvaluator.Evaluate(
						document,
						skeleton,
						clip,
						key.Time,
						includeWorkingPose: false );
					var rebased = Rebase(
						new Transform( key.Position, key.Rotation, key.Scale ),
						ParentPoseTransform( pose, oldAttachment ),
						ParentPoseTransform( pose, newAttachment ) );
					key.Position = rebased.Position;
					key.Rotation = rebased.Rotation.Normal;
					key.Scale = rebased.Scale;
				}
			}

			var working = document.Workspace.GetWorkingPose( clip.Id, controlName );
			if ( working is null )
				continue;
			var workingPose = AnimationPoseEvaluator.Evaluate(
				document,
				skeleton,
				clip,
				document.Workspace.TimelineTime,
				includeWorkingPose: false );
			working.Transform = Rebase(
				working.Transform,
				ParentPoseTransform( workingPose, oldAttachment ),
				ParentPoseTransform( workingPose, newAttachment ) );
		}

		target.AttachedBone = newAttachment;
		return true;
	}

	private static RigTarget? ResolveControl(
		WeaponAnimationDocument document,
		string controlName ) => controlName switch
	{
		"@primary_hand" => document.Binding.PrimaryHand,
		"@support_hand" => document.Binding.SupportHand,
		_ => null
	};

	private static Transform? ParentBindTransform(
		HostSkeleton skeleton,
		string name )
	{
		return !string.IsNullOrWhiteSpace( name )
			&& skeleton.ByName.TryGetValue( name, out var bone )
				? bone.BindModelTransform
				: null;
	}

	private static Transform? ParentPoseTransform(
		EvaluatedPose pose,
		string name )
	{
		return !string.IsNullOrWhiteSpace( name )
			&& pose.Model.TryGetValue( name, out var transform )
				? transform
				: null;
	}

	private static Transform Rebase(
		Transform local,
		Transform? oldParent,
		Transform? newParent )
	{
		var world = oldParent is null
			? local
			: ComposeLocal( oldParent.Value, local );
		return newParent is null
			? world
			: newParent.Value.ToLocal( world );
	}

	private static Transform ComposeLocal( Transform parent, Transform local ) => new(
		parent.PointToWorld( local.Position ),
		parent.Rotation * local.Rotation,
		parent.Scale * local.Scale );
}
