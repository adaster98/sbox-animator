#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace SboxWeaponAnimator.Editor;

internal enum CurvePreset
{
	Linear,
	EaseIn,
	EaseOut,
	EaseInOut
}

internal readonly record struct CurveChannelSample( TransformKey Key, float Value );

internal static class CurveEditingService
{
	public static IReadOnlyList<TransformTrack> KeyedTracks(
		WeaponAnimationClip clip,
		string search = "" )
	{
		var tracks = clip.Tracks
			.Where( x => x.Keys.Count > 0 );
		if ( !string.IsNullOrWhiteSpace( search ) )
		{
			tracks = tracks.Where( x =>
				x.Target.Contains( search.Trim(), StringComparison.OrdinalIgnoreCase ) );
		}
		return tracks
			.OrderBy( x => x.Kind )
			.ThenBy( x => x.Target, StringComparer.OrdinalIgnoreCase )
			.ToArray();
	}

	public static TransformTrack? ResolveSelectedTrack(
		WeaponAnimationDocument document,
		WeaponAnimationClip clip )
	{
		var view = document.Workspace.EnsureCurveView( clip.Id );
		var keyed = clip.Tracks.Where( x => x.Keys.Count > 0 ).ToArray();
		var selected = keyed.FirstOrDefault( x => x.Id == view.SelectedTrackId )
			?? keyed.FirstOrDefault( x =>
				x.Target.Equals(
					document.Workspace.SelectedControl,
					StringComparison.OrdinalIgnoreCase ) )
			?? keyed.FirstOrDefault();
		if ( selected is null )
			return null;

		view.SelectedTrackId = selected.Id;
		if ( view.VisibleChannels == TransformCurveChannel.None )
			view.VisibleChannels = DefaultVisibleChannels( selected );
		return selected;
	}

	public static TransformCurveChannel DefaultVisibleChannels( TransformTrack track )
	{
		var result = TransformCurveChannel.None;
		foreach ( var channel in Channels )
		{
			var values = ChannelSamples( track, channel ).Select( x => x.Value ).ToArray();
			if ( values.Length > 1 && values.Max() - values.Min() > 0.0001f )
				result |= channel;
		}

		if ( result != TransformCurveChannel.None )
			return result;
		return track.Kind == RigControlKind.Weapon
			? TransformCurveChannel.PositionX
			: TransformCurveChannel.RotationX;
	}

	public static IReadOnlyList<CurveChannelSample> ChannelSamples(
		TransformTrack track,
		TransformCurveChannel channel )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		var samples = new List<CurveChannelSample>( ordered.Length );
		float? previousRotation = null;
		foreach ( var key in ordered )
		{
			var value = RawChannelValue( key, channel );
			if ( IsRotation( channel ) && previousRotation is not null )
				value = UnwrapDegrees( previousRotation.Value, value );
			samples.Add( new CurveChannelSample( key, value ) );
			previousRotation = value;
		}
		return samples;
	}

	public static float GetTangent(
		TransformKey key,
		TransformCurveChannel channel,
		bool incoming )
	{
		var tangents = key.CurveTangents;
		var vector = channel switch
		{
			TransformCurveChannel.PositionX
				or TransformCurveChannel.PositionY
				or TransformCurveChannel.PositionZ => incoming
					? tangents.PositionIn
					: tangents.PositionOut,
			TransformCurveChannel.RotationX
				or TransformCurveChannel.RotationY
				or TransformCurveChannel.RotationZ => incoming
					? tangents.RotationIn
					: tangents.RotationOut,
			_ => incoming ? tangents.ScaleIn : tangents.ScaleOut
		};
		return Axis( vector, channel );
	}

	public static void SetTangent(
		TransformKey key,
		TransformCurveChannel channel,
		bool incoming,
		float value,
		bool breakHandles )
	{
		var tangents = key.CurveTangents;
		if ( breakHandles )
			tangents.FreeHandles |= channel;
		else
			tangents.FreeHandles &= ~channel;

		SetFacingTangent( tangents, channel, incoming, value );
		if ( !breakHandles )
			SetFacingTangent( tangents, channel, !incoming, value );
	}

	public static void SetKeyValue(
		TransformKey key,
		TransformCurveChannel channel,
		float value )
	{
		switch ( channel )
		{
			case TransformCurveChannel.PositionX:
				key.Position = key.Position.WithX( value );
				break;
			case TransformCurveChannel.PositionY:
				key.Position = key.Position.WithY( value );
				break;
			case TransformCurveChannel.PositionZ:
				key.Position = key.Position.WithZ( value );
				break;
			case TransformCurveChannel.ScaleX:
				key.Scale = key.Scale.WithX( MathF.Max( value, 0.0001f ) );
				break;
			case TransformCurveChannel.ScaleY:
				key.Scale = key.Scale.WithY( MathF.Max( value, 0.0001f ) );
				break;
			case TransformCurveChannel.ScaleZ:
				key.Scale = key.Scale.WithZ( MathF.Max( value, 0.0001f ) );
				break;
			default:
				var angles = key.Rotation.Angles();
				var adjusted = channel switch
				{
					TransformCurveChannel.RotationX => new Angles( value, angles.yaw, angles.roll ),
					TransformCurveChannel.RotationY => new Angles( angles.pitch, value, angles.roll ),
					_ => new Angles( angles.pitch, angles.yaw, value )
				};
				key.Rotation = Rotation.From( adjusted ).Normal;
				break;
		}
	}

	public static void ApplyPreset(
		TransformTrack track,
		IReadOnlyCollection<Guid> selectedKeys,
		CurveEditorMode mode,
		TransformCurveChannel channels,
		CurvePreset preset )
	{
		var ordered = track.Keys.OrderBy( x => x.Time ).ToArray();
		if ( ordered.Length < 2 )
			return;
		var selectedOnTrack = ordered
			.Where( x => selectedKeys.Contains( x.Id ) )
			.Select( x => x.Id )
			.ToHashSet();

		var selectedSpans = ordered
			.Zip( ordered.Skip( 1 ), ( start, end ) => (start, end) )
			.Where( pair =>
				selectedOnTrack.Count == 0
					|| selectedOnTrack.Contains( pair.start.Id )
					|| selectedOnTrack.Contains( pair.end.Id ) )
			.ToArray();
		foreach ( var (start, end) in selectedSpans )
		{
			var span = track.EnsureCurveSpan( start.Id, end.Id );
			if ( mode == CurveEditorMode.Speed )
			{
				ApplySpeedPreset( span, preset );
				if ( span.CustomChannels == TransformCurveChannel.None
					&& !span.HasInterpolationOverride )
				{
					span.HasInterpolationOverride = true;
					span.Interpolation = TrackInterpolation.Linear;
				}
				continue;
			}

			var duration = MathF.Max( end.Time - start.Time, 0.0001f );
			foreach ( var channel in Channels.Where( x => (channels & x) != 0 ) )
			{
				var startValue = ChannelSamples( track, channel )
					.First( x => x.Key.Id == start.Id ).Value;
				var endValue = ChannelSamples( track, channel )
					.First( x => x.Key.Id == end.Id ).Value;
				var secant = (endValue - startValue) / duration;
				var (outgoing, incoming) = preset switch
				{
					CurvePreset.EaseIn => (0.0f, secant * 2.0f),
					CurvePreset.EaseOut => (secant * 2.0f, 0.0f),
					CurvePreset.EaseInOut => (0.0f, 0.0f),
					_ => (secant, secant)
				};
				SetTangent( start, channel, false, outgoing, false );
				SetTangent( end, channel, true, incoming, false );
				span.CustomChannels |= channel;
			}
		}
	}

	public static void AlignHandles(
		TransformKey key,
		TransformCurveChannel channels )
	{
		foreach ( var channel in Channels.Where( x => (channels & x) != 0 ) )
		{
			var incoming = GetTangent( key, channel, true );
			var outgoing = GetTangent( key, channel, false );
			var aligned = (incoming + outgoing) * 0.5f;
			SetFacingTangent( key.CurveTangents, channel, true, aligned );
			SetFacingTangent( key.CurveTangents, channel, false, aligned );
			key.CurveTangents.FreeHandles &= ~channel;
		}
	}

	public static void FreeHandles(
		TransformKey key,
		TransformCurveChannel channels )
	{
		key.CurveTangents.FreeHandles |= channels & TransformCurveChannel.All;
	}

	public static void RepairTrack( TransformTrack track )
	{
		foreach ( var key in track.Keys )
			key.CurveTangents ??= new TransformCurveTangents();
		WeaponAnimationMath.RepairCurveSpans( track );
	}

	public static IReadOnlyList<Guid> CaptureOrder( TransformTrack track ) =>
		track.Keys.OrderBy( x => x.Time ).Select( x => x.Id ).ToArray();

	public static void RepairTopology(
		TransformTrack track,
		IReadOnlyList<Guid> previousOrder )
	{
		RepairTrack( track );
		var previousAdjacent = previousOrder
			.Zip( previousOrder.Skip( 1 ), ( start, end ) => (start, end) )
			.ToHashSet();
		var current = track.Keys.OrderBy( x => x.Time ).ToArray();
		foreach ( var (start, end) in current.Zip(
			current.Skip( 1 ),
			( start, end ) => (start, end) ) )
		{
			if ( previousAdjacent.Contains( (start.Id, end.Id) )
				|| track.FindCurveSpan( start.Id, end.Id ) is not null )
				continue;

			// A removed endpoint exposes a deterministic linear bridge.
			var span = track.EnsureCurveSpan( start.Id, end.Id );
			span.HasInterpolationOverride = true;
			span.Interpolation = TrackInterpolation.Linear;
		}
	}

	public static void RemoveKeysAndRepair(
		TransformTrack track,
		Predicate<TransformKey> predicate )
	{
		var previous = CaptureOrder( track );
		track.Keys.RemoveAll( predicate );
		RepairTopology( track, previous );
	}

	public static readonly TransformCurveChannel[] Channels =
	[
		TransformCurveChannel.PositionX,
		TransformCurveChannel.PositionY,
		TransformCurveChannel.PositionZ,
		TransformCurveChannel.RotationX,
		TransformCurveChannel.RotationY,
		TransformCurveChannel.RotationZ,
		TransformCurveChannel.ScaleX,
		TransformCurveChannel.ScaleY,
		TransformCurveChannel.ScaleZ
	];

	private static void ApplySpeedPreset( TransformCurveSpan span, CurvePreset preset )
	{
		span.HasSpeedCurve = true;
		span.Speed = preset switch
		{
			CurvePreset.EaseIn => new MotionRateCurve
			{
				StartRate = 0,
				EndRate = 2,
				StartSlope = 2,
				EndSlope = 2
			},
			CurvePreset.EaseOut => new MotionRateCurve
			{
				StartRate = 2,
				EndRate = 0,
				StartSlope = -2,
				EndSlope = -2
			},
			CurvePreset.EaseInOut => new MotionRateCurve
			{
				StartRate = 0,
				EndRate = 0,
				StartSlope = 6,
				EndSlope = -6
			},
			_ => new MotionRateCurve()
		};
	}

	private static void SetFacingTangent(
		TransformCurveTangents tangents,
		TransformCurveChannel channel,
		bool incoming,
		float value )
	{
		switch ( channel )
		{
			case TransformCurveChannel.PositionX:
			case TransformCurveChannel.PositionY:
			case TransformCurveChannel.PositionZ:
				if ( incoming )
					tangents.PositionIn = SetAxis( tangents.PositionIn, channel, value );
				else
					tangents.PositionOut = SetAxis( tangents.PositionOut, channel, value );
				break;
			case TransformCurveChannel.RotationX:
			case TransformCurveChannel.RotationY:
			case TransformCurveChannel.RotationZ:
				if ( incoming )
					tangents.RotationIn = SetAxis( tangents.RotationIn, channel, value );
				else
					tangents.RotationOut = SetAxis( tangents.RotationOut, channel, value );
				break;
			default:
				if ( incoming )
					tangents.ScaleIn = SetAxis( tangents.ScaleIn, channel, value );
				else
					tangents.ScaleOut = SetAxis( tangents.ScaleOut, channel, value );
				break;
		}
	}

	private static float RawChannelValue( TransformKey key, TransformCurveChannel channel )
	{
		var angles = key.Rotation.Angles();
		return channel switch
		{
			TransformCurveChannel.PositionX => key.Position.x,
			TransformCurveChannel.PositionY => key.Position.y,
			TransformCurveChannel.PositionZ => key.Position.z,
			TransformCurveChannel.RotationX => angles.pitch,
			TransformCurveChannel.RotationY => angles.yaw,
			TransformCurveChannel.RotationZ => angles.roll,
			TransformCurveChannel.ScaleX => key.Scale.x,
			TransformCurveChannel.ScaleY => key.Scale.y,
			_ => key.Scale.z
		};
	}

	private static float Axis( Vector3 vector, TransformCurveChannel channel ) =>
		AxisIndex( channel ) switch
		{
			0 => vector.x,
			1 => vector.y,
			_ => vector.z
		};

	private static Vector3 SetAxis(
		Vector3 vector,
		TransformCurveChannel channel,
		float value ) =>
		AxisIndex( channel ) switch
		{
			0 => vector.WithX( value ),
			1 => vector.WithY( value ),
			_ => vector.WithZ( value )
		};

	private static int AxisIndex( TransformCurveChannel channel ) => channel switch
	{
		TransformCurveChannel.PositionX
			or TransformCurveChannel.RotationX
			or TransformCurveChannel.ScaleX => 0,
		TransformCurveChannel.PositionY
			or TransformCurveChannel.RotationY
			or TransformCurveChannel.ScaleY => 1,
		_ => 2
	};

	private static bool IsRotation( TransformCurveChannel channel ) =>
		(channel & TransformCurveChannel.Rotation) != 0;

	internal static float UnwrapDegrees( float reference, float value )
	{
		var difference = (value - reference) % 360.0f;
		if ( difference > 180 )
			difference -= 360;
		else if ( difference < -180 )
			difference += 360;
		return reference + difference;
	}
}
