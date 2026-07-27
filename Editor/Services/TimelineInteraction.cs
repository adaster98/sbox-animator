#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;

namespace SboxWeaponAnimator.Editor;

internal readonly record struct TimelineFrameRange( int StartFrame, int EndFrame )
{
	public int Span => Math.Max( EndFrame - StartFrame, 0 );
}

internal readonly record struct TimelineTickSpacing( int MinorFrames, int MajorFrames );

internal static class TimelineInteraction
{
	public const int MinimumVisibleFrameIntervals = 2;

	public static int LastFrame( WeaponAnimationClip clip ) =>
		Math.Max( 1, (int)MathF.Floor(
			MathF.Max( clip.Duration, 0 ) * MathF.Max( clip.SampleRate, 1 ) + 0.0001f ) );

	public static int TimeToFrame( float time, float sampleRate ) =>
		Math.Max( 0, (int)MathF.Round( MathF.Max( time, 0 ) * MathF.Max( sampleRate, 1 ) ) );

	public static float FrameToTime( int frame, float sampleRate ) =>
		Math.Max( frame, 0 ) / MathF.Max( sampleRate, 1 );

	public static float SnapTime( WeaponAnimationClip clip, float time ) =>
		FrameToTime(
			Math.Clamp( TimeToFrame( time, clip.SampleRate ), 0, LastFrame( clip ) ),
			clip.SampleRate );

	public static TimelineFrameRange ResolveRange(
		WeaponAnimationClip clip,
		TimelineViewState? state )
	{
		var last = LastFrame( clip );
		if ( state is null || state.VisibleEnd <= state.VisibleStart )
			return new TimelineFrameRange( 0, last );

		var start = Math.Clamp( TimeToFrame( state.VisibleStart, clip.SampleRate ), 0, last );
		var end = Math.Clamp( TimeToFrame( state.VisibleEnd, clip.SampleRate ), 0, last );
		return NormalizeRange( new TimelineFrameRange( start, end ), last );
	}

	public static TimelineFrameRange Zoom(
		TimelineFrameRange range,
		int lastFrame,
		bool zoomIn )
	{
		range = NormalizeRange( range, lastFrame );
		var minimum = Math.Min( MinimumVisibleFrameIntervals, lastFrame );
		var requested = zoomIn
			? Math.Max( minimum, (int)MathF.Floor( range.Span * 0.8f ) )
			: Math.Min( lastFrame, (int)MathF.Ceiling( range.Span * 1.25f ) );
		if ( requested == range.Span )
			requested = Math.Clamp(
				range.Span + (zoomIn ? -1 : 1),
				minimum,
				lastFrame );

		// Preserve the current midpoint while the range changes size.
		var center = (range.StartFrame + range.EndFrame) * 0.5f;
		var start = (int)MathF.Round( center - requested * 0.5f );
		return NormalizeRange(
			new TimelineFrameRange( start, start + requested ),
			lastFrame,
			requested );
	}

	public static TimelineFrameRange Pan(
		TimelineFrameRange range,
		int deltaFrames,
		int lastFrame )
	{
		range = NormalizeRange( range, lastFrame );
		var start = Math.Clamp(
			range.StartFrame + deltaFrames,
			0,
			Math.Max( lastFrame - range.Span, 0 ) );
		return new TimelineFrameRange( start, start + range.Span );
	}

	public static TimelineFrameRange ResizeStart(
		TimelineFrameRange range,
		int startFrame,
		int lastFrame )
	{
		range = NormalizeRange( range, lastFrame );
		var minimum = Math.Min( MinimumVisibleFrameIntervals, lastFrame );
		return new TimelineFrameRange(
			Math.Clamp( startFrame, 0, Math.Max( range.EndFrame - minimum, 0 ) ),
			range.EndFrame );
	}

	public static TimelineFrameRange ResizeEnd(
		TimelineFrameRange range,
		int endFrame,
		int lastFrame )
	{
		range = NormalizeRange( range, lastFrame );
		var minimum = Math.Min( MinimumVisibleFrameIntervals, lastFrame );
		return new TimelineFrameRange(
			range.StartFrame,
			Math.Clamp( endFrame, Math.Min( range.StartFrame + minimum, lastFrame ), lastFrame ) );
	}

	public static TimelineTickSpacing TickSpacing( float pixelsPerFrame )
	{
		var minor = NiceFrameStep( 6 / MathF.Max( pixelsPerFrame, 0.0001f ) );
		var major = NiceFrameStep( 68 / MathF.Max( pixelsPerFrame, 0.0001f ) );
		return new TimelineTickSpacing(
			Math.Max( minor, 1 ),
			Math.Max( major, minor ) );
	}

	public static int ClampGroupFrameDelta(
		IEnumerable<int> selectedFrames,
		int requestedDelta,
		int lastFrame )
	{
		var frames = selectedFrames.ToArray();
		if ( frames.Length == 0 )
			return 0;
		return Math.Clamp(
			requestedDelta,
			-frames.Min(),
			lastFrame - frames.Max() );
	}

	public static HashSet<Guid> CombineKeySelection(
		IEnumerable<Guid> original,
		IEnumerable<Guid> hits,
		bool additive,
		bool toggle )
	{
		var result = additive || toggle
			? original.ToHashSet()
			: [];
		if ( toggle )
		{
			foreach ( var hit in hits )
			{
				if ( !result.Remove( hit ) )
					result.Add( hit );
			}
			return result;
		}

		result.UnionWith( hits );
		return result;
	}

	public static int TrackRowCount(
		WeaponAnimationDocument document,
		WeaponAnimationClip clip ) =>
		document.Rig.VisibilityParts.Count + clip.Tracks.Count + 1;

	private static TimelineFrameRange NormalizeRange(
		TimelineFrameRange range,
		int lastFrame,
		int? requestedSpan = null )
	{
		lastFrame = Math.Max( lastFrame, 1 );
		var minimum = Math.Min( MinimumVisibleFrameIntervals, lastFrame );
		var span = Math.Clamp(
			requestedSpan ?? range.Span,
			minimum,
			lastFrame );
		var start = Math.Clamp( range.StartFrame, 0, lastFrame - span );
		return new TimelineFrameRange( start, start + span );
	}

	private static int NiceFrameStep( float requested )
	{
		if ( requested <= 1 )
			return 1;

		var scale = 1;
		while ( scale < 1_000_000 )
		{
			foreach ( var multiplier in new[] { 1, 2, 5 } )
			{
				var candidate = multiplier * scale;
				if ( candidate >= requested )
					return candidate;
			}
			scale *= 10;
		}
		return scale;
	}
}
