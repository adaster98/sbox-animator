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
	bool Reachable,
	float RequestedDistance,
	float SolvedDistance );

public static class WeaponAnimationMath
{
	public const float CentimetresPerInch = 2.54f;
	public const int MotionRateIntegrationSteps = 64;
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

		var keys = track.Keys;
		if ( time <= keys[0].Time )
			return KeyTransform( keys[0] );
		if ( time >= keys[^1].Time )
			return KeyTransform( keys[^1] );

		var low = 0;
		var high = keys.Count - 1;
		while ( low < high )
		{
			var middle = low + (high - low) / 2;
			if ( keys[middle].Time < time )
				low = middle + 1;
			else
				high = middle;
		}

		if ( MathF.Abs( keys[low].Time - time ) <= Epsilon )
			return KeyTransform( keys[low] );
		return SampleSpan( track, keys[low - 1], keys[low], time );
	}

	private static Transform SampleSpan(
		TransformTrack track,
		TransformKey current,
		TransformKey next,
		float time )
	{
		var duration = MathF.Max( next.Time - current.Time, Epsilon );
		var fraction = Math.Clamp( (time - current.Time) / duration, 0.0f, 1.0f );
		var span = track.FindCurveSpan( current.Id, next.Id );
		var interpolation = span?.HasInterpolationOverride == true
			? span.Interpolation
			: track.Interpolation;
		var hasSpeedCurve = span?.HasSpeedCurve == true;
		if ( interpolation == TrackInterpolation.Stepped && !hasSpeedCurve )
			return KeyTransform( current );

		var progress = hasSpeedCurve
			? SampleMotionProgress( span!.Speed, fraction )
			: fraction;
		var valueInterpolation = hasSpeedCurve
			? TrackInterpolation.Linear
			: interpolation;
		if ( span is null || span.CustomChannels == TransformCurveChannel.None )
		{
			if ( valueInterpolation == TrackInterpolation.Cubic )
				progress = SmoothStep( progress );

			return new Transform(
				Vector3.Lerp( current.Position, next.Position, progress ),
				Rotation.Slerp( current.Rotation, next.Rotation, progress ),
				Vector3.Lerp( current.Scale, next.Scale, progress ) );
		}

		return new Transform(
			SampleVectorChannels(
				current.Position,
				next.Position,
				current.CurveTangents.PositionOut,
				next.CurveTangents.PositionIn,
				span.CustomChannels,
				TransformCurveChannel.PositionX,
				progress,
				duration,
				valueInterpolation ),
			SampleRotationChannels(
				current,
				next,
				span,
				progress,
				duration,
				valueInterpolation ),
			SampleVectorChannels(
				current.Scale,
				next.Scale,
				current.CurveTangents.ScaleOut,
				next.CurveTangents.ScaleIn,
				span.CustomChannels,
				TransformCurveChannel.ScaleX,
				progress,
				duration,
				valueInterpolation ) );
	}

	public static float SampleMotionRate( MotionRateCurve curve, float fraction )
	{
		fraction = Math.Clamp( fraction, 0.0f, 1.0f );
		var rate = Hermite(
			curve.StartRate,
			curve.EndRate,
			curve.StartSlope,
			curve.EndSlope,
			fraction );
		return IsFinite( rate ) ? MathF.Max( rate, 0 ) : 0;
	}

	public static float SampleMotionProgress( MotionRateCurve curve, float fraction )
	{
		fraction = Math.Clamp( fraction, 0.0f, 1.0f );
		if ( fraction <= 0 )
			return 0;
		if ( fraction >= 1 )
			return 1;

		var total = IntegrateMotionRate( curve, 1.0f );
		if ( total <= Epsilon || !IsFinite( total ) )
			return fraction;

		return Math.Clamp( IntegrateMotionRate( curve, fraction ) / total, 0.0f, 1.0f );
	}

	public static float MotionRateArea( MotionRateCurve curve ) =>
		IntegrateMotionRate( curve, 1.0f );

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

	public static void RepairCurveSpans( TransformTrack track )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		var adjacent = ordered
			.Zip( ordered.Skip( 1 ), ( start, end ) => (start.Id, end.Id) )
			.ToHashSet();
		track.CurveSpans.RemoveAll( span =>
			span.StartKeyId == Guid.Empty
			|| span.EndKeyId == Guid.Empty
			|| !adjacent.Contains( (span.StartKeyId, span.EndKeyId) ) );

		foreach ( var duplicate in track.CurveSpans
			.GroupBy( x => (x.StartKeyId, x.EndKeyId) )
			.SelectMany( x => x.Skip( 1 ) )
			.ToArray() )
		{
			track.CurveSpans.Remove( duplicate );
		}
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
		return new TwoBoneSolution(
			root,
			elbow,
			solvedEnd,
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

	private static float IntegrateMotionRate( MotionRateCurve curve, float end )
	{
		end = Math.Clamp( end, 0.0f, 1.0f );
		if ( end <= 0 )
			return 0;

		var step = 1.0f / MotionRateIntegrationSteps;
		var wholeSteps = Math.Clamp(
			(int)MathF.Floor( end * MotionRateIntegrationSteps ),
			0,
			MotionRateIntegrationSteps );
		var area = 0.0f;
		for ( var index = 0; index < wholeSteps; index++ )
		{
			var start = index * step;
			var finish = (index + 1) * step;
			area += (SampleMotionRate( curve, start ) + SampleMotionRate( curve, finish ))
				* 0.5f * step;
		}

		var remainderStart = wholeSteps * step;
		if ( remainderStart < end )
		{
			area += (SampleMotionRate( curve, remainderStart ) + SampleMotionRate( curve, end ))
				* 0.5f * (end - remainderStart);
		}
		return area;
	}

	private static Vector3 SampleVectorChannels(
		Vector3 start,
		Vector3 end,
		Vector3 startTangents,
		Vector3 endTangents,
		TransformCurveChannel customChannels,
		TransformCurveChannel firstChannel,
		float progress,
		float duration,
		TrackInterpolation interpolation )
	{
		var legacy = interpolation == TrackInterpolation.Cubic
			? SmoothStep( progress )
			: progress;
		return new Vector3(
			SampleScalarChannel(
				start.x, end.x, startTangents.x, endTangents.x,
				(customChannels & firstChannel) != 0, progress, legacy, duration ),
			SampleScalarChannel(
				start.y, end.y, startTangents.y, endTangents.y,
				(customChannels & (TransformCurveChannel)((int)firstChannel << 1)) != 0,
				progress, legacy, duration ),
			SampleScalarChannel(
				start.z, end.z, startTangents.z, endTangents.z,
				(customChannels & (TransformCurveChannel)((int)firstChannel << 2)) != 0,
				progress, legacy, duration ) );
	}

	private static float SampleScalarChannel(
		float start,
		float end,
		float startTangent,
		float endTangent,
		bool custom,
		float progress,
		float legacyProgress,
		float duration ) =>
		custom
			? Hermite( start, end, startTangent * duration, endTangent * duration, progress )
			: start.LerpTo( end, legacyProgress );

	private static Rotation SampleRotationChannels(
		TransformKey current,
		TransformKey next,
		TransformCurveSpan span,
		float progress,
		float duration,
		TrackInterpolation interpolation )
	{
		var custom = span.CustomChannels & TransformCurveChannel.Rotation;
		var legacy = interpolation == TrackInterpolation.Cubic
			? SmoothStep( progress )
			: progress;
		if ( custom == TransformCurveChannel.None )
			return Rotation.Slerp( current.Rotation, next.Rotation, legacy );

		var startAngles = current.Rotation.Angles();
		var endAngles = next.Rotation.Angles();
		var start = new Vector3( startAngles.pitch, startAngles.yaw, startAngles.roll );
		var end = new Vector3(
			UnwrapDegrees( start.x, endAngles.pitch ),
			UnwrapDegrees( start.y, endAngles.yaw ),
			UnwrapDegrees( start.z, endAngles.roll ) );
		var legacyRotation = Rotation.Slerp( current.Rotation, next.Rotation, legacy );
		var legacyAngles = legacyRotation.Angles();
		var legacyValues = new Vector3(
			UnwrapDegrees( start.x, legacyAngles.pitch ),
			UnwrapDegrees( start.y, legacyAngles.yaw ),
			UnwrapDegrees( start.z, legacyAngles.roll ) );
		var sampled = new Vector3(
			(custom & TransformCurveChannel.RotationX) != 0
				? Hermite(
					start.x,
					end.x,
					current.CurveTangents.RotationOut.x * duration,
					next.CurveTangents.RotationIn.x * duration,
					progress )
				: legacyValues.x,
			(custom & TransformCurveChannel.RotationY) != 0
				? Hermite(
					start.y,
					end.y,
					current.CurveTangents.RotationOut.y * duration,
					next.CurveTangents.RotationIn.y * duration,
					progress )
				: legacyValues.y,
			(custom & TransformCurveChannel.RotationZ) != 0
				? Hermite(
					start.z,
					end.z,
					current.CurveTangents.RotationOut.z * duration,
					next.CurveTangents.RotationIn.z * duration,
					progress )
				: legacyValues.z );
		return Rotation.From( new Angles( sampled.x, sampled.y, sampled.z ) ).Normal;
	}

	private static float Hermite(
		float start,
		float end,
		float startTangent,
		float endTangent,
		float amount )
	{
		var amount2 = amount * amount;
		var amount3 = amount2 * amount;
		return (2 * amount3 - 3 * amount2 + 1) * start
			+ (amount3 - 2 * amount2 + amount) * startTangent
			+ (-2 * amount3 + 3 * amount2) * end
			+ (amount3 - amount2) * endTangent;
	}

	private static float SmoothStep( float amount ) =>
		amount * amount * (3.0f - 2.0f * amount);

	private static float UnwrapDegrees( float reference, float value )
	{
		var difference = (value - reference) % 360.0f;
		if ( difference > 180 )
			difference -= 360;
		else if ( difference < -180 )
			difference += 360;
		return reference + difference;
	}
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
