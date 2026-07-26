#nullable enable annotations

using System;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

public static class WeaponPoseProjection
{
	public static bool TryGetSourceWorldOverride(
		WeaponAnimationDocument document,
		EvaluatedPose pose,
		WeaponBoneDefinition definition,
		out Transform transform )
	{
		var hostName = definition.Id.Equals(
			document.Rig.SourceSkeletonRootId,
			StringComparison.OrdinalIgnoreCase )
				? "weapon_root"
				: definition.Name;
		return pose.Model.TryGetValue( hostName, out transform );
	}

	public static Transform SolveRendererTransform(
		Transform sourceRootBind,
		Transform desiredRootWorld )
	{
		var scale = new Vector3(
			SafeDivide( desiredRootWorld.Scale.x, sourceRootBind.Scale.x ),
			SafeDivide( desiredRootWorld.Scale.y, sourceRootBind.Scale.y ),
			SafeDivide( desiredRootWorld.Scale.z, sourceRootBind.Scale.z ) );
		var rotation = (desiredRootWorld.Rotation * sourceRootBind.Rotation.Inverse).Normal;
		var position = desiredRootWorld.Position
			- rotation * (sourceRootBind.Position * scale);
		return new Transform( position, rotation, scale );
	}

	public static bool TransformNear(
		Transform left,
		Transform right,
		float tolerance = 0.0001f )
	{
		return left.Position.Distance( right.Position ) <= tolerance
			&& (left.Scale - right.Scale).Length <= tolerance
			&& (left.Rotation.Forward - right.Rotation.Forward).Length <= tolerance
			&& (left.Rotation.Up - right.Rotation.Up).Length <= tolerance;
	}

	private static float SafeDivide( float numerator, float denominator ) =>
		MathF.Abs( denominator ) <= 0.000001f
			? numerator
			: numerator / denominator;
}
