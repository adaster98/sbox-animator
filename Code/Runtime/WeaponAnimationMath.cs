#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator;

public readonly record struct ScalePreview(
	float MeasuredUnits,
	float KnownInches,
	float UniformScale,
	Vector3 OriginalDimensions,
	Vector3 ResultingDimensions );

public readonly record struct AlignmentResult(
	Transform PhysicalTransform,
	bool BoreMayBeReversed,
	Vector3 BoreDirection );

public readonly record struct TwoBoneSolution(
	Vector3 Root,
	Vector3 Elbow,
	Vector3 End,
	Rotation UpperRotation,
	Rotation LowerRotation,
	bool Reachable,
	float RequestedDistance,
	float SolvedDistance );

public static class WeaponAnimationMath
{
	public const float CentimetresPerInch = 2.54f;
	private const float Epsilon = 0.0001f;

	public static bool IsFinite( float value ) =>
		!float.IsNaN( value ) && !float.IsInfinity( value );

	public static bool IsFinite( Vector3 value ) =>
		IsFinite( value.x ) && IsFinite( value.y ) && IsFinite( value.z );

	public static bool TryCalculateUniformScale(
		Vector3 firstPoint,
		Vector3 secondPoint,
		float knownDistance,
		MeasurementUnit unit,
		Vector3 originalDimensions,
		out ScalePreview preview )
	{
		preview = default;
		var measuredUnits = firstPoint.Distance( secondPoint );
		var knownInches = unit == MeasurementUnit.Centimetres
			? knownDistance / CentimetresPerInch
			: knownDistance;

		if ( measuredUnits <= Epsilon || knownInches <= Epsilon )
			return false;

		var scale = knownInches / measuredUnits;
		if ( !IsFinite( scale ) || scale <= Epsilon )
			return false;

		preview = new ScalePreview(
			measuredUnits,
			knownInches,
			scale,
			originalDimensions,
			originalDimensions * scale );

		return true;
	}

	public static bool TryCalculateAlignment(
		Vector3 grip,
		Vector3 rearBore,
		Vector3 frontBore,
		WeaponUpAxis upAxis,
		float uniformScale,
		Vector3 canonicalGrip,
		out AlignmentResult result )
	{
		result = default;

		if ( !IsFinite( uniformScale ) || uniformScale <= Epsilon )
			return false;

		var scaledGrip = grip * uniformScale;
		var bore = (frontBore - rearBore) * uniformScale;
		if ( bore.Length <= Epsilon )
			return false;

		var forward = bore.Normal;
		var chosenUp = AxisVector( upAxis );
		var projectedUp = (chosenUp - forward * Vector3.Dot( chosenUp, forward )).Normal;
		if ( projectedUp.Length <= Epsilon )
			projectedUp = MathF.Abs( Vector3.Dot( forward, Vector3.Up ) ) < 0.95f
				? Vector3.Up
				: Vector3.Left;

		var sourceBasis = Rotation.LookAt( forward, projectedUp );
		var rotation = sourceBasis.Inverse;
		var rotatedGrip = rotation * scaledGrip;
		var position = canonicalGrip - rotatedGrip;
		var physical = new Transform( position, rotation, uniformScale );
		var reversed = Vector3.Dot( forward, Vector3.Forward ) < -0.25f;

		result = new AlignmentResult( physical, reversed, forward );
		return true;
	}

	public static Transform SampleTrack( TransformTrack track, float time, Transform fallback )
	{
		if ( track.Keys.Count == 0 || track.Muted )
			return fallback;

		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		if ( time <= ordered[0].Time )
			return KeyTransform( ordered[0] );
		if ( time >= ordered[^1].Time )
			return KeyTransform( ordered[^1] );

		for ( var i = 0; i < ordered.Length - 1; i++ )
		{
			var current = ordered[i];
			var next = ordered[i + 1];
			if ( time < current.Time || time > next.Time )
				continue;

			var duration = MathF.Max( next.Time - current.Time, Epsilon );
			var fraction = Math.Clamp( (time - current.Time) / duration, 0.0f, 1.0f );
			if ( track.Interpolation == TrackInterpolation.Stepped )
				return KeyTransform( current );

			if ( track.Interpolation == TrackInterpolation.Cubic )
				fraction = fraction * fraction * (3.0f - 2.0f * fraction);

			return new Transform(
				Vector3.Lerp( current.Position, next.Position, fraction ),
				Rotation.Slerp( current.Rotation, next.Rotation, fraction ),
				Vector3.Lerp( current.Scale, next.Scale, fraction ) );
		}

		return fallback;
	}

	public static float SnapTime( float time, float sampleRate, bool allowSubframes )
	{
		if ( allowSubframes || sampleRate <= Epsilon )
			return MathF.Max( time, 0 );

		return MathF.Max( MathF.Round( time * sampleRate ) / sampleRate, 0 );
	}

	public static TransformKey UpsertKey( TransformTrack track, float time, Transform value, float tolerance = 0.0001f )
	{
		var existing = track.Keys.FirstOrDefault( x => MathF.Abs( x.Time - time ) <= tolerance );
		if ( existing is null )
		{
			existing = new TransformKey { Time = time };
			track.Keys.Add( existing );
		}

		existing.Position = value.Position;
		existing.Rotation = value.Rotation.Normal;
		existing.Scale = value.Scale;
		track.Keys.Sort( ( a, b ) => a.Time.CompareTo( b.Time ) );
		return existing;
	}

	public static TwoBoneSolution SolveTwoBone(
		Vector3 root,
		Vector3 currentElbow,
		Vector3 currentEnd,
		Vector3 requestedTarget,
		Vector3 pole )
	{
		var upperLength = root.Distance( currentElbow );
		var lowerLength = currentElbow.Distance( currentEnd );
		var targetVector = requestedTarget - root;
		var requestedDistance = targetVector.Length;
		var direction = requestedDistance > Epsilon ? targetVector.Normal : Vector3.Forward;
		var minimum = MathF.Abs( upperLength - lowerLength ) + Epsilon;
		var maximum = MathF.Max( upperLength + lowerLength - Epsilon, minimum );
		var solvedDistance = Math.Clamp( requestedDistance, minimum, maximum );
		var reachable = requestedDistance >= minimum && requestedDistance <= maximum + Epsilon;
		var solvedEnd = root + direction * solvedDistance;

		var poleVector = pole - root;
		var poleDirection = poleVector - direction * Vector3.Dot( poleVector, direction );
		if ( poleDirection.Length <= Epsilon )
		{
			var fallback = MathF.Abs( Vector3.Dot( direction, Vector3.Up ) ) < 0.95f
				? Vector3.Up
				: Vector3.Left;
			poleDirection = fallback - direction * Vector3.Dot( fallback, direction );
		}

		poleDirection = poleDirection.Normal;
		var along = (
			upperLength * upperLength
			- lowerLength * lowerLength
			+ solvedDistance * solvedDistance ) / (2.0f * solvedDistance);
		var heightSquared = MathF.Max( upperLength * upperLength - along * along, 0 );
		var elbow = root + direction * along + poleDirection * MathF.Sqrt( heightSquared );
		var upperRotation = Rotation.LookAt( (elbow - root).Normal, poleDirection );
		var lowerRotation = Rotation.LookAt( (solvedEnd - elbow).Normal, poleDirection );

		return new TwoBoneSolution(
			root,
			elbow,
			solvedEnd,
			upperRotation,
			lowerRotation,
			reachable,
			requestedDistance,
			solvedDistance );
	}

	public static Rotation RotationFromTo( Vector3 from, Vector3 to )
	{
		if ( from.Length <= Epsilon || to.Length <= Epsilon )
			return Rotation.Identity;

		from = from.Normal;
		to = to.Normal;
		var dot = Math.Clamp( Vector3.Dot( from, to ), -1.0f, 1.0f );
		var axis = Vector3.Cross( from, to );
		if ( axis.Length <= Epsilon )
		{
			if ( dot >= 0 )
				return Rotation.Identity;

			var orthogonal = Vector3.Cross( from, Vector3.Up );
			if ( orthogonal.Length <= Epsilon )
				orthogonal = Vector3.Cross( from, Vector3.Right );
			return Rotation.FromAxis( orthogonal.Normal, 180.0f );
		}

		return Rotation.FromAxis(
			axis.Normal,
			MathF.Acos( dot ).RadianToDegree() );
	}

	public static Transform Compose( Transform physical, Transform framing )
	{
		var position = physical.PointToWorld( framing.Position );
		var rotation = physical.Rotation * framing.Rotation;
		var scale = physical.Scale * framing.Scale;
		return new Transform( position, rotation, scale );
	}

	public static float ToCentimetres( float sboxUnits ) => sboxUnits * CentimetresPerInch;

	public static Vector3 AxisVector( WeaponUpAxis axis ) => axis switch
	{
		WeaponUpAxis.NegativeZ => Vector3.Down,
		WeaponUpAxis.PositiveY => Vector3.Left,
		WeaponUpAxis.NegativeY => Vector3.Right,
		_ => Vector3.Up
	};

	private static Transform KeyTransform( TransformKey key ) =>
		new( key.Position, key.Rotation.Normal, key.Scale );
}

public static class ClipConstraintEvaluator
{
	public static Transform Apply(
		Transform source,
		Transform target,
		TimedConstraint constraint,
		float time,
		Transform maintainedOffset )
	{
		if ( time < constraint.StartTime || time > constraint.EndTime || constraint.Weight <= 0 )
			return source;

		var desired = constraint.MaintainOffset
			? new Transform(
				target.PointToWorld( maintainedOffset.Position ),
				target.Rotation * maintainedOffset.Rotation,
				target.Scale * maintainedOffset.Scale )
			: target;

		var weight = Math.Clamp( constraint.Weight, 0.0f, 1.0f );
		return new Transform(
			Vector3.Lerp( source.Position, desired.Position, weight ),
			Rotation.Slerp( source.Rotation, desired.Rotation, weight ),
			Vector3.Lerp( source.Scale, desired.Scale, weight ) );
	}
}
