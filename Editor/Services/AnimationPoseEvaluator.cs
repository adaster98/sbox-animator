#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public sealed class EvaluatedPose
{
	public Dictionary<string, Transform> Model { get; } = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, Transform> Local { get; } = new( StringComparer.OrdinalIgnoreCase );
	public bool PrimaryReachable { get; set; } = true;
	public bool SupportReachable { get; set; } = true;
}

public static class AnimationPoseEvaluator
{
	public static EvaluatedPose Evaluate(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		WeaponAnimationClip? clip,
		float time,
		bool includeWorkingPose = false )
	{
		var local = skeleton.Bones.ToDictionary(
			x => x.Name,
			skeleton.GetBindLocal,
			StringComparer.OrdinalIgnoreCase );

		if ( clip is not null )
		{
			foreach ( var track in clip.Tracks.Where( x => !x.Muted ) )
			{
				if ( !local.TryGetValue( track.Target, out var fallback ) )
					continue;
				if ( !ShouldEvaluateTrack( document, skeleton, track ) )
					continue;
				local[track.Target] = WeaponAnimationMath.SampleTrack( track, time, fallback );
			}

			if ( includeWorkingPose )
			{
				foreach ( var working in document.Workspace.WorkingPoseOverrides.Where( x =>
					x.ClipId == clip.Id
					&& !x.Target.StartsWith( "@", StringComparison.Ordinal )
					&& local.ContainsKey( x.Target )
					&& ShouldEvaluateArmTarget(
						document,
						skeleton,
						x.Target,
						x.Kind ) ) )
				{
					local[working.Target] = working.Transform;
				}
			}
		}

		var pose = new EvaluatedPose();
		BuildModelTransforms( skeleton, local, pose.Model );
		Dictionary<string, Transform>? constraintStartModel = null;
		if ( clip?.Constraints.Any( x => x.MaintainOffset ) == true )
			constraintStartModel = BuildModelAtTime( skeleton, clip, clip.Constraints.Min( x => x.StartTime ) );

		if ( document.Binding.PrimaryHand.IsBound )
		{
			var primaryHand = ResolveAndConstrainControl(
				document.Binding.PrimaryHand,
				"@primary_hand",
				SamplePreviewControl(
					document,
					clip,
					"@primary_hand",
					time,
					document.Binding.PrimaryHand.Transform,
					includeWorkingPose ),
				clip,
				time,
				skeleton,
				pose.Model,
				constraintStartModel );
			var primaryPole = ResolveAndConstrainControl(
				document.Binding.PrimaryElbowPole,
				"@primary_elbow",
				SamplePreviewControl(
					document,
					clip,
					"@primary_elbow",
					time,
					document.Binding.PrimaryElbowPole.Transform,
					includeWorkingPose ),
				clip,
				time,
				skeleton,
				pose.Model,
				constraintStartModel );

			ApplyArmIk(
				pose,
				skeleton,
				local,
				true,
				primaryHand,
				primaryPole,
				"arm_upper_R",
				"arm_lower_R",
				"hand_R" );
		}

		if ( document.Binding.Configuration == GripConfiguration.TwoHanded
			&& document.Binding.SupportHand.IsBound )
		{
			var supportHand = ResolveAndConstrainControl(
				document.Binding.SupportHand,
				"@support_hand",
				SamplePreviewControl(
					document,
					clip,
					"@support_hand",
					time,
					document.Binding.SupportHand.Transform,
					includeWorkingPose ),
				clip,
				time,
				skeleton,
				pose.Model,
				constraintStartModel );
			var supportPole = ResolveAndConstrainControl(
				document.Binding.SupportElbowPole,
				"@support_elbow",
				SamplePreviewControl(
					document,
					clip,
					"@support_elbow",
					time,
					document.Binding.SupportElbowPole.Transform,
					includeWorkingPose ),
				clip,
				time,
				skeleton,
				pose.Model,
				constraintStartModel );
			ApplyArmIk(
				pose,
				skeleton,
				local,
				false,
				supportHand,
				supportPole,
				"arm_upper_L",
				"arm_lower_L",
				"hand_L" );
		}

		ApplyBoneConstraints( clip, time, pose.Model );

		foreach ( var bone in skeleton.Bones )
		{
			if ( !pose.Model.TryGetValue( bone.Name, out var modelTransform ) )
				continue;

			pose.Local[bone.Name] = string.IsNullOrWhiteSpace( bone.ParentName )
				|| !pose.Model.TryGetValue( bone.ParentName, out var parent )
				? modelTransform
				: parent.ToLocal( modelTransform );
		}

		return pose;
	}

	internal static bool ShouldEvaluateTrack(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		TransformTrack track ) =>
		ShouldEvaluateArmTarget( document, skeleton, track.Target, track.Kind );

	private static bool ShouldEvaluateArmTarget(
		WeaponAnimationDocument document,
		HostSkeleton skeleton,
		string target,
		RigControlKind kind )
	{
		if ( kind != RigControlKind.Arm )
			return true;

		if ( IsArmSide( skeleton, target, "_R" ) )
			return document.Binding.PrimaryHand.IsBound;
		if ( IsArmSide( skeleton, target, "_L" ) )
			return document.Binding.Configuration == GripConfiguration.TwoHanded
				&& document.Binding.SupportHand.IsBound;

		// Shared arm-root controls only affect a preview after at least one hand is bound.
		return document.Binding.PrimaryHand.IsBound
			|| (document.Binding.Configuration == GripConfiguration.TwoHanded
				&& document.Binding.SupportHand.IsBound);
	}

	private static bool IsArmSide(
		HostSkeleton skeleton,
		string target,
		string suffix )
	{
		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var current = target;
		while ( !string.IsNullOrWhiteSpace( current ) && visited.Add( current ) )
		{
			if ( current.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
				return true;
			if ( !skeleton.ByName.TryGetValue( current, out var bone ) )
				break;
			current = bone.ParentName;
		}

		return false;
	}

	private static void BuildModelTransforms(
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> local,
		Dictionary<string, Transform> model )
	{
		foreach ( var bone in skeleton.Bones )
		{
			var boneLocal = local[bone.Name];
			if ( string.IsNullOrWhiteSpace( bone.ParentName )
				|| !model.TryGetValue( bone.ParentName, out var parent ) )
			{
				model[bone.Name] = boneLocal;
				continue;
			}

			model[bone.Name] = ComposeLocal( parent, boneLocal );
		}
	}

	private static void ApplyArmIk(
		EvaluatedPose pose,
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> local,
		bool primary,
		Transform target,
		Transform pole,
		string upperName,
		string lowerName,
		string handName )
	{
		if ( !pose.Model.TryGetValue( upperName, out var upper )
			|| !pose.Model.TryGetValue( lowerName, out var lower )
			|| !pose.Model.TryGetValue( handName, out var hand ) )
			return;

		var solution = WeaponAnimationMath.SolveTwoBone(
			upper.Position,
			lower.Position,
			hand.Position,
			target.Position,
			pole.Position );

		var upperRotation = WeaponAnimationMath.RotationFromTo(
			lower.Position - upper.Position,
			solution.Elbow - solution.Root ) * upper.Rotation;
		var lowerRotation = WeaponAnimationMath.RotationFromTo(
			hand.Position - lower.Position,
			solution.End - solution.Elbow ) * lower.Rotation;
		pose.Model[upperName] = new Transform( solution.Root, upperRotation, upper.Scale );
		pose.Model[lowerName] = new Transform( solution.Elbow, lowerRotation, lower.Scale );
		pose.Model[handName] = new Transform( solution.End, target.Rotation, hand.Scale );
		RebuildSolvedDescendants(
			skeleton,
			local,
			pose.Model,
			upperName,
			[upperName, lowerName, handName],
			[] );

		if ( primary )
			pose.PrimaryReachable = solution.Reachable;
		else
			pose.SupportReachable = solution.Reachable;
	}

	private static void RebuildSolvedDescendants(
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> local,
		Dictionary<string, Transform> model,
		string parentName,
		HashSet<string> solvedBones,
		HashSet<string> visited )
	{
		if ( !visited.Add( parentName ) || !model.TryGetValue( parentName, out var parent ) )
			return;

		foreach ( var child in skeleton.Bones.Where( bone =>
			bone.ParentName.Equals( parentName, StringComparison.OrdinalIgnoreCase ) ) )
		{
			if ( !solvedBones.Contains( child.Name )
				&& local.TryGetValue( child.Name, out var childLocal ) )
			{
				model[child.Name] = ComposeLocal( parent, childLocal );
			}

			RebuildSolvedDescendants(
				skeleton,
				local,
				model,
				child.Name,
				solvedBones,
				visited );
		}
	}

	private static Transform ComposeLocal( Transform parent, Transform local ) => new(
		parent.PointToWorld( local.Position ),
		parent.Rotation * local.Rotation,
		parent.Scale * local.Scale );

	private static Transform ResolveTarget(
		RigTarget target,
		Transform sampledTransform,
		IReadOnlyDictionary<string, Transform> model )
	{
		if ( !string.IsNullOrWhiteSpace( target.AttachedBone )
			&& model.TryGetValue( target.AttachedBone, out var attached ) )
		{
			return new Transform(
				attached.PointToWorld( sampledTransform.Position ),
				attached.Rotation * sampledTransform.Rotation,
				attached.Scale * sampledTransform.Scale );
		}

		return sampledTransform;
	}

	private static Transform SampleControl(
		WeaponAnimationClip? clip,
		string target,
		float time,
		Transform fallback )
	{
		var track = clip?.Tracks.FirstOrDefault( x => x.Target == target );
		return track is null
			? fallback
			: WeaponAnimationMath.SampleTrack( track, time, fallback );
	}

	private static Transform SamplePreviewControl(
		WeaponAnimationDocument document,
		WeaponAnimationClip? clip,
		string target,
		float time,
		Transform fallback,
		bool includeWorkingPose )
	{
		if ( includeWorkingPose
			&& clip is not null
			&& document.Workspace.GetWorkingPose( clip.Id, target ) is { } working )
			return working.Transform;

		return SampleControl( clip, target, time, fallback );
	}

	private static Transform ResolveAndConstrainControl(
		RigTarget targetDefinition,
		string controlName,
		Transform sampled,
		WeaponAnimationClip? clip,
		float time,
		HostSkeleton skeleton,
		IReadOnlyDictionary<string, Transform> currentModel,
		IReadOnlyDictionary<string, Transform>? sharedStartModel )
	{
		var source = ResolveTarget( targetDefinition, sampled, currentModel );
		if ( clip is null )
			return source;

		foreach ( var constraint in clip.Constraints.Where( x =>
			x.SourceControl.Equals( controlName, StringComparison.OrdinalIgnoreCase )
			&& time >= x.StartTime && time <= x.EndTime ) )
		{
			if ( !currentModel.TryGetValue( constraint.TargetBone, out var constraintTarget ) )
				continue;

			var maintainedOffset = Transform.Zero;
			if ( constraint.MaintainOffset )
			{
				var startModel = sharedStartModel;
				if ( startModel is null
					|| clip.Constraints.Any( x => x.MaintainOffset && MathF.Abs( x.StartTime - constraint.StartTime ) > 0.00001f ) )
				{
					startModel = BuildModelAtTime( skeleton, clip, constraint.StartTime );
				}

				if ( startModel.TryGetValue( constraint.TargetBone, out var startTarget ) )
				{
					var startSample = SampleControl(
						clip,
						controlName,
						constraint.StartTime,
						targetDefinition.Transform );
					var startSource = ResolveTarget( targetDefinition, startSample, startModel );
					maintainedOffset = startTarget.ToLocal( startSource );
				}
			}

			source = ClipConstraintEvaluator.Apply(
				source,
				constraintTarget,
				constraint,
				time,
				maintainedOffset );
		}

		return source;
	}

	private static Dictionary<string, Transform> BuildModelAtTime(
		HostSkeleton skeleton,
		WeaponAnimationClip clip,
		float time )
	{
		var local = skeleton.Bones.ToDictionary(
			x => x.Name,
			skeleton.GetBindLocal,
			StringComparer.OrdinalIgnoreCase );
		foreach ( var track in clip.Tracks.Where( x => !x.Muted && local.ContainsKey( x.Target ) ) )
			local[track.Target] = WeaponAnimationMath.SampleTrack( track, time, local[track.Target] );

		var model = new Dictionary<string, Transform>( StringComparer.OrdinalIgnoreCase );
		BuildModelTransforms( skeleton, local, model );
		return model;
	}

	private static void ApplyBoneConstraints(
		WeaponAnimationClip? clip,
		float time,
		Dictionary<string, Transform> model )
	{
		if ( clip is null )
			return;

		foreach ( var constraint in clip.Constraints )
		{
			if ( constraint.SourceControl.StartsWith( "@", StringComparison.Ordinal ) )
				continue;
			if ( !model.TryGetValue( constraint.SourceControl, out var source )
				|| !model.TryGetValue( constraint.TargetBone, out var target ) )
				continue;

			model[constraint.SourceControl] = ClipConstraintEvaluator.Apply(
				source,
				target,
				constraint,
				time,
				Transform.Zero );
		}
	}
}
